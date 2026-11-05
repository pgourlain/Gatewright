// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Diagnostics.Metrics;
using Gatewright.Abstractions;
using Gatewright.Core.Policies;
using Microsoft.AspNetCore.Http;

namespace Gatewright.Core.Tests.Policies;

public sealed class PolicyPipelineTests
{
    private sealed class Delegating(Func<GatewayContext, ValueTask<PolicyResult>> run) : IPolicyInstance
    {
        public ValueTask<PolicyResult> InvokeAsync(GatewayContext context) => run(context);
    }

    private static CompiledPolicy Policy(string type, Func<GatewayContext, ValueTask<PolicyResult>> run, TimeSpan? timeout = null) =>
        new(type, new Delegating(run), timeout);

    private static CompiledPolicy Recording(string type, List<string> log, PolicyResult? result = null) =>
        Policy(type, _ =>
        {
            log.Add(type);
            return new ValueTask<PolicyResult>(result ?? PolicyResult.Continue);
        });

    private static PolicyPipeline Pipeline(
        IReadOnlyList<CompiledPolicy>? inbound = null,
        IReadOnlyList<CompiledPolicy>? onError = null,
        PolicyPipelineOptions? options = null) =>
        new(
            new Dictionary<PolicyPhase, IReadOnlyList<CompiledPolicy>>
            {
                [PolicyPhase.Inbound] = inbound ?? [],
                [PolicyPhase.OnError] = onError ?? [],
            },
            options);

    private static GatewayContext Context(CancellationToken token = default) =>
        new(new DefaultHttpContext(), "r", new ApiInfo("api", "/api"), cancellationToken: token);

    [Fact]
    public async Task Execute_AllContinue_RunsPoliciesInOrder()
    {
        var log = new List<string>();

        var result = await Pipeline([Recording("a", log), Recording("b", log), Recording("c", log)])
            .ExecuteAsync(Context(), PolicyPhase.Inbound);

        result.Kind.ShouldBe(PolicyResultKind.Continue);
        log.ShouldBe(["a", "b", "c"]);
    }

    [Fact]
    public async Task Execute_PolicyRejects_ShortCircuitsRemainingPolicies()
    {
        var log = new List<string>();
        var reject = PolicyResult.Reject(401, "unauthorized", "no");

        var result = await Pipeline([Recording("a", log), Recording("b", log, reject), Recording("c", log)])
            .ExecuteAsync(Context(), PolicyPhase.Inbound);

        result.ShouldBeSameAs(reject);
        log.ShouldBe(["a", "b"]);
    }

    [Fact]
    public async Task Execute_PolicyResponds_ShortCircuits()
    {
        var log = new List<string>();
        var respond = PolicyResult.Respond(new PolicyResponse(200));

        var result = await Pipeline([Recording("a", log, respond), Recording("b", log)])
            .ExecuteAsync(Context(), PolicyPhase.Inbound);

        result.Kind.ShouldBe(PolicyResultKind.Respond);
        log.ShouldBe(["a"]);
    }

    [Fact]
    public async Task Execute_EmptyPhase_Continues()
    {
        var result = await Pipeline().ExecuteAsync(Context(), PolicyPhase.Outbound);

        result.Kind.ShouldBe(PolicyResultKind.Continue);
    }

    [Fact]
    public async Task Execute_PolicyThrows_RunsOnErrorChainWithFailure()
    {
        var log = new List<string>();
        PolicyFailure? seen = null;
        var onError = Policy("handler", ctx =>
        {
            seen = ctx.Failure;
            log.Add("handler:" + ctx.Phase);
            return new ValueTask<PolicyResult>(PolicyResult.Reject(502, "handled", "handled"));
        });

        var result = await Pipeline(
            [Policy("bad", _ => throw new InvalidOperationException("boom")), Recording("never", log)],
            [onError]).ExecuteAsync(Context(), PolicyPhase.Inbound);

        result.Code.ShouldBe("handled");
        log.ShouldBe(["handler:OnError"]);
        seen.ShouldNotBeNull();
        seen.PolicyType.ShouldBe("bad");
        seen.IsTimeout.ShouldBeFalse();
        seen.Exception.ShouldBeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task Execute_PolicyThrowsWithoutOnError_Returns500()
    {
        var result = await Pipeline([Policy("bad", _ => throw new InvalidOperationException())])
            .ExecuteAsync(Context(), PolicyPhase.Inbound);

        result.StatusCode.ShouldBe(500);
        result.Code.ShouldBe("policy_error");
    }

    [Fact]
    public async Task Execute_OnErrorHandlerThrows_Returns500()
    {
        var result = await Pipeline(
            [Policy("bad", _ => throw new InvalidOperationException())],
            [Policy("handler", _ => throw new InvalidOperationException())]).ExecuteAsync(Context(), PolicyPhase.Inbound);

        result.Code.ShouldBe("policy_error");
    }

    [Fact]
    public async Task Execute_PolicyExceedsTimeout_Returns504AndFlagsTimeout()
    {
        PolicyFailure? seen = null;
        var slow = Policy(
            "slow",
            async ctx =>
            {
                await Task.Delay(Timeout.Infinite, ctx.CancellationToken);
                return PolicyResult.Continue;
            },
            TimeSpan.FromMilliseconds(50));
        var onError = Policy("handler", ctx =>
        {
            seen = ctx.Failure;
            return new ValueTask<PolicyResult>(PolicyResult.Continue);
        });

        var result = await Pipeline([slow], [onError]).ExecuteAsync(Context(), PolicyPhase.Inbound);

        result.StatusCode.ShouldBe(504);
        result.Code.ShouldBe("policy_timeout");
        seen.ShouldNotBeNull();
        seen.IsTimeout.ShouldBeTrue();
    }

    [Fact]
    public async Task Execute_DefaultTimeoutApplies_WhenPolicyHasNone()
    {
        var slow = Policy("slow", async ctx =>
        {
            await Task.Delay(Timeout.Infinite, ctx.CancellationToken);
            return PolicyResult.Continue;
        });

        var result = await Pipeline([slow], options: new PolicyPipelineOptions { DefaultPolicyTimeout = TimeSpan.FromMilliseconds(30) })
            .ExecuteAsync(Context(), PolicyPhase.Inbound);

        result.Code.ShouldBe("policy_timeout");
    }

    [Fact]
    public async Task Execute_RequestAborted_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        var policy = Policy("p", async ctx =>
        {
            await cts.CancelAsync();
            ctx.CancellationToken.ThrowIfCancellationRequested();
            return PolicyResult.Continue;
        });

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await Pipeline([policy]).ExecuteAsync(Context(cts.Token), PolicyPhase.Inbound));
    }

    [Fact]
    public async Task Execute_Completed_RestoresRequestCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        var context = Context(cts.Token);

        await Pipeline([Recording("a", [])]).ExecuteAsync(context, PolicyPhase.Inbound);

        context.CancellationToken.ShouldBe(cts.Token);
    }

    [Fact]
    public async Task Execute_Policy_RecordsDurationHistogram()
    {
        var recorded = new List<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == PolicyTelemetry.Name)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == "policy.type")
                {
                    recorded.Add((string)tag.Value!);
                }
            }
        });
        listener.Start();

        await Pipeline([Recording("measured", [])]).ExecuteAsync(Context(), PolicyPhase.Inbound);

        recorded.ShouldContain("measured");
    }

    [Fact]
    public async Task Execute_OnErrorPhaseRequested_Throws()
    {
        await Should.ThrowAsync<ArgumentOutOfRangeException>(
            async () => await Pipeline().ExecuteAsync(Context(), PolicyPhase.OnError));
    }

    private sealed class Wrapper(string name, List<string> log) : IBackendPolicyInstance
    {
        public ValueTask<PolicyResult> InvokeAsync(GatewayContext context) => new(PolicyResult.Continue);

        public async ValueTask<PolicyResult> InvokeBackendAsync(GatewayContext context, BackendCall invokeBackend)
        {
            log.Add(name + ":before");
            var result = await invokeBackend(context);
            log.Add(name + ":after");
            return result;
        }
    }

    private static PolicyPipeline BackendPipeline(params CompiledPolicy[] backend) =>
        new(new Dictionary<PolicyPhase, IReadOnlyList<CompiledPolicy>> { [PolicyPhase.Backend] = backend });

    [Fact]
    public async Task ExecuteBackend_Wrappers_NestOutermostFirstAroundTheCall()
    {
        var log = new List<string>();
        var pipeline = BackendPipeline(
            new CompiledPolicy("outer", new Wrapper("outer", log)),
            Recording("plain", log),
            new CompiledPolicy("inner", new Wrapper("inner", log)));

        var result = await pipeline.ExecuteBackendAsync(Context(), ctx =>
        {
            log.Add("call");
            return new ValueTask<PolicyResult>(PolicyResult.Continue);
        });

        result.Kind.ShouldBe(PolicyResultKind.Continue);
        log.ShouldBe(["outer:before", "plain", "inner:before", "call", "inner:after", "outer:after"]);
    }

    [Fact]
    public async Task ExecuteBackend_PlainPolicyRejects_BackendIsNotCalled()
    {
        var log = new List<string>();
        var pipeline = BackendPipeline(Recording("gate", log, PolicyResult.Reject(403, "no", "no")));

        var result = await pipeline.ExecuteBackendAsync(Context(), _ =>
        {
            log.Add("call");
            return new ValueTask<PolicyResult>(PolicyResult.Continue);
        });

        result.StatusCode.ShouldBe(403);
        log.ShouldBe(["gate"]);
    }

    [Fact]
    public async Task ExecuteBackend_WrapperThrows_Returns500()
    {
        var pipeline = BackendPipeline(Policy("bad", _ => throw new InvalidOperationException()));

        var result = await pipeline.ExecuteBackendAsync(Context(), _ => new ValueTask<PolicyResult>(PolicyResult.Continue));

        result.Code.ShouldBe("policy_error");
    }
}

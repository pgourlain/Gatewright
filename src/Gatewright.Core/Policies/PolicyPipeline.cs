// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using Gatewright.Abstractions;

namespace Gatewright.Core.Policies;

/// <summary>Runs the compiled policy chains of one operation, phase by phase.</summary>
public sealed class PolicyPipeline
{
    private static readonly IReadOnlyList<CompiledPolicy> NoPolicies = [];

    private readonly IReadOnlyDictionary<PolicyPhase, IReadOnlyList<CompiledPolicy>> _chains;
    private readonly TimeSpan _defaultTimeout;

    /// <summary>Initializes a new instance of the <see cref="PolicyPipeline"/> class.</summary>
    /// <param name="chains">The compiled chain of each phase.</param>
    /// <param name="options">The options, or <see langword="null"/> for defaults.</param>
    public PolicyPipeline(IReadOnlyDictionary<PolicyPhase, IReadOnlyList<CompiledPolicy>> chains, PolicyPipelineOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(chains);
        _chains = chains;
        _defaultTimeout = (options ?? new PolicyPipelineOptions()).DefaultPolicyTimeout;
    }

    /// <summary>Runs one phase. Stops at the first <c>Reject</c> or <c>Respond</c>; on exception or timeout runs the <see cref="PolicyPhase.OnError"/> chain.</summary>
    /// <param name="context">The request context.</param>
    /// <param name="phase">A single phase other than <see cref="PolicyPhase.OnError"/>.</param>
    /// <returns><see cref="PolicyResult.Continue"/> when every policy let the request through, otherwise the terminating result.</returns>
    /// <exception cref="OperationCanceledException">The request was aborted.</exception>
    public async ValueTask<PolicyResult> ExecuteAsync(GatewayContext context, PolicyPhase phase)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (phase is not (PolicyPhase.Inbound or PolicyPhase.Backend or PolicyPhase.Outbound))
        {
            throw new ArgumentOutOfRangeException(nameof(phase), phase, "Expected Inbound, Backend or Outbound.");
        }

        var requestToken = context.CancellationToken;
        try
        {
            context.Phase = phase;
            var chain = Chain(phase);
            for (var i = 0; i < chain.Count; i++)
            {
                var outcome = await RunAsync(context, chain[i], phase, requestToken).ConfigureAwait(false);
                if (outcome.Failure is { } failure)
                {
                    return await HandleFailureAsync(context, failure, requestToken).ConfigureAwait(false);
                }

                if (outcome.Result!.Kind != PolicyResultKind.Continue)
                {
                    return outcome.Result;
                }
            }

            return PolicyResult.Continue;
        }
        finally
        {
            context.CancellationToken = requestToken;
        }
    }

    /// <summary>
    /// Runs the <see cref="PolicyPhase.Backend"/> phase around the backend call: plain policies run before it, policies implementing
    /// <see cref="IBackendPolicyInstance"/> wrap it, outermost first.
    /// </summary>
    /// <param name="context">The request context.</param>
    /// <param name="backendCall">Forwards the request to the backend.</param>
    /// <returns><see cref="PolicyResult.Continue"/> when the call completed, otherwise the terminating result.</returns>
    /// <exception cref="OperationCanceledException">The request was aborted.</exception>
    public async ValueTask<PolicyResult> ExecuteBackendAsync(GatewayContext context, BackendCall backendCall)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(backendCall);

        var requestToken = context.CancellationToken;
        try
        {
            context.Phase = PolicyPhase.Backend;
            var chain = Chain(PolicyPhase.Backend);
            return await RunBackendFromAsync(context, chain, 0, backendCall, requestToken).ConfigureAwait(false);
        }
        finally
        {
            context.CancellationToken = requestToken;
        }
    }

    private async ValueTask<PolicyResult> RunBackendFromAsync(
        GatewayContext context,
        IReadOnlyList<CompiledPolicy> chain,
        int index,
        BackendCall backendCall,
        CancellationToken requestToken)
    {
        if (index >= chain.Count)
        {
            return await backendCall(context).ConfigureAwait(false);
        }

        var policy = chain[index];
        Outcome outcome;
        if (policy.Instance is IBackendPolicyInstance wrapper)
        {
            outcome = await RunAsync(
                context,
                policy,
                PolicyPhase.Backend,
                () => wrapper.InvokeBackendAsync(context, c => RunBackendFromAsync(c, chain, index + 1, backendCall, requestToken)),
                applyTimeout: false,
                requestToken).ConfigureAwait(false);
            if (outcome.Failure is { } wrapperFailure)
            {
                return await HandleFailureAsync(context, wrapperFailure, requestToken).ConfigureAwait(false);
            }

            return outcome.Result!;
        }

        outcome = await RunAsync(context, policy, PolicyPhase.Backend, requestToken).ConfigureAwait(false);
        if (outcome.Failure is { } failure)
        {
            return await HandleFailureAsync(context, failure, requestToken).ConfigureAwait(false);
        }

        if (outcome.Result!.Kind != PolicyResultKind.Continue)
        {
            return outcome.Result;
        }

        return await RunBackendFromAsync(context, chain, index + 1, backendCall, requestToken).ConfigureAwait(false);
    }

    private IReadOnlyList<CompiledPolicy> Chain(PolicyPhase phase) =>
        _chains.TryGetValue(phase, out var chain) ? chain : NoPolicies;

    private async ValueTask<PolicyResult> HandleFailureAsync(GatewayContext context, PolicyFailure failure, CancellationToken requestToken)
    {
        context.Failure = failure;
        context.Phase = PolicyPhase.OnError;
        var fallback = failure.IsTimeout
            ? PolicyResult.Reject(504, "policy_timeout", $"Policy '{failure.PolicyType}' timed out.")
            : PolicyResult.Reject(500, "policy_error", $"Policy '{failure.PolicyType}' failed.");

        var chain = Chain(PolicyPhase.OnError);
        for (var i = 0; i < chain.Count; i++)
        {
            var outcome = await RunAsync(context, chain[i], PolicyPhase.OnError, requestToken).ConfigureAwait(false);
            if (outcome.Failure is not null)
            {
                return fallback;
            }

            if (outcome.Result!.Kind != PolicyResultKind.Continue)
            {
                return outcome.Result;
            }
        }

        return fallback;
    }

    private ValueTask<Outcome> RunAsync(GatewayContext context, CompiledPolicy policy, PolicyPhase phase, CancellationToken requestToken) =>
        RunAsync(context, policy, phase, () => policy.Instance.InvokeAsync(context), applyTimeout: true, requestToken);

    private async ValueTask<Outcome> RunAsync(
        GatewayContext context,
        CompiledPolicy policy,
        PolicyPhase phase,
        Func<ValueTask<PolicyResult>> invoke,
        bool applyTimeout,
        CancellationToken requestToken)
    {
        var timeout = applyTimeout ? policy.Timeout ?? _defaultTimeout : TimeSpan.Zero;
        CancellationTokenSource? timeoutSource = null;
        if (timeout > TimeSpan.Zero)
        {
            timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(requestToken);
            timeoutSource.CancelAfter(timeout);
            context.CancellationToken = timeoutSource.Token;
        }
        else
        {
            context.CancellationToken = requestToken;
        }

        using var activity = PolicyTelemetry.Source.StartActivity("policy " + policy.Type);
        var start = Stopwatch.GetTimestamp();
        var status = "Unknown";
        try
        {
            var result = await invoke().ConfigureAwait(false);
            status = result.Kind.ToString();
            return new Outcome(result, null);
        }
        catch (OperationCanceledException) when (requestToken.IsCancellationRequested)
        {
            status = "Aborted";
            throw;
        }
        catch (OperationCanceledException ex) when (timeoutSource is { IsCancellationRequested: true })
        {
            status = "Timeout";
            return new Outcome(null, new PolicyFailure(policy.Type, phase, true, ex));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            status = "Error";
            return new Outcome(null, new PolicyFailure(policy.Type, phase, false, ex));
        }
        finally
        {
            timeoutSource?.Dispose();
            var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (context.Variables.TryGetValue(PolicyTraceEntry.VariableKey, out var trace) && trace is List<PolicyTraceEntry> entries)
            {
                lock (entries)
                {
                    entries.Add(new PolicyTraceEntry(policy.Type, phase.ToString(), Math.Round(elapsed, 3), status));
                }
            }

            PolicyTelemetry.Duration.Record(
                elapsed,
                new KeyValuePair<string, object?>("policy.type", policy.Type),
                new KeyValuePair<string, object?>("policy.phase", phase.ToString()),
                new KeyValuePair<string, object?>("policy.result", status));
            activity?.SetTag("policy.result", status);
        }
    }

    private readonly record struct Outcome(PolicyResult? Result, PolicyFailure? Failure);
}

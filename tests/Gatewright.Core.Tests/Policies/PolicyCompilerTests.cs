// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Text.Json;
using Gatewright.Abstractions;
using Gatewright.Core.Configuration;
using Gatewright.Core.Policies;

namespace Gatewright.Core.Tests.Policies;

public sealed class PolicyCompilerTests
{
    private sealed class FakePolicy(string type, PolicyPhase phases, string schema, Action? onCompile = null) : IGatewayPolicy
    {
        public int CompileCount { get; private set; }

        public string Type => type;

        public PolicyPhase Phases => phases;

        public JsonElement ConfigSchema { get; } = JsonElement.Parse(schema);

        public IPolicyInstance Compile(JsonElement config, PolicyCompilationContext context)
        {
            CompileCount++;
            onCompile?.Invoke();
            return new FakeInstance();
        }
    }

    private sealed class FakeInstance : IPolicyInstance
    {
        public ValueTask<PolicyResult> InvokeAsync(GatewayContext context) => new(PolicyResult.Continue);
    }

    private const string LimitSchema = """
        {"type":"object","properties":{"limit":{"type":"integer","minimum":1}},"required":["limit"]}
        """;

    private static PolicyReference Ref(string type, string? json = null) => new()
    {
        Type = type,
        Config = json is null ? null : JsonElement.Parse(json),
    };

    private static GatewayConfig ConfigWith(params PolicyReference[] inbound) => new()
    {
        Apis =
        [
            new ApiDefinition
            {
                Name = "orders",
                BasePath = "/orders",
                Backend = new BackendDefinition(),
                Policies = new PolicySet { Inbound = inbound },
                Operations =
                [
                    new OperationDefinition { Name = "get", Method = "GET", Path = "/" },
                    new OperationDefinition { Name = "post", Method = "POST", Path = "/" },
                ],
            },
        ],
    };

    private static PolicyCompiler Compiler(params IGatewayPolicy[] policies) =>
        new(new PolicyRegistry(policies), new ServiceProviderStub());

    private sealed class ServiceProviderStub : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    [Fact]
    public void Compile_ValidConfig_CompilesEachOperationChain()
    {
        var policy = new FakePolicy("rateLimit", PolicyPhase.Inbound, LimitSchema);

        var result = Compiler(policy).Compile(ConfigWith(Ref("rateLimit", """{"limit":5}""")));

        result.IsSuccess.ShouldBeTrue();
        result.Operations.Count.ShouldBe(2);
        result.Operations[0].Chains[PolicyPhase.Inbound].ShouldHaveSingleItem().Type.ShouldBe("rateLimit");
        policy.CompileCount.ShouldBe(2);
    }

    [Fact]
    public void Compile_InvalidConfig_ReportsApiOperationPolicyAndJsonPath()
    {
        var result = Compiler(new FakePolicy("rateLimit", PolicyPhase.Inbound, LimitSchema))
            .Compile(ConfigWith(Ref("rateLimit", """{"limit":0}""")));

        result.IsSuccess.ShouldBeFalse();
        result.Operations.ShouldBeEmpty();
        var error = result.Errors[0];
        error.Api.ShouldBe("orders");
        error.Operation.ShouldBe("get");
        error.PolicyType.ShouldBe("rateLimit");
        error.JsonPath.ShouldBe("/limit");
    }

    [Fact]
    public void Compile_MissingRequiredProperty_IsReported()
    {
        var result = Compiler(new FakePolicy("rateLimit", PolicyPhase.Inbound, LimitSchema))
            .Compile(ConfigWith(Ref("rateLimit")));

        result.Errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void Compile_UnknownPolicyType_IsReported()
    {
        var result = Compiler().Compile(ConfigWith(Ref("nope")));

        result.Errors.ShouldAllBe(e => e.PolicyType == "nope" && e.Message.Contains("Unknown"));
    }

    [Fact]
    public void Compile_PolicyUsedInUnsupportedPhase_IsReported()
    {
        var result = Compiler(new FakePolicy("x", PolicyPhase.Outbound, "{}")).Compile(ConfigWith(Ref("x")));

        result.Errors.ShouldContain(e => e.Message.Contains("phase Inbound"));
    }

    [Fact]
    public void Compile_PolicyThrows_ErrorAggregatedWithoutStopping()
    {
        var failing = new FakePolicy("boom", PolicyPhase.Inbound, "{}", () => throw new InvalidOperationException("bad"));
        var other = new FakePolicy("other", PolicyPhase.Inbound, "{}");

        var result = Compiler(failing, other).Compile(ConfigWith(Ref("boom"), Ref("other"), Ref("missing")));

        result.Errors.ShouldContain(e => e.PolicyType == "boom" && e.Message.Contains("bad"));
        result.Errors.ShouldContain(e => e.PolicyType == "missing");
        other.CompileCount.ShouldBe(2);
    }

    [Fact]
    public void Compile_UnknownSharedReference_IsReportedAsError()
    {
        var config = ConfigWith(new PolicyReference { Ref = "ghost" });

        var result = Compiler().Compile(config);

        result.IsSuccess.ShouldBeFalse();
    }

    [Fact]
    public void Registry_DuplicateType_Throws()
    {
        Should.Throw<ArgumentException>(() => new PolicyRegistry(
            [new FakePolicy("a", PolicyPhase.Inbound, "{}"), new FakePolicy("a", PolicyPhase.Inbound, "{}")]));
    }
}

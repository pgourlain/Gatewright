// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Text.Json;
using Gatewright.Abstractions;
using Gatewright.Core.Policies;

namespace Gatewright.Sdk.Tests;

public sealed class GatewayPolicyTests
{
    public enum Mode
    {
        Strict,
        Lenient,
    }

    public sealed record SampleConfig
    {
        [Description("How many things.")]
        public required int Count { get; init; }

        public string Label { get; init; } = "default";

        public Mode Mode { get; init; } = Mode.Strict;

        public List<string> Tags { get; init; } = [];
    }

    [GatewayPolicy("test.sample", Phases = PolicyPhase.Inbound | PolicyPhase.Outbound)]
    private sealed class SamplePolicy : GatewayPolicy<SampleConfig>
    {
        public SampleConfig? Compiled { get; private set; }

        protected override IPolicyInstance Create(SampleConfig config, PolicyCompilationContext context)
        {
            Compiled = config;
            return PolicyInstance.From(_ => PolicyResult.Continue);
        }
    }

    private sealed class Undecorated : GatewayPolicy<SampleConfig>
    {
        protected override IPolicyInstance Create(SampleConfig config, PolicyCompilationContext context) => throw new NotSupportedException();
    }

    private static PolicyCompilationContext Context() => new(new EmptyServices(), "test");

    private sealed class EmptyServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    [Fact]
    public void Attribute_ProvidesTypeAndPhases()
    {
        var policy = new SamplePolicy();

        policy.Type.ShouldBe("test.sample");
        policy.Phases.ShouldBe(PolicyPhase.Inbound | PolicyPhase.Outbound);
    }

    [Fact]
    public void MissingAttribute_ThrowsAClearError()
    {
        Should.Throw<InvalidOperationException>(() => new Undecorated()).Message.ShouldContain("[GatewayPolicy");
    }

    [Fact]
    public void Schema_IsGeneratedFromTheConfigurationType()
    {
        var schema = new SamplePolicy().ConfigSchema;

        schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ShouldBe(["count"]);
        schema.GetProperty("additionalProperties").GetBoolean().ShouldBeFalse();
        var properties = schema.GetProperty("properties");
        properties.GetProperty("count").GetProperty("type").GetString().ShouldBe("integer");
        properties.GetProperty("count").GetProperty("description").GetString().ShouldBe("How many things.");
        properties.GetProperty("tags").GetProperty("type").GetString().ShouldBe("array");
        properties.GetProperty("mode").GetProperty("enum").EnumerateArray().Select(e => e.GetString()).ShouldBe(["strict", "lenient"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("""{"count":3}""", 0)]
    [InlineData("""{"count":3,"label":"x","mode":"lenient","tags":["a"]}""", 0)]
    [InlineData("""{}""", 1)]
    [InlineData("""{"count":"three"}""", 1)]
    [InlineData("""{"count":3,"unknown":true}""", 1)]
    [InlineData("""{"count":3,"mode":"banana"}""", 1)]
    public void Registry_ValidatesConfigurationsAgainstTheGeneratedSchema(string json, int expectedErrors)
    {
        var registry = new PolicyRegistry([new SamplePolicy()]);

        var errors = registry.Validate("test.sample", JsonElement.Parse(json));

        (errors.Count > 0 ? 1 : 0).ShouldBe(expectedErrors);
    }

    [Fact]
    public void Compile_DeserializesTheConfigurationWithCamelCaseAndEnums()
    {
        var policy = new SamplePolicy();

        policy.Compile(JsonElement.Parse("""{"count":7,"label":"hello","mode":"lenient","tags":["a","b"]}"""), Context());

        policy.Compiled.ShouldNotBeNull();
        policy.Compiled.Count.ShouldBe(7);
        policy.Compiled.Label.ShouldBe("hello");
        policy.Compiled.Mode.ShouldBe(Mode.Lenient);
        policy.Compiled.Tags.ShouldBe(["a", "b"]);
    }

    [Fact]
    public async Task PolicyInstance_From_WrapsDelegates()
    {
        var sync = PolicyInstance.From(_ => PolicyResult.Reject(400, "x", "y"));
        var async = PolicyInstance.From(_ => new ValueTask<PolicyResult>(PolicyResult.Continue));
        var context = new GatewayContext(new Microsoft.AspNetCore.Http.DefaultHttpContext(), "r", new ApiInfo("a", "/a"));

        (await sync.InvokeAsync(context)).StatusCode.ShouldBe(400);
        (await async.InvokeAsync(context)).Kind.ShouldBe(PolicyResultKind.Continue);
    }
}

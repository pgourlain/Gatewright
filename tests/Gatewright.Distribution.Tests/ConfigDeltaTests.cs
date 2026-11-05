// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Configuration;
using Gatewright.Core.Snapshots;

namespace Gatewright.Distribution.Tests;

public sealed class ConfigDeltaTests
{
    private static ApiDefinition Api(string name, string backend = "http://a") => new()
    {
        Name = name,
        BasePath = "/" + name,
        Backend = new BackendDefinition { Destinations = [backend] },
    };

    private static GatewayConfig Config(long version, params ApiDefinition[] apis) => new() { Version = version, Apis = [.. apis.OrderBy(a => a.Name, StringComparer.Ordinal)] };

    [Fact]
    public void Compute_IdenticalConfigs_IsEmpty()
    {
        var config = Config(1, Api("a"), Api("b"));

        ConfigDeltaCalculator.Compute(config, config with { Version = 2 }).IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void ApplyAfterCompute_ReproducesTheNewConfiguration()
    {
        var before = Config(1, Api("a"), Api("b"), Api("c"));
        var after = Config(2, Api("a"), Api("b", "http://changed"), Api("d")) with
        {
            Products = [new ProductDefinition { Name = "gold" }],
            Subscriptions = [new SubscriptionDefinition { Id = "s", Product = "gold", KeyHashes = ["aa"] }],
            Policies = new Dictionary<string, PolicyReference> { ["p"] = new() { Type = "headers" } },
            GlobalPolicies = new PolicySet { Inbound = [new PolicyReference { Type = "cors" }] },
        };

        var delta = ConfigDeltaCalculator.Compute(before, after);
        var rebuilt = ConfigDeltaCalculator.Apply(before, delta, 2);

        delta.ApisRemoved.ShouldBe(["c"]);
        delta.ApisUpserted.Select(a => a.Name).ShouldBe(["b", "d"]);
        delta.GlobalPolicies.ShouldNotBeNull();
        delta.GatewayPolicies.ShouldBeNull();
        ConfigHasher.Hash(rebuilt).ShouldBe(ConfigHasher.Hash(after));
        rebuilt.Version.ShouldBe(2);
    }

    [Fact]
    public void Delta_IsMuchSmallerThanTheSnapshotForASmallChange()
    {
        var apis = Enumerable.Range(0, 200).Select(i => Api("api" + i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture))).ToArray();
        var before = Config(1, apis);
        var after = Config(2, [.. apis.Take(199), Api("api199", "http://moved")]);

        var delta = GatewayJson.Serialize(ConfigDeltaCalculator.Compute(before, after));

        delta.Length.ShouldBeLessThan(GatewayConfigSerializer.Serialize(after).Length / 20);
    }

    [Fact]
    public void Delta_SerializesAndRestores()
    {
        var delta = ConfigDeltaCalculator.Compute(Config(1, Api("a")), Config(2, Api("a", "http://x"), Api("b")));

        var restored = GatewayJson.Deserialize<ConfigDelta>(GatewayJson.Serialize(delta));

        restored.ApisUpserted.Select(a => a.Name).ShouldBe(["a", "b"]);
        restored.ApisUpserted[0].Backend.Destinations.ShouldBe(["http://x"]);
    }

    [Fact]
    public void Apply_RemovesPoliciesAndSubscriptions()
    {
        var before = Config(1, Api("a")) with
        {
            Policies = new Dictionary<string, PolicyReference> { ["old"] = new() { Type = "x" } },
            Products = [new ProductDefinition { Name = "p" }],
            Subscriptions = [new SubscriptionDefinition { Id = "s", Product = "p" }],
        };
        var after = Config(2, Api("a")) with { Products = [new ProductDefinition { Name = "p" }] };

        var rebuilt = ConfigDeltaCalculator.Apply(before, ConfigDeltaCalculator.Compute(before, after), 2);

        rebuilt.Policies.ShouldBeEmpty();
        rebuilt.Subscriptions.ShouldBeEmpty();
        ConfigHasher.Hash(rebuilt).ShouldBe(ConfigHasher.Hash(after));
    }
}

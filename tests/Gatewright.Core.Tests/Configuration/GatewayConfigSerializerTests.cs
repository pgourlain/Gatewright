// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Text.Json;
using Gatewright.Core.Configuration;

namespace Gatewright.Core.Tests.Configuration;

public sealed class GatewayConfigSerializerTests
{
    private static GatewayConfig Sample() => new()
    {
        Version = 7,
        GlobalPolicies = new PolicySet
        {
            Inbound = [new PolicyReference { Type = "headers", Config = JsonDocument.Parse("""{"set":{"x-a":"1"}}""").RootElement }],
        },
        Policies = new Dictionary<string, PolicyReference>
        {
            ["default-limit"] = new() { Type = "rateLimit", Config = JsonDocument.Parse("""{"limit":10}""").RootElement },
        },
        Products = [new ProductDefinition { Name = "gold", RequiresSubscription = true }],
        Apis =
        [
            new ApiDefinition
            {
                Name = "orders",
                BasePath = "/orders",
                Version = "v1",
                Products = ["gold"],
                Backend = new BackendDefinition
                {
                    Destinations = ["http://orders:8080"],
                    Timeout = TimeSpan.FromSeconds(30),
                    HttpVersion = "2",
                },
                Policies = new PolicySet
                {
                    Inbound = [new PolicyReference { Ref = "default-limit", Inheritance = PolicyInheritance.Override }],
                },
                Operations =
                [
                    new OperationDefinition
                    {
                        Name = "getOrder",
                        Method = "GET",
                        Path = "/{id}",
                        Policies = new PolicySet
                        {
                            Outbound = [new PolicyReference { Type = "cache", Inheritance = PolicyInheritance.Remove }],
                        },
                    },
                ],
            },
        ],
    };

    [Fact]
    public void RoundTrip_SampleConfig_ProducesIdenticalJson()
    {
        var json = GatewayConfigSerializer.Serialize(Sample());

        var roundTripped = GatewayConfigSerializer.Deserialize(json);

        GatewayConfigSerializer.Serialize(roundTripped).ShouldBe(json);
    }

    [Fact]
    public void Deserialize_SampleJson_RestoresStructure()
    {
        var config = GatewayConfigSerializer.Deserialize(GatewayConfigSerializer.Serialize(Sample()));

        config.Version.ShouldBe(7);
        var api = config.Apis.ShouldHaveSingleItem();
        api.Name.ShouldBe("orders");
        api.Backend.Timeout.ShouldBe(TimeSpan.FromSeconds(30));
        api.Policies.Inbound.ShouldHaveSingleItem().Inheritance.ShouldBe(PolicyInheritance.Override);
        api.Operations.ShouldHaveSingleItem().Policies.Outbound[0].Inheritance.ShouldBe(PolicyInheritance.Remove);
        config.Policies["default-limit"].Config!.Value.GetProperty("limit").GetInt32().ShouldBe(10);
    }

    [Fact]
    public void Serialize_EnumValues_AreWrittenAsStrings()
    {
        GatewayConfigSerializer.Serialize(Sample()).ShouldContain("\"inheritance\": \"Override\"");
    }

    [Fact]
    public void Deserialize_MinimalJson_UsesEmptyDefaults()
    {
        var config = GatewayConfigSerializer.Deserialize("""{"version":1}""");

        config.Apis.ShouldBeEmpty();
        config.Products.ShouldBeEmpty();
        config.GlobalPolicies.Inbound.ShouldBeEmpty();
    }

    [Fact]
    public void Deserialize_MissingRequiredProperty_Throws()
    {
        Should.Throw<JsonException>(
            () => GatewayConfigSerializer.Deserialize("""{"apis":[{"basePath":"/x","backend":{}}]}"""));
    }

    [Fact]
    public void Deserialize_Null_Throws()
    {
        Should.Throw<JsonException>(() => GatewayConfigSerializer.Deserialize("null"));
    }
}

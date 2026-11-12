// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Text.Json;
using Gatewright.Core.Configuration;

namespace Gatewright.IntegrationTests;

public sealed class RewritePolicyTests
{
    private static Task<TestGateway> StartAsync(string rewrite) => TestGateway.StartAsync(url => new GatewayConfig
    {
        Apis = [TestGateway.Api(url, new PolicySet { Inbound = [TestGateway.Policy("rewrite", rewrite)] })],
    });

    private static async Task<(string Path, string Query)> EchoAsync(TestGateway gateway, string url)
    {
        var json = JsonElement.Parse(await gateway.Client.GetStringAsync(url));
        return (json.GetProperty("path").GetString()!, json.GetProperty("query").GetString() ?? "");
    }

    [Fact]
    public async Task Path_RegexRewrite_IsAppliedRelativeToBasePath()
    {
        await using var gateway = await StartAsync("""{"path":{"regex":"^/items/(?<id>[0-9]+)$","replacement":"/v2/things/${id}"}}""");

        (await EchoAsync(gateway, "/api/items/42")).Path.ShouldBe("/v2/things/42");
    }

    [Fact]
    public async Task Path_NoMatch_IsLeftUntouched()
    {
        await using var gateway = await StartAsync("""{"path":{"regex":"^/items/(.*)$","replacement":"/v2/$1"}}""");

        (await EchoAsync(gateway, "/api/other/1")).Path.ShouldBe("/other/1");
    }

    [Fact]
    public async Task Query_SetAndRemove_RewriteTheQueryString()
    {
        await using var gateway = await StartAsync("""{"query":{"set":{"source":"{api.name}","page":"1"},"remove":["debug"]}}""");

        var (_, query) = await EchoAsync(gateway, "/api/x?keep=1&debug=true&page=9");

        query.ShouldContain("keep=1");
        query.ShouldContain("source=api");
        query.ShouldContain("page=1");
        query.ShouldNotContain("page=9");
        query.ShouldNotContain("debug");
    }

    [Fact]
    public async Task Config_InvalidRegex_IsRejectedAtCompile()
    {
        await Should.ThrowAsync<InvalidOperationException>(() => StartAsync("""{"path":{"regex":"([","replacement":"/x"}}"""));
    }
}

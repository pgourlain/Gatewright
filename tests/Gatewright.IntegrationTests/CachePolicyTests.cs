// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Net;
using System.Net.Http.Headers;
using Gatewright.Core.Configuration;
using Gatewright.Policies.Cache;
using Microsoft.Extensions.DependencyInjection;

namespace Gatewright.IntegrationTests;

public sealed class CachePolicyTests
{
    private static Task<TestGateway> StartAsync(string cache = """{"durationSeconds":60}""") => TestGateway.StartAsync(url => new GatewayConfig
    {
        Apis =
        [
            TestGateway.Api(
                url,
                null,
                "api",
                new OperationDefinition
                {
                    Name = "read",
                    Method = "GET",
                    Path = "/{**rest}",
                    Policies = new PolicySet { Inbound = [TestGateway.Policy("cache", cache)] },
                },
                new OperationDefinition
                {
                    Name = "write",
                    Method = "POST",
                    Path = "/{**rest}",
                    Policies = new PolicySet { Inbound = [TestGateway.Policy("cache", cache)] },
                }),
        ],
    });

    private static async Task<HttpResponseMessage> GetAsync(TestGateway gateway, string path, string? bearer = null, string? header = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (bearer is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        if (header is not null)
        {
            request.Headers.Add("x-variant", header);
        }

        return await gateway.Client.SendAsync(request);
    }

    private static string Cache(HttpResponseMessage response) => response.Headers.GetValues("X-Cache").Single();

    [Fact]
    public async Task Get_SecondCall_IsServedFromCacheWithoutBackend()
    {
        await using var gateway = await StartAsync();

        var first = await GetAsync(gateway, "/api/items");
        var second = await GetAsync(gateway, "/api/items");

        Cache(first).ShouldBe("MISS");
        Cache(second).ShouldBe("HIT");
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await second.Content.ReadAsStringAsync()).ShouldBe(await first.Content.ReadAsStringAsync());
        second.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
        gateway.BackendCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Get_DifferentQuery_IsCachedSeparately_AndQueryOrderDoesNotMatter()
    {
        await using var gateway = await StartAsync();

        await GetAsync(gateway, "/api/items?a=1&b=2");
        Cache(await GetAsync(gateway, "/api/items?b=2&a=1")).ShouldBe("HIT");
        Cache(await GetAsync(gateway, "/api/items?a=9&b=2")).ShouldBe("MISS");
    }

    [Fact]
    public async Task VaryByQuery_Restricted_IgnoresOtherParameters()
    {
        await using var gateway = await StartAsync("""{"durationSeconds":60,"varyByQuery":["page"]}""");

        await GetAsync(gateway, "/api/items?page=1&noise=a");
        Cache(await GetAsync(gateway, "/api/items?page=1&noise=b")).ShouldBe("HIT");
        Cache(await GetAsync(gateway, "/api/items?page=2")).ShouldBe("MISS");
    }

    [Fact]
    public async Task VaryByHeader_SeparatesVariants()
    {
        await using var gateway = await StartAsync("""{"durationSeconds":60,"varyByHeader":["x-variant"]}""");

        await GetAsync(gateway, "/api/items", header: "a");
        Cache(await GetAsync(gateway, "/api/items", header: "a")).ShouldBe("HIT");
        Cache(await GetAsync(gateway, "/api/items", header: "b")).ShouldBe("MISS");
    }

    [Fact]
    public async Task AuthenticatedRequests_BypassTheCacheByDefault()
    {
        await using var gateway = await StartAsync();

        await GetAsync(gateway, "/api/items", bearer: "token-of-alice");
        var second = await GetAsync(gateway, "/api/items", bearer: "token-of-alice");
        var anonymous = await GetAsync(gateway, "/api/items");

        second.Headers.Contains("X-Cache").ShouldBeFalse();
        Cache(anonymous).ShouldBe("MISS");
        gateway.BackendCalls.ShouldBe(3);
    }

    [Fact]
    public async Task AuthenticatedRequests_AreCachedWhenAllowedAndVaried()
    {
        await using var gateway = await StartAsync("""{"durationSeconds":60,"allowAuthenticated":true,"varyByHeader":["Authorization"]}""");

        await GetAsync(gateway, "/api/items", bearer: "alice");
        Cache(await GetAsync(gateway, "/api/items", bearer: "alice")).ShouldBe("HIT");
        Cache(await GetAsync(gateway, "/api/items", bearer: "bob")).ShouldBe("MISS");
    }

    [Fact]
    public async Task NonGetAndErrorResponses_AreNotCached()
    {
        await using var gateway = await StartAsync();

        using var post = new HttpRequestMessage(HttpMethod.Post, "/api/items");
        (await gateway.Client.SendAsync(post)).Headers.Contains("X-Cache").ShouldBeFalse();
        await GetAsync(gateway, "/api/status/500");
        var again = await GetAsync(gateway, "/api/status/500");

        Cache(again).ShouldBe("MISS");
        again.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task EvictByTag_DropsCachedEntries()
    {
        await using var gateway = await StartAsync("""{"durationSeconds":60,"tags":["catalog"]}""");
        await GetAsync(gateway, "/api/items");
        Cache(await GetAsync(gateway, "/api/items")).ShouldBe("HIT");

        await gateway.Services.GetRequiredService<CacheInvalidator>().EvictByTagAsync("catalog", CancellationToken.None);

        Cache(await GetAsync(gateway, "/api/items")).ShouldBe("MISS");
    }

    [Fact]
    public async Task EvictByAutomaticApiTag_DropsCachedEntries()
    {
        await using var gateway = await StartAsync();
        await GetAsync(gateway, "/api/items");

        await gateway.Services.GetRequiredService<CacheInvalidator>().EvictByTagAsync("api:api", CancellationToken.None);

        Cache(await GetAsync(gateway, "/api/items")).ShouldBe("MISS");
    }

    [Fact]
    public async Task Config_NonPositiveDuration_IsRejected()
    {
        await Should.ThrowAsync<InvalidOperationException>(() => StartAsync("""{"durationSeconds":0}"""));
    }
}

// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Text.Json;
using Gatewright.Abstractions;
using Gatewright.Core.Security;
using Gatewright.Policies.RateLimit;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Gatewright.State.Redis.Tests;

[Collection(RedisTestGroup.Name)]
public sealed class RedisSharedRateLimitTests(RedisFixture redis)
{
    private sealed class CountingStore(IRateLimitStore inner) : IRateLimitStore
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public ValueTask<RateLimitReservation> ReserveAsync(string key, RateLimitBudget budget, int requested, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return inner.ReserveAsync(key, budget, requested, cancellationToken);
        }
    }

    private static IPolicyInstance CompilePolicy(IRateLimitStore store, string config)
    {
        var services = new ServiceCollection();
        services.AddSingleton(store);
        services.AddSingleton<RateLimiterRegistry>();
        using var provider = services.BuildServiceProvider();
        return new RateLimitPolicy().Compile(JsonElement.Parse(config), new PolicyCompilationContext(provider, "api:test"));
    }

    private static GatewayContext Context() => new(new DefaultHttpContext(), "r", new ApiInfo("api", "/api"));

    [Fact]
    public async Task ThreeGateways_ShareOneBudgetThroughRedis_WithinTheDocumentedTolerance()
    {
        var prefix = $"t{Guid.NewGuid():N}:";
        await using var guardA = redis.CreateGuard(prefix);
        await using var guardB = redis.CreateGuard(prefix);
        await using var guardC = redis.CreateGuard(prefix);
        var stores = new[] { guardA, guardB, guardC }.Select(g => new CountingStore(new RedisRateLimitStore(g))).ToArray();
        const string config = """{"shared":true,"limit":300,"periodSeconds":3600,"partitionBy":"global","batchSize":20,"name":"global"}""";
        var policies = stores.Select(s => CompilePolicy(s, config)).ToArray();

        var allowed = 0;
        await Parallel.ForAsync(0, 1200, async (i, ct) =>
        {
            var result = await policies[i % 3].InvokeAsync(Context());
            if (result.Kind == PolicyResultKind.Continue)
            {
                Interlocked.Increment(ref allowed);
            }
        });

        // Global limit 300; each of the 3 gateways may hold one 20-token batch beyond what it consumed.
        allowed.ShouldBeGreaterThanOrEqualTo(300 - (3 * 20));
        allowed.ShouldBeLessThanOrEqualTo(300 + (3 * 20));
    }

    [Fact]
    public async Task NominalLoad_UsesFewerThanOneRedisCallPerTwentyRequests()
    {
        await using var guard = redis.CreateGuard();
        var store = new CountingStore(new RedisRateLimitStore(guard));
        var policy = CompilePolicy(store, """{"shared":true,"limit":100000,"periodSeconds":10,"partitionBy":"global","name":"load"}""");

        for (var i = 0; i < 2000; i++)
        {
            (await policy.InvokeAsync(Context())).Kind.ShouldBe(PolicyResultKind.Continue);
        }

        store.Calls.ShouldBeLessThan(2000 / 20);
    }

    [Fact]
    public async Task RedisDown_FailOpenAllows_FailClosedReturns503()
    {
        var down = "127.0.0.1:1,connectTimeout=200";
        await using var guard = redis.CreateGuard(connectionString: down, timeoutMs: 100, threshold: 2);
        var store = new RedisRateLimitStore(guard);
        var open = CompilePolicy(store, """{"shared":true,"limit":1,"periodSeconds":60,"partitionBy":"global","name":"open"}""");
        var closed = CompilePolicy(store, """{"shared":true,"limit":1,"periodSeconds":60,"partitionBy":"global","name":"closed","onStoreFailure":"failClosed"}""");

        for (var i = 0; i < 5; i++)
        {
            (await open.InvokeAsync(Context())).Kind.ShouldBe(PolicyResultKind.Continue);
        }

        var rejected = await closed.InvokeAsync(Context());
        rejected.Kind.ShouldBe(PolicyResultKind.Reject);
        rejected.StatusCode.ShouldBe(503);
        rejected.Code.ShouldBe("rate_limit_unavailable");
    }
}

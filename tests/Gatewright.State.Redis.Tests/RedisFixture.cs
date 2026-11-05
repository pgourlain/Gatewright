// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.Redis;

namespace Gatewright.State.Redis.Tests;

/// <summary>A real Redis (Testcontainers) shared by the tests of this assembly. Requires Docker.</summary>
public sealed class RedisFixture : IAsyncLifetime
{
    private readonly RedisContainer _container = new RedisBuilder("redis:7-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public RedisContainer Container => _container;

    public Task InitializeAsync() => _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();

    public RedisGuard CreateGuard(string? prefix = null, string? connectionString = null, int timeoutMs = 1000, int threshold = 5, TimeProvider? time = null) =>
        new(
            Options.Create(new RedisOptions
            {
                ConnectionString = connectionString ?? ConnectionString,
                KeyPrefix = prefix ?? $"t{Guid.NewGuid():N}:",
                CommandTimeoutMilliseconds = timeoutMs,
                FailureThreshold = threshold,
                BreakSeconds = 30,
            }),
            time ?? TimeProvider.System,
            NullLogger<RedisGuard>.Instance);
}

[CollectionDefinition(Name)]
public sealed class RedisTestGroup : ICollectionFixture<RedisFixture>
{
    public const string Name = "redis";
}

// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Configuration;
using Gatewright.Core.Snapshots;
using Gatewright.DataPlane;
using Gatewright.Sdk;
using Microsoft.AspNetCore.TestHost;

namespace Gatewright.Testing;

/// <summary>
/// Starts a complete gateway (mode <c>All</c>, in memory) in front of a <see cref="SimulatedBackend"/> so that custom policies can be tested
/// through real requests. Use it from xUnit, NUnit or MSTest alike.
/// </summary>
public sealed class GatewayTestHost : IAsyncDisposable
{
    private readonly GatewayApplication _app;
    private readonly string _directory;
    private readonly string _configFile;

    private GatewayTestHost(GatewayApplication app, SimulatedBackend backend, string directory, string configFile)
    {
        _app = app;
        Backend = backend;
        _directory = directory;
        _configFile = configFile;
        Client = app.App.GetTestClient();
    }

    /// <summary>Gets the HTTP client that sends requests through the gateway.</summary>
    public HttpClient Client { get; }

    /// <summary>Gets the simulated backend behind the gateway.</summary>
    public SimulatedBackend Backend { get; }

    /// <summary>Gets the gateway services (for example to resolve a policy dependency you registered).</summary>
    public IServiceProvider Services => _app.Services;

    /// <summary>Starts a gateway with your policies and a configuration.</summary>
    /// <param name="configure">Registers custom policies and services, the same way your real host does.</param>
    /// <param name="config">Builds the gateway configuration from the simulated backend (use <see cref="Api"/> for the destination).</param>
    /// <returns>The running host, ready to serve requests.</returns>
    /// <exception cref="InvalidOperationException">The configuration is invalid; the message lists every error.</exception>
    public static async Task<GatewayTestHost> StartAsync(Action<GatewayHostBuilder>? configure, Func<SimulatedBackend, GatewayConfig> config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var backend = await SimulatedBackend.StartAsync().ConfigureAwait(false);
        var directory = Directory.CreateTempSubdirectory("gatewright-testhost-").FullName;
        var configFile = Path.Combine(directory, "config.json");
        try
        {
            await File.WriteAllTextAsync(configFile, GatewayConfigSerializer.Serialize(config(backend))).ConfigureAwait(false);
            var builder = GatewayHost.CreateBuilder();
            builder.Builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gateway:Mode"] = "All",
                ["Gateway:ConfigFile"] = configFile,
                ["Gateway:ConfigPollInterval"] = "00:00:00.050",
            });
            builder.Builder.Logging.ClearProviders();
            configure?.Invoke(builder);
            var app = builder.Build();
            await app.StartAsync().ConfigureAwait(false);
            var host = new GatewayTestHost(app, backend, directory, configFile);
            await host.WaitUntilAppliedAsync(expectedHash: null).ConfigureAwait(false);
            return host;
        }
        catch
        {
            await backend.DisposeAsync().ConfigureAwait(false);
            Directory.Delete(directory, recursive: true);
            throw;
        }
    }

    /// <summary>Builds an API definition that routes <c>/{name}/**</c> to the simulated backend.</summary>
    /// <param name="backend">The simulated backend.</param>
    /// <param name="name">The API name, also used as base path (<c>/name</c>).</param>
    /// <param name="policies">The policies declared at API scope.</param>
    /// <param name="operations">Optional operations; without any, the whole base path is routed.</param>
    /// <returns>The API definition.</returns>
    public static ApiDefinition Api(SimulatedBackend backend, string name = "api", PolicySet? policies = null, params OperationDefinition[] operations)
    {
        ArgumentNullException.ThrowIfNull(backend);
        return new ApiDefinition
        {
            Name = name,
            BasePath = "/" + name,
            Backend = new BackendDefinition { Destinations = [backend.Address.ToString().TrimEnd('/')] },
            Policies = policies ?? PolicySet.Empty,
            Operations = operations,
        };
    }

    /// <summary>Builds a policy reference from a JSON configuration.</summary>
    /// <param name="type">The policy type.</param>
    /// <param name="configJson">The policy configuration as JSON, or <see langword="null"/>.</param>
    /// <returns>The reference.</returns>
    public static PolicyReference Policy(string type, string? configJson = null) => new()
    {
        Type = type,
        Config = configJson is null ? null : System.Text.Json.JsonElement.Parse(configJson),
    };

    /// <summary>Replaces the configuration and waits until the gateway applied it.</summary>
    /// <param name="config">The new configuration.</param>
    /// <returns>A task completing when the new configuration is serving.</returns>
    public async Task UpdateConfigAsync(GatewayConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var expected = ConfigHasher.Hash(config);
        await File.WriteAllTextAsync(_configFile, GatewayConfigSerializer.Serialize(config)).ConfigureAwait(false);
        await WaitUntilAppliedAsync(expected).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.DisposeAsync().ConfigureAwait(false);
        await Backend.DisposeAsync().ConfigureAwait(false);
        Directory.Delete(_directory, recursive: true);
    }

    private async Task WaitUntilAppliedAsync(string? expectedHash)
    {
        var registry = Services.GetRequiredService<PipelineRegistry>();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (registry.Current is { } current && (expectedHash is null || current.Hash == expectedHash))
            {
                return;
            }

            await Task.Delay(25).ConfigureAwait(false);
        }

        throw new InvalidOperationException("The gateway did not apply the configuration in time. It is probably invalid: check the application logs (errors list every problem).");
    }
}

// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.ControlPlane.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Gatewright.ControlPlane;

/// <summary>Creates and seeds the control plane database before the application starts serving.</summary>
public sealed class DatabaseInitializer : IHostedService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ControlPlaneOptions _options;

    /// <summary>Initializes a new instance of the <see cref="DatabaseInitializer"/> class.</summary>
    /// <param name="scopes">The scope factory.</param>
    /// <param name="options">The control plane options.</param>
    public DatabaseInitializer(IServiceScopeFactory scopes, IOptions<ControlPlaneOptions> options)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(options);
        _scopes = scopes;
        _options = options.Value;
    }

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopes.CreateScope();
        await ControlPlaneDatabase.InitializeAsync(
            scope.ServiceProvider.GetRequiredService<GatewrightDbContext>(),
            _options.SeedSample,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

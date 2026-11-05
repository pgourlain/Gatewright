// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Abstractions;
using Gatewright.ControlPlane.AdminApi;
using Gatewright.ControlPlane.Distribution;
using Gatewright.Core.Hosting;
using Gatewright.DataPlane;
using Gatewright.DataPlane.Tls;
using Gatewright.Sdk.Internal;
using Microsoft.Extensions.Configuration.Memory;

namespace Gatewright.Sdk;

/// <summary>Entry point of a Gatewright host: the standard gateway (all modes) plus any custom policies you add.</summary>
public static class GatewayHost
{
    /// <summary>Creates a builder preconfigured with every mode (<c>All</c>, <c>ControlPlane</c>, <c>DataPlane</c>), selected by <c>Gateway:Mode</c>.</summary>
    /// <param name="args">The command-line arguments.</param>
    /// <returns>The builder.</returns>
    public static GatewayHostBuilder CreateBuilder(string[]? args = null)
    {
        var builder = WebApplication.CreateBuilder(args ?? []);
        return new GatewayHostBuilder(builder);
    }
}

/// <summary>Builds a Gatewright host. Register custom policies with <see cref="AddPolicy{TPolicy}"/> before <see cref="Build"/>.</summary>
public sealed class GatewayHostBuilder
{
    internal GatewayHostBuilder(WebApplicationBuilder builder)
    {
        Builder = builder;
        builder.WebHost.UseGatewrightTls(builder.Configuration);
        builder.AddGatewrightTelemetry();

        // One log line per SQL command (the pollers run several per second) hides everything else. This is a default with the lowest priority:
        // any configuration source (appsettings, environment, command line) can raise the category back to Information to debug.
        builder.Configuration.Sources.Insert(0, new MemoryConfigurationSource
        {
            InitialData = new Dictionary<string, string?> { ["Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command"] = "Warning" },
        });
    }

    /// <summary>Gets the underlying ASP.NET Core builder, for advanced configuration (logging, extra services, Kestrel…).</summary>
    public WebApplicationBuilder Builder { get; }

    /// <summary>Gets the service collection.</summary>
    public IServiceCollection Services => Builder.Services;

    /// <summary>Gets the application configuration.</summary>
    public ConfigurationManager Configuration => Builder.Configuration;

    /// <summary>Registers a custom policy type. It is available to the data plane, validated by the control plane and listed in the policy catalog.</summary>
    /// <typeparam name="TPolicy">The policy class, usually deriving from <see cref="GatewayPolicy{TConfig}"/>.</typeparam>
    /// <returns>This builder.</returns>
    public GatewayHostBuilder AddPolicy<TPolicy>()
        where TPolicy : class, IGatewayPolicy
    {
        Services.AddSingleton<IGatewayPolicy, TPolicy>();
        return this;
    }

    /// <summary>Registers a custom policy instance.</summary>
    /// <param name="policy">The policy.</param>
    /// <returns>This builder.</returns>
    public GatewayHostBuilder AddPolicy(IGatewayPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        Services.AddSingleton(policy);
        return this;
    }

    /// <summary>Builds the host and maps the endpoints of the selected mode.</summary>
    /// <returns>The application.</returns>
    public GatewayApplication Build()
    {
        Services.AddGatewright(Configuration, Builder.Environment);
        var app = Builder.Build();
        var components = app.Services.GetServices<GatewayComponent>().Select(c => c.Name).ToHashSet();
        if (components.Contains("forwarded-headers"))
        {
            app.UseGatewrightForwardedHeaders();
        }

        if (components.Contains("data-plane"))
        {
            app.UseMiddleware<Gatewright.DataPlane.Telemetry.AccessLogMiddleware>();
            app.UseMiddleware<HttpsPolicyMiddleware>();
        }

        if (components.Contains("distribution:grpc-server"))
        {
            app.MapDistributionServer();
        }

        if (components.Contains("admin-api"))
        {
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapAdminApi();
        }

        app.MapGet("/", () => "Gatewright");
        app.MapGatewrightHealth();
        if (components.Contains("data-plane"))
        {
            app.MapGatewrightDataPlane();
        }

        return new GatewayApplication(app);
    }
}

/// <summary>A configured Gatewright application.</summary>
public sealed class GatewayApplication : IAsyncDisposable
{
    internal GatewayApplication(WebApplication app) => App = app;

    /// <summary>Gets the underlying ASP.NET Core application.</summary>
    public WebApplication App { get; }

    /// <summary>Gets the application services.</summary>
    public IServiceProvider Services => App.Services;

    /// <summary>Runs the application and blocks until it shuts down.</summary>
    public void Run() => App.Run();

    /// <summary>Runs the application until it shuts down.</summary>
    /// <param name="cancellationToken">Stops the application.</param>
    /// <returns>A task completing when the application stopped.</returns>
    public Task RunAsync(CancellationToken cancellationToken = default) => App.RunAsync(cancellationToken);

    /// <summary>Starts the application without blocking.</summary>
    /// <param name="cancellationToken">Cancels the start.</param>
    /// <returns>A task completing when the application started.</returns>
    public Task StartAsync(CancellationToken cancellationToken = default) => App.StartAsync(cancellationToken);

    /// <summary>Stops the application.</summary>
    /// <param name="cancellationToken">Cancels the stop.</param>
    /// <returns>A task completing when the application stopped.</returns>
    public Task StopAsync(CancellationToken cancellationToken = default) => App.StopAsync(cancellationToken);

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => App.DisposeAsync();
}

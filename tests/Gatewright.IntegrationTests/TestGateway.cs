// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Text.Json;
using Gatewright.Abstractions;
using Gatewright.Core.Configuration;
using Gatewright.Core.Policies;
using Gatewright.Core.Snapshots;
using Gatewright.Core.Validation;
using Gatewright.DataPlane;
using Gatewright.Policies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Gatewright.IntegrationTests;

/// <summary>An in-memory gateway (TestServer) in front of an echo backend (real Kestrel), configured from a <see cref="GatewayConfig"/>.</summary>
internal sealed class TestGateway : IAsyncDisposable
{
    private readonly WebApplication _backend;
    private readonly WebApplication _gateway;
    private int _backendCalls;

    private TestGateway(WebApplication backend, WebApplication gateway, HttpClient client)
    {
        _backend = backend;
        _gateway = gateway;
        Client = client;
    }

    public HttpClient Client { get; }

    public IServiceProvider Services => _gateway.Services;

    public TestServer Server => _gateway.GetTestServer();

    public string GatewayUrl => _gateway.Urls.First();

    public int BackendCalls => Volatile.Read(ref _backendCalls);

    public string BackendUrl => _backend.Urls.First();

    public static async Task<TestGateway> StartAsync(
        Func<string, GatewayConfig> buildConfig,
        Action<IServiceCollection>? configureServices = null,
        IEnumerable<IGatewayPolicy>? extraPolicies = null,
        bool useKestrel = false)
    {
        TestGateway? holder = null;
        var flakyCalls = new System.Collections.Concurrent.ConcurrentDictionary<string, int>();
        var backendBuilder = WebApplication.CreateSlimBuilder();
        backendBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        var backend = backendBuilder.Build();
        backend.UseWebSockets();
        backend.Map("/{**path}", async (HttpContext ctx) =>
        {
            if (ctx.WebSockets.IsWebSocketRequest)
            {
                using var socket = await ctx.WebSockets.AcceptWebSocketAsync();
                var buffer = new byte[4096];
                while (true)
                {
                    var received = await socket.ReceiveAsync(buffer, ctx.RequestAborted);
                    if (received.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
                    {
                        await socket.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "bye", ctx.RequestAborted);
                        return;
                    }

                    await socket.SendAsync(buffer.AsMemory(0, received.Count), received.MessageType, received.EndOfMessage, ctx.RequestAborted);
                }
            }

            if (holder is not null)
            {
                Interlocked.Increment(ref holder._backendCalls);
            }

            if (ctx.Request.Path.StartsWithSegments("/slow", out var slow) && int.TryParse(slow.Value?.Trim('/'), out var delay))
            {
                await Task.Delay(delay, ctx.RequestAborted);
                await ctx.Response.WriteAsync("slow-done");
                return;
            }

            if (ctx.Request.Path.StartsWithSegments("/flaky", out var flaky) && int.TryParse(flaky.Value?.Trim('/'), out var failures))
            {
                var seen = flakyCalls.AddOrUpdate(ctx.Request.QueryString.Value ?? "", 1, (_, n) => n + 1);
                if (seen <= failures)
                {
                    ctx.Abort();
                    return;
                }

                await ctx.Response.WriteAsync("flaky-ok");
                return;
            }

            if (ctx.Request.Path.StartsWithSegments("/slowfirst", out var slowFirst) && int.TryParse(slowFirst.Value?.Trim('/'), out var slowCalls))
            {
                var seen = flakyCalls.AddOrUpdate("slowfirst" + ctx.Request.QueryString.Value, 1, (_, n) => n + 1);
                if (seen <= slowCalls)
                {
                    await Task.Delay(Timeout.Infinite, ctx.RequestAborted);
                }

                await ctx.Response.WriteAsync("slowfirst-ok");
                return;
            }

            if (ctx.Request.Path.StartsWithSegments("/status", out var rest) && int.TryParse(rest.Value?.Trim('/'), out var code))
            {
                ctx.Response.StatusCode = code;
                return;
            }

            ctx.Response.ContentType = "application/json";
            await JsonSerializer.SerializeAsync(ctx.Response.Body, new
            {
                method = ctx.Request.Method,
                path = ctx.Request.Path.Value,
                query = ctx.Request.QueryString.Value,
                headers = ctx.Request.Headers.ToDictionary(h => h.Key.ToLowerInvariant(), h => h.Value.ToString()),
            });
        });
        await backend.StartAsync();

        var config = buildConfig(backend.Urls.First());
        var gatewayBuilder = WebApplication.CreateBuilder();
        if (useKestrel)
        {
            gatewayBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        }
        else
        {
            gatewayBuilder.WebHost.UseTestServer();
        }

        gatewayBuilder.Services.AddGatewrightDataPlane();
        gatewayBuilder.Services.AddBuiltInPolicies();
        foreach (var policy in extraPolicies ?? [])
        {
            gatewayBuilder.Services.AddSingleton(policy);
        }

        configureServices?.Invoke(gatewayBuilder.Services);
        var gateway = gatewayBuilder.Build();
        gateway.Use(async (context, next) =>
        {
            if (context.Request.Headers.TryGetValue("x-test-remote-ip", out var ip) && System.Net.IPAddress.TryParse(ip, out var address))
            {
                context.Connection.RemoteIpAddress = address;
            }

            await next();
        });
        gateway.MapGatewrightDataPlane();

        var registry = new PolicyRegistry(gateway.Services.GetServices<IGatewayPolicy>());
        var translator = new SnapshotTranslator(new GatewayConfigValidator(registry), new PolicyCompiler(registry, gateway.Services));
        var translation = translator.Translate(config);
        if (!translation.IsSuccess)
        {
            throw new InvalidOperationException("Invalid test configuration: " + string.Join("; ", translation.Errors));
        }

        await gateway.StartAsync();
        await gateway.Services.GetRequiredService<SnapshotApplier>().ApplyAsync(translation.Snapshot!);

        holder = new TestGateway(backend, gateway, useKestrel ? new HttpClient { BaseAddress = new Uri(gateway.Urls.First()) } : gateway.GetTestClient());
        return holder;
    }

    public static ApiDefinition Api(string backendUrl, PolicySet? policies = null, string name = "api", params OperationDefinition[] operations) => new()
    {
        Name = name,
        BasePath = "/" + name,
        Backend = new BackendDefinition { Destinations = [backendUrl] },
        Policies = policies ?? PolicySet.Empty,
        Operations = operations,
    };

    public static PolicyReference Policy(string type, string? json = null) => new()
    {
        Type = type,
        Config = json is null ? null : JsonDocument.Parse(json).RootElement,
    };

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _gateway.DisposeAsync();
        await _backend.DisposeAsync();
    }
}

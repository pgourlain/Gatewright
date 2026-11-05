// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace Gatewright.Testing;

/// <summary>
/// A backend running on a local port that records every request and answers with a configurable handler. By default it answers
/// <c>200</c> with a JSON description of the request it received.
/// </summary>
public sealed class SimulatedBackend : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentQueue<RecordedRequest> _requests = new();
    private Func<HttpContext, Task> _handler = EchoAsync;

    private SimulatedBackend(WebApplication app) => _app = app;

    /// <summary>Gets the base address of the backend, for use as a destination in the gateway configuration.</summary>
    public Uri Address => new(_app.Urls.First());

    /// <summary>Gets the requests received so far, oldest first.</summary>
    public IReadOnlyList<RecordedRequest> Requests => [.. _requests];

    /// <summary>Starts a backend on a free local port.</summary>
    /// <returns>The running backend.</returns>
    public static async Task<SimulatedBackend> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        var backend = new SimulatedBackend(app);
        app.Map("/{**path}", async (HttpContext context) =>
        {
            context.Request.EnableBuffering();
            using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
            var body = await reader.ReadToEndAsync(context.RequestAborted).ConfigureAwait(false);
            context.Request.Body.Position = 0;
            backend._requests.Enqueue(new RecordedRequest(
                context.Request.Method,
                context.Request.Path.Value ?? "/",
                context.Request.QueryString.Value ?? string.Empty,
                context.Request.Headers.ToDictionary(h => h.Key.ToLowerInvariant(), h => h.Value.ToString()),
                body));
            await backend._handler(context).ConfigureAwait(false);
        });
        await app.StartAsync().ConfigureAwait(false);
        return backend;
    }

    /// <summary>Replaces how the backend answers.</summary>
    /// <param name="handler">The handler.</param>
    /// <returns>This backend.</returns>
    public SimulatedBackend RespondWith(Func<HttpContext, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handler = handler;
        return this;
    }

    /// <summary>Makes the backend answer every request with a fixed status and body.</summary>
    /// <param name="statusCode">The status code.</param>
    /// <param name="body">The body.</param>
    /// <param name="contentType">The content type.</param>
    /// <returns>This backend.</returns>
    public SimulatedBackend RespondWith(int statusCode, string body = "", string contentType = "text/plain") =>
        RespondWith(async context =>
        {
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = contentType;
            await context.Response.WriteAsync(body).ConfigureAwait(false);
        });

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => _app.DisposeAsync();

    private static async Task EchoAsync(HttpContext context)
    {
        context.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(
            context.Response.Body,
            new
            {
                method = context.Request.Method,
                path = context.Request.Path.Value,
                query = context.Request.QueryString.Value,
                headers = context.Request.Headers.ToDictionary(h => h.Key.ToLowerInvariant(), h => h.Value.ToString()),
            }).ConfigureAwait(false);
    }
}

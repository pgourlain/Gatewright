// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Text.Json;
using Gatewright.Abstractions;
using Gatewright.Core.Policies;
using Gatewright.Core.Snapshots;
using Gatewright.DataPlane.Telemetry;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Gatewright.DataPlane;

/// <summary>
/// YARP pipeline middleware running the policy phases around the proxied call:
/// <c>Inbound</c> and <c>Backend</c> before forwarding, <c>Outbound</c> when the response is about to be sent.
/// </summary>
/// <remarks>
/// Outbound policies run in <see cref="HttpResponse.OnStarting(Func{Task})"/>: they can change headers and the status code,
/// but cannot replace a body that is already streaming.
/// </remarks>
public sealed partial class PolicyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly PipelineRegistry _registry;
    private readonly ILogger<PolicyMiddleware> _logger;
    private readonly string? _traceToken;
    private readonly bool _subscriptionDimension;
    private readonly UsageAggregator? _usage;
    private const string RejectionKey = "gw.rejection";

    /// <summary>Initializes a new instance of the <see cref="PolicyMiddleware"/> class.</summary>
    /// <param name="next">The next middleware.</param>
    /// <param name="registry">The pipeline registry.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="options">The gateway options (the diagnostics token authorizes request tracing).</param>
    /// <param name="telemetry">The telemetry options.</param>
    /// <param name="usage">The usage aggregator, when usage analytics is registered.</param>
    public PolicyMiddleware(RequestDelegate next, PipelineRegistry registry, ILogger<PolicyMiddleware> logger, Microsoft.Extensions.Options.IOptions<Gatewright.Core.Hosting.GatewayOptions> options,
        Microsoft.Extensions.Options.IOptions<TelemetryOptions> telemetry,
        UsageAggregator? usage = null)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(logger);
        _next = next;
        _registry = registry;
        _logger = logger;
        _traceToken = options.Value.DiagnosticsToken;
        _subscriptionDimension = telemetry.Value.SubscriptionDimension;
        _usage = usage;
    }

    /// <summary>Runs the middleware.</summary>
    /// <param name="httpContext">The HTTP context.</param>
    /// <returns>A task completing when the request is processed.</returns>
    public async Task InvokeAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            await ProcessAsync(httpContext).ConfigureAwait(false);
        }
        finally
        {
            Observe(httpContext, System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }

    private void Observe(HttpContext httpContext, double elapsedMs)
    {
        var gateway = httpContext.Features.Get<GatewayContext>();
        if (gateway is null)
        {
            return;
        }

        var status = httpContext.Response.StatusCode;
        var rejection = httpContext.Items.TryGetValue(RejectionKey, out var code) ? code as string : null;
        var product = gateway.Subscription?.ProductId ?? string.Empty;
        var subscription = gateway.Subscription?.Id ?? string.Empty;
        GatewayMetrics.Record(
            gateway.Api.Name,
            gateway.Operation?.Name ?? string.Empty,
            product,
            _subscriptionDimension ? subscription : null,
            status,
            rejection,
            elapsedMs);
        _usage?.Record(gateway.Api.Name, gateway.Operation?.Name ?? string.Empty, product, subscription, status, elapsedMs);
    }

    private async Task ProcessAsync(HttpContext httpContext)
    {

        var routeId = httpContext.GetReverseProxyFeature().Route.Config.RouteId;
        if (!_registry.TryGetPipeline(routeId, out var routePipeline))
        {
            LogMissingPipeline(_logger, routeId);
            await WriteRejectionAsync(httpContext, PolicyResult.Reject(503, "no_pipeline", "No policy pipeline for this route.")).ConfigureAwait(false);
            return;
        }

        var context = new GatewayContext(httpContext, routeId, routePipeline.Api, routePipeline.Operation, httpContext.RequestAborted);
        httpContext.Features.Set(context);
        var trace = TraceRequested(httpContext) ? new List<PolicyTraceEntry>() : null;
        if (trace is not null)
        {
            context.Variables[PolicyTraceEntry.VariableKey] = trace;
        }

        var result = await routePipeline.Pipeline.ExecuteAsync(context, PolicyPhase.Inbound).ConfigureAwait(false);
        if (result.Kind != PolicyResultKind.Continue)
        {
            AddTraceHeader(httpContext, trace);
            await WriteResultAsync(httpContext, result).ConfigureAwait(false);
            return;
        }

        httpContext.Response.OnStarting(
            static async state =>
            {
                var (self, ctx, pipeline, entries) = ((PolicyMiddleware, GatewayContext, RoutePipeline, List<PolicyTraceEntry>?))state;
                var outbound = await pipeline.Pipeline.ExecuteAsync(ctx, PolicyPhase.Outbound).ConfigureAwait(false);
                self.ApplyOutbound(ctx.HttpContext, outbound);
                AddTraceHeader(ctx.HttpContext, entries);
            },
            (this, context, routePipeline, trace));

        result = await routePipeline.Pipeline.ExecuteBackendAsync(context, ForwardAsync).ConfigureAwait(false);
        if (result.Kind != PolicyResultKind.Continue && !httpContext.Response.HasStarted)
        {
            AddTraceHeader(httpContext, trace);
            await WriteResultAsync(httpContext, result).ConfigureAwait(false);
        }
    }

    private async ValueTask<PolicyResult> ForwardAsync(GatewayContext context)
    {
        var httpContext = context.HttpContext;
        var capture = httpContext.Features.Get<ResponseCaptureFeature>();
        if (capture is null)
        {
            await _next(httpContext).ConfigureAwait(false);
            return PolicyResult.Continue;
        }

        var original = httpContext.Response.Body;
        var tee = new CaptureStream(original, capture.MaxBodyBytes);
        httpContext.Response.Body = tee;
        try
        {
            await _next(httpContext).ConfigureAwait(false);
        }
        finally
        {
            httpContext.Response.Body = original;
        }

        if (!tee.Overflowed && !httpContext.RequestAborted.IsCancellationRequested && tee.GetCopy() is { } body)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, values) in httpContext.Response.Headers)
            {
                headers[name] = string.Join(", ", values.ToArray());
            }

            await capture.Captured(new ResponseCapture(httpContext.Response.StatusCode, headers, body)).ConfigureAwait(false);
        }

        return PolicyResult.Continue;
    }

    private static readonly System.Text.Json.JsonSerializerOptions TraceJson = new(System.Text.Json.JsonSerializerDefaults.Web);

    private bool TraceRequested(HttpContext httpContext)
    {
        if (string.IsNullOrEmpty(_traceToken) || !httpContext.Request.Headers.TryGetValue("x-gatewright-trace", out var presented))
        {
            return false;
        }

        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(presented.ToString())),
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(_traceToken)));
    }

    private static void AddTraceHeader(HttpContext httpContext, List<PolicyTraceEntry>? entries)
    {
        if (entries is null || httpContext.Response.HasStarted)
        {
            return;
        }

        lock (entries)
        {
            httpContext.Response.Headers["x-gatewright-trace-result"] = Convert.ToBase64String(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(entries, TraceJson));
        }
    }

    private void ApplyOutbound(HttpContext httpContext, PolicyResult result)
    {
        switch (result.Kind)
        {
            case PolicyResultKind.Reject:
                httpContext.Response.StatusCode = result.StatusCode;
                break;
            case PolicyResultKind.Respond:
                httpContext.Response.StatusCode = result.StatusCode;
                foreach (var (name, value) in result.Response!.Headers)
                {
                    httpContext.Response.Headers[name] = value;
                }

                LogOutboundBodyIgnored(_logger);
                break;
        }
    }

    private static Task WriteResultAsync(HttpContext httpContext, PolicyResult result) =>
        result.Kind == PolicyResultKind.Respond ? WriteResponseAsync(httpContext, result.Response!) : WriteRejectionAsync(httpContext, result);

    private static async Task WriteResponseAsync(HttpContext httpContext, PolicyResponse response)
    {
        var http = httpContext.Response;
        http.StatusCode = response.StatusCode;
        foreach (var (name, value) in response.Headers)
        {
            http.Headers[name] = value;
        }

        if (response.ContentType is not null)
        {
            http.ContentType = response.ContentType;
        }

        if (!response.Body.IsEmpty)
        {
            http.ContentLength = response.Body.Length;
            await http.Body.WriteAsync(response.Body, httpContext.RequestAborted).ConfigureAwait(false);
        }
    }

    private static async Task WriteRejectionAsync(HttpContext httpContext, PolicyResult result)
    {
        httpContext.Items[RejectionKey] = result.Code;
        var http = httpContext.Response;
        http.StatusCode = result.StatusCode;
        http.ContentType = "application/problem+json";
        await using var writer = new Utf8JsonWriter(http.BodyWriter);
        writer.WriteStartObject();
        writer.WriteString("type", result.Code);
        writer.WriteString("title", result.Message);
        writer.WriteNumber("status", result.StatusCode);
        writer.WriteEndObject();
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "No policy pipeline found for route {RouteId}.")]
    private static partial void LogMissingPipeline(ILogger logger, string routeId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "An outbound policy returned a response body, which cannot replace a response that is already being sent; only status and headers were applied.")]
    private static partial void LogOutboundBodyIgnored(ILogger logger);
}

// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Threading.Channels;
using Gatewright.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gatewright.DataPlane.Telemetry;

/// <summary>One access log entry. Contains no credentials: sensitive query values are masked and no header is recorded.</summary>
/// <param name="Method">The HTTP method.</param>
/// <param name="Path">The path.</param>
/// <param name="Query">The query string with sensitive values masked.</param>
/// <param name="Status">The response status.</param>
/// <param name="DurationMs">The duration in milliseconds.</param>
/// <param name="Api">The API name, if the request was routed.</param>
/// <param name="Operation">The operation name, if any.</param>
/// <param name="Subscription">The subscription id, if authenticated by key.</param>
/// <param name="ClientIp">The client address.</param>
/// <param name="TraceId">The trace id, to correlate with traces.</param>
public sealed record AccessLogEntry(string Method, string Path, string Query, int Status, double DurationMs, string? Api, string? Operation, string? Subscription, string? ClientIp, string? TraceId);

/// <summary>Writes access logs asynchronously, with sampling and masking. The request path only enqueues; a background service writes.</summary>
public sealed partial class AccessLogService : BackgroundService
{
    private static readonly HashSet<string> SensitiveNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "key", "api_key", "apikey", "api-key", "token", "access_token", "id_token", "refresh_token", "code", "password", "pwd", "secret", "client_secret", "sig", "signature", "x-api-key",
    };

    private readonly Channel<AccessLogEntry> _channel;
    private readonly AccessLogOptions _options;
    private readonly HashSet<string> _masked;
    private readonly ILogger _logger;
    private long _dropped;

    /// <summary>Initializes a new instance of the <see cref="AccessLogService"/> class.</summary>
    /// <param name="options">The telemetry options.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    public AccessLogService(IOptions<TelemetryOptions> options, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _options = options.Value.AccessLog;
        _masked = new HashSet<string>(_options.MaskedQueryParameters, StringComparer.OrdinalIgnoreCase);
        _logger = loggerFactory.CreateLogger("Gatewright.AccessLog");
        _channel = Channel.CreateBounded<AccessLogEntry>(new BoundedChannelOptions(Math.Max(1, _options.QueueCapacity)) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });
    }

    /// <summary>Gets the number of entries dropped because the queue was full.</summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>Gets a value indicating whether access logging is on.</summary>
    public bool Enabled => _options.Enabled;

    /// <summary>Queues an entry for a finished request, applying sampling.</summary>
    /// <param name="context">The HTTP context of the finished request.</param>
    /// <param name="elapsedMs">The duration in milliseconds.</param>
    public void Enqueue(HttpContext context, double elapsedMs)
    {
        ArgumentNullException.ThrowIfNull(context);
        var status = context.Response.StatusCode;
        if (status < 400 && _options.SampleRate < 1.0 && Random.Shared.NextDouble() >= _options.SampleRate)
        {
            return;
        }

        var gateway = context.Features.Get<GatewayContext>();
        var entry = new AccessLogEntry(
            context.Request.Method,
            context.Request.Path.Value ?? "/",
            MaskQuery(context.Request.QueryString.Value),
            status,
            Math.Round(elapsedMs, 2),
            gateway?.Api.Name,
            gateway?.Operation?.Name,
            gateway?.Subscription?.Id,
            context.Connection.RemoteIpAddress?.ToString(),
            Activity.Current?.TraceId.ToString());
        if (!_channel.Writer.TryWrite(entry))
        {
            Interlocked.Increment(ref _dropped);
        }
    }

    /// <summary>Replaces the values of sensitive query parameters with <c>***</c>.</summary>
    /// <param name="query">The raw query string (with or without the leading <c>?</c>).</param>
    /// <returns>The masked query string.</returns>
    public string MaskQuery(string? query)
    {
        if (string.IsNullOrEmpty(query))
        {
            return string.Empty;
        }

        var parts = query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            var equals = parts[i].IndexOf('=', StringComparison.Ordinal);
            if (equals < 0)
            {
                continue;
            }

            var name = Uri.UnescapeDataString(parts[i][..equals]);
            if (SensitiveNames.Contains(name) || _masked.Contains(name))
            {
                parts[i] = parts[i][..(equals + 1)] + "***";
            }
        }

        return "?" + string.Join('&', parts);
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var entry in _channel.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                Write(_logger, entry.Method, entry.Path, entry.Query, entry.Status, entry.DurationMs, entry.Api, entry.Operation, entry.Subscription, entry.ClientIp, entry.TraceId);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "{Method} {Path}{Query} -> {Status} in {DurationMs} ms (api={Api}, operation={Operation}, subscription={Subscription}, client={ClientIp}, trace={TraceId})")]
    private static partial void Write(ILogger logger, string method, string path, string query, int status, double durationMs, string? api, string? operation, string? subscription, string? clientIp, string? traceId);
}

/// <summary>Measures every request and hands it to the access log, so that logs and metrics share the same timing.</summary>
public sealed class AccessLogMiddleware
{
    private readonly RequestDelegate _next;
    private readonly AccessLogService _log;

    /// <summary>Initializes a new instance of the <see cref="AccessLogMiddleware"/> class.</summary>
    /// <param name="next">The next middleware.</param>
    /// <param name="log">The access log service.</param>
    public AccessLogMiddleware(RequestDelegate next, AccessLogService log)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(log);
        _next = next;
        _log = log;
    }

    /// <summary>Runs the middleware.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>A task completing when the request is handled.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!_log.Enabled)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var start = Stopwatch.GetTimestamp();
        try
        {
            await _next(context).ConfigureAwait(false);
        }
        finally
        {
            _log.Enqueue(context, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }
}

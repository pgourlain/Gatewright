// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gatewright.DataPlane.Telemetry;

/// <summary>Aggregates usage locally (no I/O per request) and sends it to the sinks in batches every flush interval.</summary>
public sealed partial class UsageAggregator : BackgroundService
{
    private readonly ConcurrentDictionary<(string Api, string Operation, string Product, string Subscription), Counter> _counters = new();
    private readonly IReadOnlyList<IUsageSink> _sinks;
    private readonly UsageOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<UsageAggregator> _logger;
    private DateTimeOffset _windowStart;

    /// <summary>Initializes a new instance of the <see cref="UsageAggregator"/> class.</summary>
    /// <param name="sinks">Where batches are sent.</param>
    /// <param name="options">The telemetry options.</param>
    /// <param name="time">The clock.</param>
    /// <param name="logger">The logger.</param>
    public UsageAggregator(IEnumerable<IUsageSink> sinks, IOptions<TelemetryOptions> options, TimeProvider time, ILogger<UsageAggregator> logger)
    {
        ArgumentNullException.ThrowIfNull(sinks);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        _sinks = [.. sinks];
        _options = options.Value.Usage;
        _time = time;
        _logger = logger;
        _windowStart = time.GetUtcNow();
    }

    /// <summary>Gets a value indicating whether usage is collected.</summary>
    public bool Enabled => _options.Enabled && _sinks.Count > 0;

    /// <summary>Counts one request.</summary>
    /// <param name="api">The API name.</param>
    /// <param name="operation">The operation name.</param>
    /// <param name="product">The product.</param>
    /// <param name="subscription">The subscription id, or empty.</param>
    /// <param name="statusCode">The response status.</param>
    /// <param name="elapsedMs">The duration in milliseconds.</param>
    public void Record(string api, string operation, string product, string subscription, int statusCode, double elapsedMs)
    {
        if (!Enabled)
        {
            return;
        }

        var counter = _counters.GetOrAdd((api, operation, product, subscription), _ => new Counter());
        lock (counter)
        {
            counter.Requests++;
            counter.Errors += statusCode >= 400 ? 1 : 0;
            counter.TotalDurationMs += elapsedMs;
        }
    }

    /// <summary>Sends everything aggregated so far and starts a new window.</summary>
    /// <param name="cancellationToken">Cancels the flush.</param>
    /// <returns>A task completing when the batch was handed to every sink.</returns>
    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        var end = _time.GetUtcNow();
        var start = _windowStart;
        _windowStart = end;
        var records = new List<UsageRecord>();
        foreach (var key in _counters.Keys.ToList())
        {
            if (!_counters.TryRemove(key, out var counter))
            {
                continue;
            }

            lock (counter)
            {
                records.Add(new UsageRecord(start, end, key.Api, key.Operation, key.Product, key.Subscription, counter.Requests, counter.Errors, counter.TotalDurationMs));
            }
        }

        if (records.Count == 0)
        {
            return;
        }

        foreach (var sink in _sinks)
        {
            try
            {
                await sink.WriteAsync(records, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogSinkFailed(_logger, ex, sink.Name, records.Count);
            }
        }
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, _options.FlushSeconds)), _time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await FlushAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down: send what is left.
        }

        await FlushAsync(CancellationToken.None).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Usage sink {Sink} failed; {Count} records were dropped.")]
    private static partial void LogSinkFailed(ILogger logger, Exception exception, string sink, int count);

    private sealed class Counter
    {
        public long Requests;
        public long Errors;
        public double TotalDurationMs;
    }
}

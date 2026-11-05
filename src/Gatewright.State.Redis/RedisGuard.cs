// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Gatewright.State.Redis;

/// <summary>Runs Redis calls with a short timeout, a circuit breaker and metrics. Failures surface as <see cref="RedisUnavailableException"/>.</summary>
public sealed partial class RedisGuard : IAsyncDisposable
{
    private readonly RedisOptions _options;
    private readonly CircuitBreaker _breaker;
    private readonly ILogger<RedisGuard> _logger;
    private readonly Lazy<Task<IConnectionMultiplexer>> _connection;

    /// <summary>Initializes a new instance of the <see cref="RedisGuard"/> class.</summary>
    /// <param name="options">The Redis options.</param>
    /// <param name="time">The clock used by the circuit breaker.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="connectionFactory">Creates the connection; defaults to a lazy StackExchange.Redis multiplexer that does not fail at startup.</param>
    public RedisGuard(
        IOptions<RedisOptions> options,
        TimeProvider time,
        ILogger<RedisGuard> logger,
        Func<ConfigurationOptions, Task<IConnectionMultiplexer>>? connectionFactory = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options.Value;
        _logger = logger;
        _breaker = new CircuitBreaker(_options.FailureThreshold, TimeSpan.FromSeconds(_options.BreakSeconds), time);
        var factory = connectionFactory ?? (async configuration => await ConnectionMultiplexer.ConnectAsync(configuration).ConfigureAwait(false));
        _connection = new Lazy<Task<IConnectionMultiplexer>>(() => factory(BuildConfiguration(_options)));
    }

    /// <summary>Gets the key prefix.</summary>
    public string KeyPrefix => _options.KeyPrefix;

    /// <summary>Gets the call timeout.</summary>
    public TimeSpan Timeout => TimeSpan.FromMilliseconds(_options.CommandTimeoutMilliseconds);

    /// <summary>Gets a value indicating whether the circuit around Redis is open.</summary>
    public bool IsCircuitOpen => _breaker.IsOpen;

    /// <summary>Runs a call against the database.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="call">The call.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The call result.</returns>
    /// <exception cref="RedisUnavailableException">The circuit is open, or the call failed or timed out.</exception>
    public async ValueTask<T> ExecuteAsync<T>(Func<IConnectionMultiplexer, Task<T>> call, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(call);
        if (!_breaker.TryAcquire(out _))
        {
            RedisTelemetry.Rejected.Add(1);
            throw new RedisUnavailableException("The Redis circuit is open.");
        }

        RedisTelemetry.Calls.Add(1);
        try
        {
            var connection = await _connection.Value.WaitAsync(Timeout, cancellationToken).ConfigureAwait(false);
            var result = await call(connection).WaitAsync(Timeout, cancellationToken).ConfigureAwait(false);
            _breaker.RecordSuccess();
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException or OperationCanceledException or InvalidOperationException)
        {
            _breaker.RecordFailure();
            RedisTelemetry.Errors.Add(1);
            LogFailure(_logger, ex.GetType().Name, ex.Message);
            throw new RedisUnavailableException("A Redis call failed.", ex);
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_connection.IsValueCreated && _connection.Value.IsCompletedSuccessfully)
        {
            await _connection.Value.Result.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static ConfigurationOptions BuildConfiguration(RedisOptions options)
    {
        var configuration = ConfigurationOptions.Parse(options.ConnectionString ?? throw new InvalidOperationException("Redis:ConnectionString is not set."));
        configuration.AbortOnConnectFail = false;
        configuration.ConnectTimeout = Math.Max(options.CommandTimeoutMilliseconds, 1000);
        configuration.AsyncTimeout = options.CommandTimeoutMilliseconds;
        configuration.SyncTimeout = options.CommandTimeoutMilliseconds;
        return configuration;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Redis call failed ({ExceptionType}): {Reason}")]
    private static partial void LogFailure(ILogger logger, string exceptionType, string reason);
}

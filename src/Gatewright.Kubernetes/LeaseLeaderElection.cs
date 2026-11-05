// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Discovery;
using k8s;
using k8s.LeaderElection;
using k8s.LeaderElection.ResourceLock;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gatewright.Kubernetes;

/// <summary>Leader election on a Kubernetes <c>Lease</c>: exactly one control plane replica holds it, and takes over within a lease duration if it dies.</summary>
public sealed partial class LeaseLeaderElection : BackgroundService, ILeaderElection
{
    private readonly IKubernetes _client;
    private readonly KubernetesOptions _options;
    private readonly ILogger<LeaseLeaderElection> _logger;
    private readonly string _identity;
    private readonly object _gate = new();
    private TaskCompletionSource<CancellationToken> _leadership = NewSignal();
    private CancellationTokenSource? _term;

    /// <summary>Initializes a new instance of the <see cref="LeaseLeaderElection"/> class.</summary>
    /// <param name="client">The Kubernetes client.</param>
    /// <param name="options">The Kubernetes options.</param>
    /// <param name="logger">The logger.</param>
    public LeaseLeaderElection(IKubernetes client, IOptions<KubernetesOptions> options, ILogger<LeaseLeaderElection> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _client = client;
        _options = options.Value;
        _logger = logger;
        _identity = _options.Identity ?? $"{Environment.MachineName}-{Guid.NewGuid().ToString("N")[..8]}";
    }

    /// <inheritdoc/>
    public bool IsLeader
    {
        get
        {
            lock (_gate)
            {
                return _term is { IsCancellationRequested: false };
            }
        }
    }

    /// <summary>Gets the identity this replica presents in the election.</summary>
    public string Identity => _identity;

    /// <inheritdoc/>
    public async Task<CancellationToken> WaitForLeadershipAsync(CancellationToken cancellationToken)
    {
        Task<CancellationToken> waiting;
        lock (_gate)
        {
            waiting = _leadership.Task;
        }

        return await waiting.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var leaseNamespace = _options.LeaseNamespace ?? DetectNamespace();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var lease = new LeaseLock(_client, leaseNamespace, _options.LeaseName, _identity);
                var elector = new LeaderElector(new LeaderElectionConfig(lease)
                {
                    LeaseDuration = TimeSpan.FromSeconds(_options.LeaseDurationSeconds),
                    RenewDeadline = TimeSpan.FromSeconds(_options.RenewDeadlineSeconds),
                    RetryPeriod = TimeSpan.FromSeconds(_options.RetryPeriodSeconds),
                });
                elector.OnStartedLeading += StartedLeading;
                elector.OnStoppedLeading += StoppedLeading;
                await elector.RunUntilLeadershipLostAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogElectionFailed(_logger, ex);
            }

            StoppedLeading();
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.RetryPeriodSeconds), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        StoppedLeading();
    }

    private static TaskCompletionSource<CancellationToken> NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static string DetectNamespace()
    {
        const string path = "/var/run/secrets/kubernetes.io/serviceaccount/namespace";
        return File.Exists(path) ? File.ReadAllText(path).Trim() : "default";
    }

    private void StartedLeading()
    {
        lock (_gate)
        {
            _term?.Dispose();
            _term = new CancellationTokenSource();
            _leadership.TrySetResult(_term.Token);
        }

        LogLeading(_logger, _identity);
    }

    private void StoppedLeading()
    {
        lock (_gate)
        {
            if (_term is null)
            {
                return;
            }

            _term.Cancel();
            _term = null;
            _leadership = NewSignal();
        }

        LogNotLeading(_logger, _identity);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Identity} is now the control plane leader.")]
    private static partial void LogLeading(ILogger logger, string identity);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Identity} is no longer the control plane leader.")]
    private static partial void LogNotLeading(ILogger logger, string identity);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Leader election failed; retrying.")]
    private static partial void LogElectionFailed(ILogger logger, Exception exception);
}

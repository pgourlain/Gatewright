// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging;

namespace Gatewright.Kubernetes;

/// <summary>
/// Keeps a local view of a Kubernetes resource list up to date: an initial list, then a watch from the list's resource version,
/// relisting when the watch expires and reconnecting with backoff on errors.
/// </summary>
/// <typeparam name="T">The resource type.</typeparam>
/// <typeparam name="TList">The list type.</typeparam>
public sealed partial class ResourceWatcher<T, TList>
    where T : class, IKubernetesObject<V1ObjectMeta>
    where TList : class, IKubernetesObject<V1ListMeta>, IItems<T>
{
    private readonly string _name;
    private readonly Func<CancellationToken, Task<TList>> _list;
    private readonly Func<string?, CancellationToken, IAsyncEnumerable<(WatchEventType Type, T Item)>> _watch;
    private readonly Action _clear;
    private readonly Action<T> _upsert;
    private readonly Action<T> _remove;
    private readonly Action _changed;
    private readonly ILogger _logger;

    /// <summary>Initializes a new instance of the <see cref="ResourceWatcher{T, TList}"/> class.</summary>
    /// <param name="name">A name for logs.</param>
    /// <param name="list">Lists all resources.</param>
    /// <param name="watch">Starts a watch from a resource version.</param>
    /// <param name="clear">Forgets everything seen (before a relist).</param>
    /// <param name="upsert">Records an added or modified resource.</param>
    /// <param name="remove">Records a deleted resource.</param>
    /// <param name="changed">Called after each batch of changes.</param>
    /// <param name="logger">The logger.</param>
    public ResourceWatcher(
        string name,
        Func<CancellationToken, Task<TList>> list,
        Func<string?, CancellationToken, IAsyncEnumerable<(WatchEventType Type, T Item)>> watch,
        Action clear,
        Action<T> upsert,
        Action<T> remove,
        Action changed,
        ILogger logger)
    {
        _name = name;
        _list = list;
        _watch = watch;
        _clear = clear;
        _upsert = upsert;
        _remove = remove;
        _changed = changed;
        _logger = logger;
    }

    /// <summary>Runs until cancelled.</summary>
    /// <param name="cancellationToken">Stops the watcher.</param>
    /// <returns>A task completing when cancelled.</returns>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var failures = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var list = await _list(cancellationToken).ConfigureAwait(false);
                _clear();
                foreach (var item in list.Items)
                {
                    _upsert(item);
                }

                _changed();
                failures = 0;

                var resourceVersion = list.Metadata?.ResourceVersion;
                await foreach (var (type, item) in _watch(resourceVersion, cancellationToken).ConfigureAwait(false))
                {
                    switch (type)
                    {
                        case WatchEventType.Added:
                        case WatchEventType.Modified:
                            _upsert(item);
                            _changed();
                            break;
                        case WatchEventType.Deleted:
                            _remove(item);
                            _changed();
                            break;
                        case WatchEventType.Error:
                            throw new InvalidOperationException("The watch reported an error (resource version too old?); relisting.");
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                failures++;
                LogRestart(_logger, ex, _name);
                var delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, Math.Min(failures, 5)) / 2));
                try
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Watching {Name} failed; relisting after a pause.")]
    private static partial void LogRestart(ILogger logger, Exception exception, string name);
}

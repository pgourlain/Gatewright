// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Text;
using Gatewright.Core.Security;
using k8s;
using k8s.Autorest;
using Microsoft.Extensions.Logging;

namespace Gatewright.Kubernetes;

/// <summary>Reads a Kubernetes TLS secret (<c>tls.crt</c>, <c>tls.key</c>) referenced by a hostname's certificate source.</summary>
public sealed partial class KubernetesSecretFetcher : ISecretFetcher
{
    private readonly IKubernetes _client;
    private readonly ILogger<KubernetesSecretFetcher> _logger;

    /// <summary>Initializes a new instance of the <see cref="KubernetesSecretFetcher"/> class.</summary>
    /// <param name="client">The Kubernetes client.</param>
    /// <param name="logger">The logger.</param>
    public KubernetesSecretFetcher(IKubernetes client, ILogger<KubernetesSecretFetcher> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(logger);
        _client = client;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async ValueTask<CertificateMaterial?> FetchAsync(string reference, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        var parts = reference.Split('/', 2);
        if (parts.Length != 2)
        {
            LogInvalidReference(_logger, reference);
            return null;
        }

        try
        {
            var secret = await _client.CoreV1.ReadNamespacedSecretAsync(parts[1], parts[0], cancellationToken: cancellationToken).ConfigureAwait(false);
            if (secret.Data is not { } data || !data.TryGetValue("tls.crt", out var cert) || !data.TryGetValue("tls.key", out var key))
            {
                LogMissingKeys(_logger, reference);
                return null;
            }

            return new CertificateMaterial(Encoding.UTF8.GetString(cert), Encoding.UTF8.GetString(key));
        }
        catch (HttpOperationException ex)
        {
            LogReadFailed(_logger, ex, reference);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Secret reference {Reference} is not namespace/name; ignored.")]
    private static partial void LogInvalidReference(ILogger logger, string reference);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Secret {Reference} has no tls.crt or tls.key data.")]
    private static partial void LogMissingKeys(ILogger logger, string reference);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read secret {Reference}.")]
    private static partial void LogReadFailed(ILogger logger, Exception exception, string reference);
}

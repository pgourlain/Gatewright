// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Gatewright.Core.Configuration;
using Gatewright.Core.Security;
using Gatewright.Core.Snapshots;
using Microsoft.Extensions.Logging;

namespace Gatewright.DataPlane.Tls;

/// <summary>
/// The certificates the data plane presents, selected by SNI. Fed by each snapshot (file sources are loaded and watched for changes) and
/// by pushed secrets. A certificate that fails to load never replaces a working one.
/// </summary>
public sealed partial class CertificateStore : ISecurityDataSink
{
    private readonly ILogger<CertificateStore> _logger;
    private readonly ConcurrentDictionary<string, SecretMaterial> _secrets = new(StringComparer.Ordinal);
    private Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<HostnameDefinition> _hostnames = [];

    /// <summary>Initializes a new instance of the <see cref="CertificateStore"/> class.</summary>
    /// <param name="logger">The logger.</param>
    public CertificateStore(ILogger<CertificateStore> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>Gets the hostnames of the applied snapshot.</summary>
    public IReadOnlyList<HostnameDefinition> Hostnames => Volatile.Read(ref _hostnames);

    /// <inheritdoc/>
    public void Apply(SecurityData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var previous = Volatile.Read(ref _entries);
        var next = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        foreach (var hostname in data.Hostnames)
        {
            if (hostname.Certificate is not { } source)
            {
                continue;
            }

            var key = CertificateKey(source);
            if (previous.TryGetValue(hostname.Name, out var existing) && existing.Key == key)
            {
                next[hostname.Name] = existing;
                continue;
            }

            var entry = new Entry(key, source);
            entry.Certificate = TryLoad(hostname.Name, entry);
            next[hostname.Name] = entry;
        }

        Volatile.Write(ref _entries, next);
        Volatile.Write(ref _hostnames, data.Hostnames);
    }

    /// <summary>Provides the key material of a Kubernetes secret pushed by the control plane.</summary>
    /// <param name="reference">The secret reference (<c>namespace/name</c>).</param>
    /// <param name="certificatePem">The certificate chain in PEM.</param>
    /// <param name="keyPem">The private key in PEM.</param>
    public void SetSecret(string reference, string certificatePem, string keyPem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        _secrets[reference] = new SecretMaterial(certificatePem, keyPem);
        foreach (var (name, entry) in Volatile.Read(ref _entries))
        {
            if (entry.Source.Secret == reference)
            {
                entry.Certificate = TryLoad(name, entry) ?? entry.Certificate;
            }
        }
    }

    /// <summary>Reloads certificates whose files changed. Called periodically.</summary>
    public void ReloadChanged()
    {
        foreach (var (name, entry) in Volatile.Read(ref _entries))
        {
            if (entry.Source.File is null)
            {
                continue;
            }

            var fingerprint = Fingerprint(entry.Source.File);
            if (fingerprint is null || fingerprint == entry.Fingerprint)
            {
                continue;
            }

            var loaded = TryLoad(name, entry);
            if (loaded is not null)
            {
                entry.Certificate = loaded;
                LogReloaded(_logger, name, loaded.Thumbprint);
            }
        }
    }

    /// <summary>Selects the certificate for a TLS server name: exact match, then <c>*.parent</c>, then the <c>*</c> default.</summary>
    /// <param name="serverName">The SNI host name sent by the client, if any.</param>
    /// <returns>The certificate, or <see langword="null"/> when none matches.</returns>
    public X509Certificate2? Select(string? serverName)
    {
        var entries = Volatile.Read(ref _entries);
        if (serverName is { Length: > 0 })
        {
            if (entries.TryGetValue(serverName, out var exact) && exact.Certificate is { } exactCertificate)
            {
                return exactCertificate;
            }

            var dot = serverName.IndexOf('.', StringComparison.Ordinal);
            if (dot > 0 && entries.TryGetValue("*" + serverName[dot..], out var wildcard) && wildcard.Certificate is { } wildcardCertificate)
            {
                return wildcardCertificate;
            }
        }

        return entries.TryGetValue("*", out var fallback) ? fallback.Certificate : null;
    }

    /// <summary>Finds the hostname definition for a request host: exact, wildcard, then <c>*</c>.</summary>
    /// <param name="host">The request host (without port).</param>
    /// <returns>The definition, or <see langword="null"/>.</returns>
    public HostnameDefinition? FindHostname(string host)
    {
        var hostnames = Hostnames;
        if (hostnames.Count == 0)
        {
            return null;
        }

        HostnameDefinition? wildcard = null;
        HostnameDefinition? fallback = null;
        var dot = host.IndexOf('.', StringComparison.Ordinal);
        var parent = dot > 0 ? "*" + host[dot..] : null;
        foreach (var hostname in hostnames)
        {
            if (string.Equals(hostname.Name, host, StringComparison.OrdinalIgnoreCase))
            {
                return hostname;
            }

            if (parent is not null && string.Equals(hostname.Name, parent, StringComparison.OrdinalIgnoreCase))
            {
                wildcard = hostname;
            }
            else if (hostname.Name == "*")
            {
                fallback = hostname;
            }
        }

        return wildcard ?? fallback;
    }

    private X509Certificate2? TryLoad(string hostname, Entry entry)
    {
        try
        {
            if (entry.Source.File is { } files)
            {
                entry.Fingerprint = Fingerprint(files);
                return X509Certificate2.CreateFromPemFile(files.CertPath, files.KeyPath);
            }

            if (entry.Source.Secret is { } reference && _secrets.TryGetValue(reference, out var material))
            {
                return X509Certificate2.CreateFromPem(material.CertificatePem, material.KeyPem);
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or CryptographicException or ArgumentException or UnauthorizedAccessException)
        {
            LogLoadFailed(_logger, ex, hostname);
            return null;
        }
    }

    private static string? Fingerprint(CertificateFiles files)
    {
        try
        {
            var bytes = File.ReadAllBytes(files.CertPath).Concat(File.ReadAllBytes(files.KeyPath)).ToArray();
            return Convert.ToHexString(SHA256.HashData(bytes));
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string CertificateKey(CertificateSource source) =>
        source.File is { } files ? $"file:{files.CertPath}|{files.KeyPath}" : $"secret:{source.Secret}";

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not load the certificate for {Hostname}; keeping the previous one if any.")]
    private static partial void LogLoadFailed(ILogger logger, Exception exception, string hostname);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reloaded the certificate for {Hostname} (thumbprint {Thumbprint}).")]
    private static partial void LogReloaded(ILogger logger, string hostname, string thumbprint);

    private sealed record SecretMaterial(string CertificatePem, string KeyPem);

    private sealed class Entry(string key, CertificateSource source)
    {
        private X509Certificate2? _certificate;

        public string Key { get; } = key;

        public CertificateSource Source { get; } = source;

        public string? Fingerprint { get; set; }

        public X509Certificate2? Certificate
        {
            get => Volatile.Read(ref _certificate);
            set => Volatile.Write(ref _certificate, value);
        }
    }
}

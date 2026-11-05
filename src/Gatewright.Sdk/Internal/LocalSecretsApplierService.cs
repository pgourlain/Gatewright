// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.ControlPlane.Distribution;
using Gatewright.DataPlane;

namespace Gatewright.Sdk.Internal;

/// <summary>In single-process mode, applies TLS secret material straight to the data plane (no gRPC stream is involved).</summary>
internal sealed class LocalSecretsApplierService : BackgroundService
{
    private readonly TlsSecretsHub _hub;
    private readonly SnapshotApplier _applier;

    public LocalSecretsApplierService(TlsSecretsHub hub, SnapshotApplier applier)
    {
        _hub = hub;
        _applier = applier;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var subscription = _hub.Subscribe();
        try
        {
            Apply(_hub.GetCurrent());
            await foreach (var secrets in subscription.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                Apply(secrets);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    private void Apply(Gatewright.Core.Security.CertificateMaterialSet secrets)
    {
        foreach (var (reference, material) in secrets.Secrets)
        {
            _applier.ApplySecret(reference, material.CertificatePem, material.KeyPem);
        }
    }
}

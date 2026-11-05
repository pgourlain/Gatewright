// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.ControlPlane.Data;
using Gatewright.ControlPlane.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatewright.ControlPlane.Distribution;

/// <summary>Records in the database which configuration version each data plane node applied, so any control plane replica can answer.</summary>
public sealed class NodeRegistry
{
    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _time;

    /// <summary>Initializes a new instance of the <see cref="NodeRegistry"/> class.</summary>
    /// <param name="scopes">The scope factory.</param>
    /// <param name="time">The clock.</param>
    public NodeRegistry(IServiceScopeFactory scopes, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(time);
        _scopes = scopes;
        _time = time;
    }

    /// <summary>Records that a node opened a stream.</summary>
    /// <param name="nodeId">The node identifier.</param>
    /// <param name="environment">The environment it follows.</param>
    /// <param name="version">The version it reports.</param>
    /// <param name="hash">The hash it reports.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task completing when the node is recorded.</returns>
    public Task ConnectedAsync(string nodeId, string environment, long version, string hash, CancellationToken cancellationToken) =>
        UpdateAsync(nodeId, environment, node =>
        {
            node.Connected = true;
            node.ConnectedAt = _time.GetUtcNow();
            node.AppliedVersion = version;
            node.AppliedHash = hash;
        }, cancellationToken);

    /// <summary>Records that a node's stream closed.</summary>
    /// <param name="nodeId">The node identifier.</param>
    /// <param name="environment">The environment.</param>
    /// <returns>A task completing when the node is recorded.</returns>
    public Task DisconnectedAsync(string nodeId, string environment) =>
        UpdateAsync(nodeId, environment, node => node.Connected = false, CancellationToken.None);

    /// <summary>Records that a node is still alive.</summary>
    /// <param name="nodeId">The node identifier.</param>
    /// <param name="environment">The environment.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task completing when the node is recorded.</returns>
    public Task TouchAsync(string nodeId, string environment, CancellationToken cancellationToken) =>
        UpdateAsync(nodeId, environment, _ => { }, cancellationToken);

    /// <summary>Records the outcome of applying a version.</summary>
    /// <param name="nodeId">The node identifier.</param>
    /// <param name="environment">The environment.</param>
    /// <param name="version">The version concerned.</param>
    /// <param name="hash">The hash concerned.</param>
    /// <param name="applied">Whether it was applied.</param>
    /// <param name="error">Why it was refused.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task completing when the outcome is recorded.</returns>
    public Task AckAsync(string nodeId, string environment, long version, string hash, bool applied, string? error, CancellationToken cancellationToken) =>
        UpdateAsync(nodeId, environment, node =>
        {
            if (applied)
            {
                node.AppliedVersion = version;
                node.AppliedHash = hash;
                node.LastError = null;
            }
            else
            {
                node.LastError = $"version {version}: {error}";
            }
        }, cancellationToken);

    private async Task UpdateAsync(string nodeId, string environment, Action<NodeStatusEntity> change, CancellationToken cancellationToken)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewrightDbContext>();
        var node = await db.Nodes.FirstOrDefaultAsync(n => n.NodeId == nodeId, cancellationToken).ConfigureAwait(false);
        if (node is null)
        {
            node = new NodeStatusEntity { NodeId = nodeId, Environment = environment, ConnectedAt = _time.GetUtcNow() };
            db.Nodes.Add(node);
        }

        node.Environment = environment;
        node.LastSeen = _time.GetUtcNow();
        change(node);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

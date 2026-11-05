// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Abstractions;

namespace Gatewright.Core.Configuration;

/// <summary>The policies declared in one scope, grouped by pipeline phase.</summary>
public sealed record PolicySet
{
    /// <summary>Gets an empty policy set.</summary>
    public static PolicySet Empty { get; } = new();

    /// <summary>Gets the policies run before the request is forwarded.</summary>
    public IReadOnlyList<PolicyReference> Inbound { get; init; } = [];

    /// <summary>Gets the policies run around the backend call.</summary>
    public IReadOnlyList<PolicyReference> Backend { get; init; } = [];

    /// <summary>Gets the policies run on the backend response.</summary>
    public IReadOnlyList<PolicyReference> Outbound { get; init; } = [];

    /// <summary>Gets the policies run when another policy failed.</summary>
    public IReadOnlyList<PolicyReference> OnError { get; init; } = [];

    /// <summary>Gets the policies of one phase.</summary>
    /// <param name="phase">A single phase.</param>
    /// <returns>The policies declared for that phase.</returns>
    public IReadOnlyList<PolicyReference> Get(PolicyPhase phase) => phase switch
    {
        PolicyPhase.Inbound => Inbound,
        PolicyPhase.Backend => Backend,
        PolicyPhase.Outbound => Outbound,
        PolicyPhase.OnError => OnError,
        _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, "A single phase is expected."),
    };
}

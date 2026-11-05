// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Configuration;

namespace Gatewright.ControlPlane.Domain;

/// <summary>The global and gateway-wide policy sets (a single row).</summary>
public sealed class ScopePoliciesEntity
{
    /// <summary>The fixed identifier of the single row.</summary>
    public static readonly Guid SingletonId = new("00000000-0000-0000-0000-000000000001");

    /// <summary>Gets or sets the identifier.</summary>
    public Guid Id { get; set; } = SingletonId;

    /// <summary>Gets or sets the policies applied to every API.</summary>
    public PolicySet Global { get; set; } = PolicySet.Empty;

    /// <summary>Gets or sets the policies applied to every API of this gateway.</summary>
    public PolicySet Gateway { get; set; } = PolicySet.Empty;
}

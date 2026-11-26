// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Kubernetes.GatewayApi;

/// <summary>Options of the optional Gateway API source (ADR 0017). Off by default.</summary>
public sealed class GatewayApiOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "GatewayApi";

    /// <summary>Gets or sets a value indicating whether <c>HTTPRoute</c> objects are read. It needs <c>Kubernetes:Enabled</c> and the Gateway API CRDs.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the <c>spec.gatewayClassName</c> of the Gateways whose routes are served.</summary>
    public string GatewayClassName { get; set; } = "gatewright";

    /// <summary>Gets or sets how long changes are gathered before one reconciliation, in milliseconds.</summary>
    public int DebounceMilliseconds { get; set; } = 500;

    /// <summary>Gets or sets the interval of the periodic reconciliation, in seconds. It repairs missed events and counts the absences before a deletion.</summary>
    public int ReconcileSeconds { get; set; } = 30;

    /// <summary>Gets or sets a value indicating whether the outcome is written to the route status (<c>Accepted</c>). It needs the <c>httproutes/status</c> patch right.</summary>
    public bool WriteStatus { get; set; }
}

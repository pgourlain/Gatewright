// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Distribution;

/// <summary>Settings of the control plane → data plane distribution, bound from <c>Distribution</c>.</summary>
public sealed class DistributionOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Distribution";

    /// <summary>Gets or sets the accepted service tokens (server side). Several tokens allow a rotation without downtime. Provide them through a secret store.</summary>
    public List<string> Tokens { get; set; } = [];

    /// <summary>Gets or sets the token presented to the control plane (client side).</summary>
    public string? Token { get; set; }

    /// <summary>Gets or sets the control plane gRPC address, for example <c>https://gatewright-cp:8081</c> (client side).</summary>
    public string? Endpoint { get; set; }

    /// <summary>Gets or sets the node identifier (client side). Defaults to the machine name plus a random suffix.</summary>
    public string? NodeId { get; set; }

    /// <summary>Gets or sets how often the control plane looks for new publications, in milliseconds (server side).</summary>
    public int PollMilliseconds { get; set; } = 500;

    /// <summary>Gets or sets how long changes are collected before being distributed, in milliseconds (server side).</summary>
    public int DebounceMilliseconds { get; set; } = 300;

    /// <summary>Gets or sets the interval between heartbeats on idle streams, in seconds (server side).</summary>
    public int HeartbeatSeconds { get; set; } = 10;

    /// <summary>Gets or sets the longest delay before a client reconnects, in seconds (client side).</summary>
    public int MaxReconnectSeconds { get; set; } = 30;
}

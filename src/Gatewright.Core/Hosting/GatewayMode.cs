// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Core.Hosting;

/// <summary>The role played by a Gatewright process.</summary>
public enum GatewayMode
{
    /// <summary>Control plane and data plane in one process (development, small deployments).</summary>
    All = 0,

    /// <summary>Control plane only: Admin API, storage, snapshot distribution.</summary>
    ControlPlane = 1,

    /// <summary>Data plane only: serves traffic from snapshots received from a control plane.</summary>
    DataPlane = 2,
}

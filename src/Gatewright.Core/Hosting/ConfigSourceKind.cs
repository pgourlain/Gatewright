// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Core.Hosting;

/// <summary>Where the configuration comes from in mode <see cref="GatewayMode.All"/>.</summary>
public enum ConfigSourceKind
{
    /// <summary>A JSON file reloaded when it changes.</summary>
    File = 0,

    /// <summary>The control plane database: the active publication of <see cref="GatewayOptions.Environment"/>.</summary>
    Database = 1,
}

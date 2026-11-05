// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Core.Hosting;

/// <summary>Describes a component enabled in the current process; used for diagnostics and tests.</summary>
/// <param name="Name">The component name (for example <c>data-plane</c>).</param>
public sealed record GatewayComponent(string Name);

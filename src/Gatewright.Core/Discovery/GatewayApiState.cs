// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Core.Discovery;

/// <summary>Every <c>HTTPRoute</c> that targets a Gatewright Gateway, after a complete and successful listing of the cluster.</summary>
/// <param name="Routes">The mapped routes.</param>
public sealed record GatewayApiState(IReadOnlyList<GatewayApiRoute> Routes);

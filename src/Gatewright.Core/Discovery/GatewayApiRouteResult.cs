// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Core.Discovery;

/// <summary>What became of a route after a synchronization, reported back to the route status when enabled.</summary>
/// <param name="Ref">The route reference, <c>namespace/name</c>.</param>
/// <param name="Accepted">Whether the route is applied.</param>
/// <param name="Reason">A short reason in the Gateway API vocabulary, such as <c>Accepted</c>, <c>UnsupportedValue</c> or <c>PathConflict</c>.</param>
/// <param name="Message">A human readable explanation.</param>
public sealed record GatewayApiRouteResult(string Ref, bool Accepted, string Reason, string Message);

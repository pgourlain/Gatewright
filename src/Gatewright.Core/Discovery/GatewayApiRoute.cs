// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Configuration;

namespace Gatewright.Core.Discovery;

/// <summary>An <c>HTTPRoute</c> read from the cluster and mapped to the desired API (ADR 0017).</summary>
/// <param name="Ref">The route reference, <c>namespace/name</c>.</param>
/// <param name="Uid">The uid of the route.</param>
/// <param name="Generation">The <c>metadata.generation</c> of the route, used to skip unchanged routes.</param>
/// <param name="Api">The desired API, or <see langword="null"/> when the route uses a feature that is not supported.</param>
/// <param name="Problems">Why the route cannot be mapped (empty when <paramref name="Api"/> is set).</param>
public sealed record GatewayApiRoute(string Ref, string Uid, long? Generation, ApiDefinition? Api, IReadOnlyList<string> Problems);

// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Abstractions;

/// <summary>Forwards the request to the backend (and runs the rest of the <see cref="PolicyPhase.Backend"/> chain).</summary>
/// <param name="context">The request context.</param>
/// <returns><see cref="PolicyResult.Continue"/> when the call completed, or a terminating result.</returns>
public delegate ValueTask<PolicyResult> BackendCall(GatewayContext context);

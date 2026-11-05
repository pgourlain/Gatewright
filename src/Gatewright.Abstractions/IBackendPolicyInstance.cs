// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Abstractions;

/// <summary>
/// A <see cref="PolicyPhase.Backend"/> policy that wraps the backend call (timeouts, retries, circuit breaking).
/// Wrappers run outermost first, in declaration order. They are not subject to the default policy timeout since they enclose the whole call.
/// </summary>
public interface IBackendPolicyInstance : IPolicyInstance
{
    /// <summary>Runs the policy around the backend call.</summary>
    /// <param name="context">The request context.</param>
    /// <param name="invokeBackend">Calls the inner policies and the backend. May be called several times (retries) or not at all (short-circuit).</param>
    /// <returns>The result; <see cref="PolicyResult.Continue"/> when the call completed.</returns>
    ValueTask<PolicyResult> InvokeBackendAsync(GatewayContext context, BackendCall invokeBackend);
}

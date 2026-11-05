// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Abstractions;

namespace Gatewright.Sdk;

/// <summary>Helpers to write <see cref="IPolicyInstance"/>s.</summary>
public static class PolicyInstance
{
    /// <summary>Creates an instance from a delegate. The delegate must be thread-safe and must not capture per-request state.</summary>
    /// <param name="invoke">What the policy does for a request.</param>
    /// <returns>The instance.</returns>
    public static IPolicyInstance From(Func<GatewayContext, ValueTask<PolicyResult>> invoke)
    {
        ArgumentNullException.ThrowIfNull(invoke);
        return new DelegateInstance(invoke);
    }

    /// <summary>Creates an instance from a synchronous delegate.</summary>
    /// <param name="invoke">What the policy does for a request.</param>
    /// <returns>The instance.</returns>
    public static IPolicyInstance From(Func<GatewayContext, PolicyResult> invoke)
    {
        ArgumentNullException.ThrowIfNull(invoke);
        return new DelegateInstance(context => new ValueTask<PolicyResult>(invoke(context)));
    }

    private sealed class DelegateInstance(Func<GatewayContext, ValueTask<PolicyResult>> invoke) : IPolicyInstance
    {
        public ValueTask<PolicyResult> InvokeAsync(GatewayContext context) => invoke(context);
    }
}

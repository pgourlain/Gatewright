// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Abstractions;

/// <summary>A policy compiled for one configuration. Instances are created once per snapshot and must be thread-safe.</summary>
public interface IPolicyInstance
{
    /// <summary>Runs the policy for the phase given by <see cref="GatewayContext.Phase"/>.</summary>
    /// <param name="context">The request context.</param>
    /// <returns>The result telling whether the pipeline continues.</returns>
    ValueTask<PolicyResult> InvokeAsync(GatewayContext context);
}

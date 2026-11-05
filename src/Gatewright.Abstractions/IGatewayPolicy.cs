// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Text.Json;

namespace Gatewright.Abstractions;

/// <summary>A policy type: describes a kind of policy and compiles configurations into runnable instances.</summary>
public interface IGatewayPolicy
{
    /// <summary>Gets the unique policy type name used in configuration (for example <c>rateLimit</c>).</summary>
    string Type { get; }

    /// <summary>Gets the phases in which this policy type can run.</summary>
    PolicyPhase Phases { get; }

    /// <summary>Gets the JSON Schema (2020-12) that configurations of this policy must satisfy.</summary>
    JsonElement ConfigSchema { get; }

    /// <summary>Compiles a validated configuration into a runnable instance. Called once per snapshot, never per request.</summary>
    /// <param name="config">The policy configuration, already validated against <see cref="ConfigSchema"/>.</param>
    /// <param name="context">Information about where the policy is declared.</param>
    /// <returns>The compiled instance.</returns>
    IPolicyInstance Compile(JsonElement config, PolicyCompilationContext context);
}

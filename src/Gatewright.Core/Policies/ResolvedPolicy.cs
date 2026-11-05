// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Text.Json;

namespace Gatewright.Core.Policies;

/// <summary>A policy of an effective chain, with shared references resolved.</summary>
/// <param name="Type">The policy type.</param>
/// <param name="Config">The policy configuration, if any.</param>
/// <param name="Scope">The scope the policy was declared in.</param>
/// <param name="SharedName">The shared policy name when the policy came from a reference.</param>
public sealed record ResolvedPolicy(string Type, JsonElement? Config, PolicyScope Scope, string? SharedName = null);

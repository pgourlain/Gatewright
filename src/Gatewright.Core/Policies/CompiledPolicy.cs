// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Abstractions;

namespace Gatewright.Core.Policies;

/// <summary>A policy compiled for one position of a chain.</summary>
/// <param name="Type">The policy type.</param>
/// <param name="Instance">The runnable instance.</param>
/// <param name="Timeout">Maximum run time, or <see langword="null"/> to use the pipeline default.</param>
public sealed record CompiledPolicy(string Type, IPolicyInstance Instance, TimeSpan? Timeout = null);

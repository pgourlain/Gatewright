// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Abstractions;

namespace Gatewright.Core.Policies;

/// <summary>The compiled policy chains of one API operation (or of a whole API when it declares no operation).</summary>
/// <param name="Api">The API name.</param>
/// <param name="Operation">The operation name, or <see langword="null"/>.</param>
/// <param name="Chains">The compiled chain of each phase.</param>
public sealed record CompiledOperation(
    string Api,
    string? Operation,
    IReadOnlyDictionary<PolicyPhase, IReadOnlyList<CompiledPolicy>> Chains);

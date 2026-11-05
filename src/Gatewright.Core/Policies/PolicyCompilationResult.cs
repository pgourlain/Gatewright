// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Core.Policies;

/// <summary>The outcome of compiling a whole configuration.</summary>
/// <param name="Operations">The compiled operations; empty when there are errors.</param>
/// <param name="Errors">All errors found; compilation does not stop at the first one.</param>
public sealed record PolicyCompilationResult(IReadOnlyList<CompiledOperation> Operations, IReadOnlyList<PolicyCompilationError> Errors)
{
    /// <summary>Gets a value indicating whether compilation succeeded.</summary>
    public bool IsSuccess => Errors.Count == 0;
}

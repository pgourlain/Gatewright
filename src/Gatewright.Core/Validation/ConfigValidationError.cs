// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Core.Validation;

/// <summary>A problem found in a gateway configuration.</summary>
/// <param name="Code">A stable machine-readable rule code (for example <c>api.duplicate-name</c>).</param>
/// <param name="Path">Where the problem is (for example <c>apis[orders].operations[get]</c>).</param>
/// <param name="Message">A human-readable description.</param>
public sealed record ConfigValidationError(string Code, string Path, string Message)
{
    /// <inheritdoc/>
    public override string ToString() => $"[{Code}] {Path}: {Message}";
}

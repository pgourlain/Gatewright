// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Core.Policies;

/// <summary>An error found while compiling the policies of an API operation.</summary>
/// <param name="Api">The API name.</param>
/// <param name="Operation">The operation name, or <see langword="null"/> for an API-level route.</param>
/// <param name="PolicyType">The policy type.</param>
/// <param name="JsonPath">JSON Pointer inside the policy configuration, empty when not applicable.</param>
/// <param name="Message">What is wrong.</param>
public sealed record PolicyCompilationError(string Api, string? Operation, string PolicyType, string JsonPath, string Message)
{
    /// <inheritdoc/>
    public override string ToString() =>
        $"{Api}{(Operation is null ? "" : "/" + Operation)} policy '{PolicyType}' at '{JsonPath}': {Message}";
}

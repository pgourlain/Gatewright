// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Core.Policies;

/// <summary>A schema violation in a policy configuration.</summary>
/// <param name="JsonPath">JSON Pointer to the offending value inside the policy configuration.</param>
/// <param name="Message">What is wrong.</param>
public sealed record PolicyConfigError(string JsonPath, string Message);

// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Text.Json;

namespace Gatewright.Core.Configuration;

/// <summary>A policy declared in a scope: either inline (type + configuration) or a reference to a shared named policy.</summary>
public sealed record PolicyReference
{
    /// <summary>Gets the policy type (for example <c>rateLimit</c>). Required for inline policies; resolved from the shared policy when <see cref="Ref"/> is set.</summary>
    public string? Type { get; init; }

    /// <summary>Gets the inline policy configuration.</summary>
    public JsonElement? Config { get; init; }

    /// <summary>Gets the name of a shared policy defined in <see cref="GatewayConfig.Policies"/>.</summary>
    public string? Ref { get; init; }

    /// <summary>Gets how this policy combines with inherited policies.</summary>
    public PolicyInheritance Inheritance { get; init; } = PolicyInheritance.Inherit;
}

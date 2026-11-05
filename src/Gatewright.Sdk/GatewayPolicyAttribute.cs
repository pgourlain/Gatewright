// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Abstractions;

namespace Gatewright.Sdk;

/// <summary>Declares the type name and the phases of a <see cref="GatewayPolicy{TConfig}"/>.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class GatewayPolicyAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="GatewayPolicyAttribute"/> class.</summary>
    /// <param name="type">The policy type name used in configuration, for example <c>acme.fraudCheck</c>. Prefix custom types with your organization to avoid clashes.</param>
    public GatewayPolicyAttribute(string type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        Type = type;
    }

    /// <summary>Gets the policy type name.</summary>
    public string Type { get; }

    /// <summary>Gets or sets the phases in which the policy can run. Defaults to <see cref="PolicyPhase.Inbound"/>.</summary>
    public PolicyPhase Phases { get; set; } = PolicyPhase.Inbound;
}

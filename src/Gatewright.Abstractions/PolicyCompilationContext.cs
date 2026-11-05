// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Abstractions;

/// <summary>Information available to a policy while its configuration is compiled.</summary>
public sealed class PolicyCompilationContext
{
    /// <summary>Initializes a new instance of the <see cref="PolicyCompilationContext"/> class.</summary>
    /// <param name="services">The application services.</param>
    /// <param name="scope">A description of where the policy is declared (for example <c>api:orders/operation:getOrder</c>).</param>
    public PolicyCompilationContext(IServiceProvider services, string scope)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        Services = services;
        Scope = scope;
    }

    /// <summary>Gets the application services.</summary>
    public IServiceProvider Services { get; }

    /// <summary>Gets a description of where the policy is declared.</summary>
    public string Scope { get; }
}

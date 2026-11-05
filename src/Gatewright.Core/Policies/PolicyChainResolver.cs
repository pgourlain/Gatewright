// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Text.Json;
using Gatewright.Abstractions;
using Gatewright.Core.Configuration;

namespace Gatewright.Core.Policies;

/// <summary>
/// Computes the effective policy chain of an operation by walking the scopes
/// <c>global → gateway → product → API → operation</c> and applying <see cref="PolicyInheritance"/> operators.
/// </summary>
public static class PolicyChainResolver
{
    private static readonly PolicyPhase[] Phases = [PolicyPhase.Inbound, PolicyPhase.Backend, PolicyPhase.Outbound, PolicyPhase.OnError];

    /// <summary>Resolves the effective chain of one phase.</summary>
    /// <param name="config">The configuration holding global scopes, products and shared policies.</param>
    /// <param name="api">The API.</param>
    /// <param name="operation">The operation, or <see langword="null"/> to resolve at API scope.</param>
    /// <param name="phase">A single phase.</param>
    /// <returns>The policies in execution order.</returns>
    /// <exception cref="InvalidOperationException">A policy references an unknown shared policy or has no type.</exception>
    public static IReadOnlyList<ResolvedPolicy> Resolve(
        GatewayConfig config,
        ApiDefinition api,
        OperationDefinition? operation,
        PolicyPhase phase)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(api);

        var chain = new List<ResolvedPolicy>();
        Apply(chain, config, config.GlobalPolicies, PolicyScope.Global, phase);
        Apply(chain, config, config.GatewayPolicies, PolicyScope.Gateway, phase);
        foreach (var productName in api.Products)
        {
            foreach (var product in config.Products)
            {
                if (string.Equals(product.Name, productName, StringComparison.Ordinal))
                {
                    Apply(chain, config, product.Policies, PolicyScope.Product, phase);
                }
            }
        }

        Apply(chain, config, api.Policies, PolicyScope.Api, phase);
        if (operation is not null)
        {
            Apply(chain, config, operation.Policies, PolicyScope.Operation, phase);
        }

        return chain;
    }

    /// <summary>Resolves the effective chains of all phases.</summary>
    /// <param name="config">The configuration.</param>
    /// <param name="api">The API.</param>
    /// <param name="operation">The operation, or <see langword="null"/> to resolve at API scope.</param>
    /// <returns>The chain of each phase.</returns>
    public static IReadOnlyDictionary<PolicyPhase, IReadOnlyList<ResolvedPolicy>> ResolveAll(
        GatewayConfig config,
        ApiDefinition api,
        OperationDefinition? operation)
    {
        var result = new Dictionary<PolicyPhase, IReadOnlyList<ResolvedPolicy>>(Phases.Length);
        foreach (var phase in Phases)
        {
            result[phase] = Resolve(config, api, operation, phase);
        }

        return result;
    }

    private static void Apply(List<ResolvedPolicy> chain, GatewayConfig config, PolicySet set, PolicyScope scope, PolicyPhase phase)
    {
        foreach (var reference in set.Get(phase))
        {
            var (type, settings, sharedName) = Resolve(config, reference);
            switch (reference.Inheritance)
            {
                case PolicyInheritance.Inherit:
                    chain.Add(new ResolvedPolicy(type, settings, scope, sharedName));
                    break;

                case PolicyInheritance.Override:
                    var replacement = new ResolvedPolicy(type, settings, scope, sharedName);
                    var index = chain.FindIndex(p => p.Type == type);
                    if (index < 0)
                    {
                        chain.Add(replacement);
                    }
                    else
                    {
                        chain.RemoveAll(p => p.Type == type);
                        chain.Insert(Math.Min(index, chain.Count), replacement);
                    }

                    break;

                case PolicyInheritance.Remove:
                    chain.RemoveAll(p => p.Type == type);
                    break;

                default:
                    throw new InvalidOperationException($"Unknown inheritance operator '{reference.Inheritance}'.");
            }
        }
    }

    private static (string Type, JsonElement? Config, string? SharedName) Resolve(GatewayConfig config, PolicyReference reference)
    {
        if (reference.Ref is { } name)
        {
            if (!config.Policies.TryGetValue(name, out var shared))
            {
                throw new InvalidOperationException($"Shared policy '{name}' is not defined.");
            }

            var sharedType = shared.Type ?? throw new InvalidOperationException($"Shared policy '{name}' has no type.");
            return (sharedType, shared.Config, name);
        }

        var type = reference.Type ?? throw new InvalidOperationException("A policy must declare a type or a reference.");
        return (type, reference.Config, null);
    }
}

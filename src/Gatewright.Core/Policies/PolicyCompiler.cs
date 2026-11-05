// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Abstractions;
using Gatewright.Core.Configuration;

namespace Gatewright.Core.Policies;

/// <summary>
/// Validates and compiles every effective policy of a configuration. Run once per snapshot,
/// never per request.
/// </summary>
public sealed class PolicyCompiler
{
    private readonly PolicyRegistry _registry;
    private readonly IServiceProvider _services;

    /// <summary>Initializes a new instance of the <see cref="PolicyCompiler"/> class.</summary>
    /// <param name="registry">The available policy types.</param>
    /// <param name="services">Services made available to policies while they compile.</param>
    public PolicyCompiler(PolicyRegistry registry, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(services);
        _registry = registry;
        _services = services;
    }

    /// <summary>Compiles all operations of a configuration.</summary>
    /// <param name="config">The configuration.</param>
    /// <returns>The compiled operations, or every error found.</returns>
    public PolicyCompilationResult Compile(GatewayConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var operations = new List<CompiledOperation>();
        var errors = new List<PolicyCompilationError>();

        foreach (var api in config.Apis)
        {
            if (api.Operations.Count == 0)
            {
                CompileTarget(config, api, null, operations, errors);
                continue;
            }

            foreach (var operation in api.Operations)
            {
                CompileTarget(config, api, operation, operations, errors);
            }
        }

        return new PolicyCompilationResult(errors.Count == 0 ? operations : [], errors);
    }

    private void CompileTarget(
        GatewayConfig config,
        ApiDefinition api,
        OperationDefinition? operation,
        List<CompiledOperation> operations,
        List<PolicyCompilationError> errors)
    {
        var chains = new Dictionary<PolicyPhase, IReadOnlyList<CompiledPolicy>>();
        var scope = operation is null ? $"api:{api.Name}" : $"api:{api.Name}/operation:{operation.Name}";
        var context = new PolicyCompilationContext(_services, scope);

        foreach (var (phase, resolved) in ResolveChains(config, api, operation, api.Name, errors))
        {
            var compiled = new List<CompiledPolicy>(resolved.Count);
            foreach (var policy in resolved)
            {
                if (!_registry.TryGet(policy.Type, out var definition))
                {
                    errors.Add(new PolicyCompilationError(api.Name, operation?.Name, policy.Type, "", "Unknown policy type."));
                    continue;
                }

                if (!definition.Phases.HasFlag(phase))
                {
                    errors.Add(new PolicyCompilationError(
                        api.Name, operation?.Name, policy.Type, "", $"Policy cannot run in phase {phase}."));
                    continue;
                }

                var configErrors = _registry.Validate(policy.Type, policy.Config);
                if (configErrors.Count > 0)
                {
                    errors.AddRange(configErrors.Select(
                        e => new PolicyCompilationError(api.Name, operation?.Name, policy.Type, e.JsonPath, e.Message)));
                    continue;
                }

                try
                {
                    var instance = definition.Compile(policy.Config ?? default, context);
                    compiled.Add(new CompiledPolicy(policy.Type, instance));
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    errors.Add(new PolicyCompilationError(api.Name, operation?.Name, policy.Type, "", $"Compilation failed: {ex.Message}"));
                }
            }

            chains[phase] = compiled;
        }

        operations.Add(new CompiledOperation(api.Name, operation?.Name, chains));
    }

    private static IEnumerable<KeyValuePair<PolicyPhase, IReadOnlyList<ResolvedPolicy>>> ResolveChains(
        GatewayConfig config,
        ApiDefinition api,
        OperationDefinition? operation,
        string apiName,
        List<PolicyCompilationError> errors)
    {
        try
        {
            return PolicyChainResolver.ResolveAll(config, api, operation);
        }
        catch (InvalidOperationException ex)
        {
            errors.Add(new PolicyCompilationError(apiName, operation?.Name, "", "", ex.Message));
            return [];
        }
    }
}

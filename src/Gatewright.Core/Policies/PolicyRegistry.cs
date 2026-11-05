// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Text.Json;
using Gatewright.Abstractions;
using Json.Schema;

namespace Gatewright.Core.Policies;

/// <summary>The policy types available to the gateway, usually filled from dependency injection.</summary>
public sealed class PolicyRegistry
{
    // JsonSchema.Net keeps a process-wide, non-thread-safe schema registry that is written when a schema is built and read when it
    // is evaluated. Both happen at configuration compile time (never per request), so one lock keeps parallel hosts safe.
    private static readonly Lock SchemaGate = new();

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    /// <summary>Initializes a new instance of the <see cref="PolicyRegistry"/> class.</summary>
    /// <param name="policies">The registered policy types.</param>
    /// <exception cref="ArgumentException">Two policies use the same type name.</exception>
    public PolicyRegistry(IEnumerable<IGatewayPolicy> policies)
    {
        ArgumentNullException.ThrowIfNull(policies);
        foreach (var policy in policies)
        {
            JsonSchema schema;
            lock (SchemaGate)
            {
                schema = JsonSchema.FromText(policy.ConfigSchema.GetRawText());
            }

            if (!_entries.TryAdd(policy.Type, new Entry(policy, schema)))
            {
                throw new ArgumentException($"Policy type '{policy.Type}' is registered more than once.", nameof(policies));
            }
        }
    }

    /// <summary>Gets the registered policy type names.</summary>
    public IReadOnlyCollection<string> Types => _entries.Keys;

    /// <summary>Describes every registered policy type, ordered by name.</summary>
    /// <returns>The descriptors.</returns>
    public IReadOnlyList<PolicyDescriptor> Describe() =>
        [.. _entries.Values
            .OrderBy(e => e.Policy.Type, StringComparer.Ordinal)
            .Select(e => new PolicyDescriptor(e.Policy.Type, e.Policy.Phases, e.Policy.ConfigSchema))];

    /// <summary>Looks up a policy type.</summary>
    /// <param name="type">The type name.</param>
    /// <param name="policy">The policy, when found.</param>
    /// <returns><see langword="true"/> when the type is registered.</returns>
    public bool TryGet(string type, out IGatewayPolicy policy)
    {
        if (_entries.TryGetValue(type, out var entry))
        {
            policy = entry.Policy;
            return true;
        }

        policy = null!;
        return false;
    }

    /// <summary>Validates a configuration against the JSON Schema of its policy type.</summary>
    /// <param name="type">The type name.</param>
    /// <param name="config">The configuration; <see langword="null"/> is validated as an empty object.</param>
    /// <returns>The validation errors; empty when valid.</returns>
    /// <exception cref="KeyNotFoundException">The type is not registered.</exception>
    public IReadOnlyList<PolicyConfigError> Validate(string type, JsonElement? config)
    {
        var entry = _entries[type];
        using var empty = JsonDocument.Parse("{}");
        var instance = config ?? empty.RootElement;
        EvaluationResults results;
        lock (SchemaGate)
        {
            results = entry.Schema.Evaluate(instance, new EvaluationOptions { OutputFormat = OutputFormat.List });
        }

        if (results.IsValid)
        {
            return [];
        }

        var errors = new List<PolicyConfigError>();
        foreach (var detail in results.Details ?? [])
        {
            if (detail.Errors is null)
            {
                continue;
            }

            foreach (var error in detail.Errors)
            {
                errors.Add(new PolicyConfigError(detail.InstanceLocation.ToString(), error.Value));
            }
        }

        if (errors.Count == 0)
        {
            errors.Add(new PolicyConfigError("", "Configuration does not match the policy schema."));
        }

        return errors;
    }

    private sealed record Entry(IGatewayPolicy Policy, JsonSchema Schema);
}

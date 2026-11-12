// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Text.Json;
using System.Text.Json.Serialization;
using Gatewright.Abstractions;

namespace Gatewright.Policies.Internal;

/// <summary>Base class of built-in policies: deserializes the configuration into <typeparamref name="TConfig"/> and delegates to <see cref="Create"/>.</summary>
/// <typeparam name="TConfig">The configuration type.</typeparam>
public abstract class PolicyBase<TConfig> : IGatewayPolicy
    where TConfig : class, new()
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    private readonly Lazy<JsonElement> _schema;

    /// <summary>Initializes a new instance of the <see cref="PolicyBase{TConfig}"/> class.</summary>
    /// <param name="type">The policy type name.</param>
    /// <param name="phases">The supported phases.</param>
    /// <param name="schemaJson">The JSON Schema of the configuration.</param>
    protected PolicyBase(string type, PolicyPhase phases, string schemaJson)
    {
        Type = type;
        Phases = phases;
        _schema = new Lazy<JsonElement>(() => JsonElement.Parse(schemaJson));
    }

    /// <inheritdoc/>
    public string Type { get; }

    /// <inheritdoc/>
    public PolicyPhase Phases { get; }

    /// <inheritdoc/>
    public JsonElement ConfigSchema => _schema.Value;

    /// <inheritdoc/>
    public IPolicyInstance Compile(JsonElement config, PolicyCompilationContext context)
    {
        var typed = config.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? new TConfig()
            : config.Deserialize<TConfig>(Options) ?? new TConfig();
        return Create(typed, context);
    }

    /// <summary>Compiles a typed configuration.</summary>
    /// <param name="config">The configuration.</param>
    /// <param name="context">The compilation context.</param>
    /// <returns>The runnable instance.</returns>
    protected abstract IPolicyInstance Create(TConfig config, PolicyCompilationContext context);

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { AllowTrailingCommas = true };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}

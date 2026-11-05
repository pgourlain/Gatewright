// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using Gatewright.Abstractions;

namespace Gatewright.Sdk;

/// <summary>
/// Base class for custom policies. The configuration type <typeparamref name="TConfig"/> is a plain C# class or record: its JSON Schema is generated
/// from it (properties become camelCase, <c>required</c> members become required, <see cref="System.ComponentModel.DescriptionAttribute"/> becomes the description,
/// unknown properties are rejected), and configurations are deserialized into it once per snapshot.
/// </summary>
/// <typeparam name="TConfig">The configuration type.</typeparam>
public abstract class GatewayPolicy<TConfig> : IGatewayPolicy
    where TConfig : class
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    private readonly GatewayPolicyAttribute _attribute;
    private readonly Lazy<JsonElement> _schema;

    /// <summary>Initializes a new instance of the <see cref="GatewayPolicy{TConfig}"/> class.</summary>
    /// <exception cref="InvalidOperationException">The class is not decorated with <see cref="GatewayPolicyAttribute"/>.</exception>
    protected GatewayPolicy()
    {
        _attribute = (GatewayPolicyAttribute?)Attribute.GetCustomAttribute(GetType(), typeof(GatewayPolicyAttribute), inherit: false)
            ?? throw new InvalidOperationException($"{GetType().Name} must be decorated with [GatewayPolicy(\"type\")].");
        _schema = new Lazy<JsonElement>(BuildSchema);
    }

    /// <inheritdoc/>
    public string Type => _attribute.Type;

    /// <inheritdoc/>
    public PolicyPhase Phases => _attribute.Phases;

    /// <inheritdoc/>
    public JsonElement ConfigSchema => _schema.Value;

    /// <inheritdoc/>
    public IPolicyInstance Compile(JsonElement config, PolicyCompilationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        TConfig typed;
        if (config.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            typed = Activator.CreateInstance<TConfig>();
        }
        else
        {
            typed = config.Deserialize<TConfig>(Options)
                ?? throw new InvalidOperationException($"The configuration of policy '{Type}' is empty.");
        }

        return Create(typed, context);
    }

    /// <summary>Compiles a typed configuration into a runnable instance. Called once per snapshot, never per request: do the expensive work here.</summary>
    /// <param name="config">The validated configuration.</param>
    /// <param name="context">Information about where the policy is declared and the application services.</param>
    /// <returns>The instance, which must be thread-safe.</returns>
    protected abstract IPolicyInstance Create(TConfig config, PolicyCompilationContext context);

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { AllowTrailingCommas = true, NumberHandling = JsonNumberHandling.Strict };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    private static JsonElement BuildSchema()
    {
        var node = JsonSchemaExporter.GetJsonSchemaAsNode(
            Options,
            typeof(TConfig),
            new JsonSchemaExporterOptions
            {
                TreatNullObliviousAsNonNullable = true,
                TransformSchemaNode = static (context, schema) =>
                {
                    if (schema is not JsonObject obj)
                    {
                        return schema;
                    }

                    if (obj["type"]?.ToString() == "object" && obj["properties"] is not null)
                    {
                        obj["additionalProperties"] = false;
                    }

                    var provider = context.PropertyInfo?.AttributeProvider ?? context.TypeInfo.Type;
                    if (provider.GetCustomAttributes(typeof(DescriptionAttribute), inherit: true).FirstOrDefault() is DescriptionAttribute description)
                    {
                        obj["description"] = description.Description;
                    }

                    return schema;
                },
            });
        return JsonSerializer.SerializeToElement(node);
    }
}

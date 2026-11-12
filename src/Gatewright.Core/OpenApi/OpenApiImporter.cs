// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Gatewright.Core.Configuration;
using YamlDotNet.RepresentationModel;

namespace Gatewright.Core.OpenApi;

/// <summary>
/// Reads an OpenAPI 3.0, 3.1 or 3.2 document (JSON or YAML) and turns it into an <see cref="ApiDefinition"/>: operations from <c>paths</c>, and policies from
/// the <c>x-gateway</c> extensions. Parsing only; nothing is stored.
/// </summary>
public static partial class OpenApiImporter
{
    private static readonly string[] Methods = ["get", "put", "post", "delete", "options", "head", "patch", "trace"];

    private static readonly Dictionary<string, (string Type, string[] Phases)> Shortcuts = new(StringComparer.Ordinal)
    {
        ["jwt"] = ("jwt", ["inbound"]),
        ["apiKey"] = ("apiKey", ["inbound"]),
        ["ipFilter"] = ("ipFilter", ["inbound"]),
        ["rateLimit"] = ("rateLimit", ["inbound"]),
        ["quota"] = ("quota", ["inbound"]),
        ["cache"] = ("cache", ["inbound"]),
        ["rewrite"] = ("rewrite", ["inbound"]),
        ["mockResponse"] = ("mockResponse", ["inbound"]),
        ["cors"] = ("cors", ["inbound", "outbound"]),
        ["timeout"] = ("timeout", ["backend"]),
        ["retry"] = ("retry", ["backend"]),
        ["circuitBreaker"] = ("circuitBreaker", ["backend"]),
    };

    /// <summary>Reads a document.</summary>
    /// <param name="document">The OpenAPI text, JSON or YAML.</param>
    /// <param name="options">Import choices.</param>
    /// <returns>The API, or the reasons it cannot be imported.</returns>
    public static OpenApiImportResult Import(string document, OpenApiImportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        options ??= new OpenApiImportOptions();
        var warnings = new List<string>();
        var errors = new List<string>();

        JsonObject root;
        try
        {
            root = Parse(document);
        }
        catch (Exception ex) when (ex is JsonException or YamlDotNet.Core.YamlException or InvalidOperationException)
        {
            return Failed($"The document is not valid JSON or YAML: {ex.Message}");
        }

        var version = root["openapi"]?.GetValue<string>() ?? "";
        if (!version.StartsWith("3.0", StringComparison.Ordinal) && !version.StartsWith("3.1", StringComparison.Ordinal) && !version.StartsWith("3.2", StringComparison.Ordinal))
        {
            return Failed($"Unsupported OpenAPI version '{version}'. Gatewright reads OpenAPI 3.0, 3.1 and 3.2.");
        }

        if (root[XGateway.VersionExtension] is { } declared && declared.GetValue<long>() > XGateway.CurrentVersion)
        {
            return Failed($"The document uses x-gateway-version {declared}, but this Gatewright understands up to {XGateway.CurrentVersion}.");
        }

        var info = root["info"] as JsonObject;
        var title = info?["title"]?.GetValue<string>();
        var global = ReadExtension(root, "document", warnings, errors);

        var name = options.Name ?? global?.Name ?? Slug(title ?? "");
        if (name.Length == 0)
        {
            errors.Add("The API needs a name: set info.title, x-gateway.name or pass a name.");
        }

        var basePath = options.BasePath ?? global?.BasePath ?? ServerPath(root) ?? ("/" + name);
        if (basePath.Length == 0 || basePath[0] != '/')
        {
            basePath = "/" + basePath;
        }

        var backend = options.Backend ?? global?.Backend;
        if (backend is null)
        {
            errors.Add("No backend: set x-gateway.backend in the document or pass a backend (a service reference or destinations).");
        }

        var operations = ReadOperations(root, warnings, errors);
        if (operations.Count == 0 && errors.Count == 0)
        {
            warnings.Add("The document declares no operation: the whole base path will be routed to the backend.");
        }

        if (errors.Count > 0 || backend is null)
        {
            return new OpenApiImportResult(null, title, [], warnings, errors);
        }

        var products = options.Products ?? global?.Products ?? [];
        var api = new ApiDefinition
        {
            Name = name,
            BasePath = basePath,
            Version = info?["version"]?.GetValue<string>(),
            Backend = backend,
            Products = [.. products],
            Operations = operations,
            Policies = global?.Policies ?? PolicySet.Empty,
        };
        return new OpenApiImportResult(api, title, products, warnings, errors);

        OpenApiImportResult Failed(string message) => new(null, null, [], warnings, [message]);
    }

    private static List<OperationDefinition> ReadOperations(JsonObject root, List<string> warnings, List<string> errors)
    {
        var operations = new List<OperationDefinition>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (root["paths"] is not JsonObject paths)
        {
            return operations;
        }

        foreach (var (path, item) in paths.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (item is not JsonObject pathItem)
            {
                continue;
            }

            foreach (var method in Methods)
            {
                if (pathItem[method] is not JsonObject operation)
                {
                    continue;
                }

                var name = operation["operationId"]?.GetValue<string>() is { Length: > 0 } id
                    ? Sanitize(id)
                    : Slug($"{method}-{path}");
                if (!names.Add(name))
                {
                    var unique = name;
                    for (var i = 2; !names.Add(unique = $"{name}-{i}"); i++)
                    {
                    }

                    warnings.Add($"Operation name '{name}' is used twice ({method.ToUpperInvariant()} {path}); renamed '{unique}'.");
                    name = unique;
                }

                var extension = ReadExtension(operation, $"{method.ToUpperInvariant()} {path}", warnings, errors);
                operations.Add(new OperationDefinition
                {
                    Name = name,
                    Method = method.ToUpperInvariant(),
                    Path = path,
                    Policies = extension?.Policies ?? PolicySet.Empty,
                });
            }
        }

        return operations;
    }

    private static Extension? ReadExtension(JsonObject owner, string where, List<string> warnings, List<string> errors)
    {
        if (owner[XGateway.Extension] is null)
        {
            return null;
        }

        if (owner[XGateway.Extension] is not JsonObject node)
        {
            errors.Add($"{where}: x-gateway must be an object.");
            return null;
        }

        string? name = null;
        string? basePath = null;
        List<string>? products = null;
        BackendDefinition? backend = null;
        var sets = new Dictionary<string, List<PolicyReference>>(StringComparer.Ordinal)
        {
            ["inbound"] = [],
            ["backend"] = [],
            ["outbound"] = [],
            ["onError"] = [],
        };

        foreach (var (key, value) in node)
        {
            try
            {
                switch (key)
                {
                    case "name":
                        name = value?.GetValue<string>();
                        break;
                    case "basePath":
                        basePath = value?.GetValue<string>();
                        break;
                    case "product":
                        products = [value!.GetValue<string>()];
                        break;
                    case "products":
                        products = [.. value!.AsArray().Select(p => p!.GetValue<string>())];
                        break;
                    case "backend":
                        backend = GatewayJson.Deserialize<BackendDefinition>(value!.ToJsonString());
                        break;
                    case "policies":
                        var declared = GatewayJson.Deserialize<PolicySet>(value!.ToJsonString());
                        sets["inbound"].AddRange(declared.Inbound);
                        sets["backend"].AddRange(declared.Backend);
                        sets["outbound"].AddRange(declared.Outbound);
                        sets["onError"].AddRange(declared.OnError);
                        break;
                    default:
                        if (Shortcuts.TryGetValue(key, out var shortcut))
                        {
                            var reference = new PolicyReference { Type = shortcut.Type, Config = JsonElement.Parse(value?.ToJsonString() ?? "{}") };
                            foreach (var phase in shortcut.Phases)
                            {
                                sets[phase].Add(reference);
                            }
                        }
                        else
                        {
                            warnings.Add($"{where}: unknown x-gateway key '{key}' ignored.");
                        }

                        break;
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
            {
                errors.Add($"{where}: invalid x-gateway.{key}: {ex.Message}");
            }
        }

        return new Extension(
            name,
            basePath,
            products,
            backend,
            new PolicySet { Inbound = sets["inbound"], Backend = sets["backend"], Outbound = sets["outbound"], OnError = sets["onError"] });
    }

    private static JsonObject Parse(string text)
    {
        var trimmed = text.TrimStart();
        if (!trimmed.StartsWith('{'))
        {
            var yaml = new YamlStream();
            yaml.Load(new StringReader(text));
            if (yaml.Documents.Count == 0)
            {
                throw new InvalidOperationException("The document is empty.");
            }

            return ConvertYaml(yaml.Documents[0].RootNode) as JsonObject
                ?? throw new InvalidOperationException("The document must be a mapping at the top level.");
        }

        return JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip })?.AsObject()
            ?? throw new InvalidOperationException("The document is empty.");
    }

    /// <summary>Converts YAML to JSON applying the YAML 1.2 core schema: plain scalars become booleans, null or numbers when they look like them; quoted ones stay strings.</summary>
    private static JsonNode? ConvertYaml(YamlNode node)
    {
        switch (node)
        {
            case YamlMappingNode mapping:
                var obj = new JsonObject();
                foreach (var (key, value) in mapping.Children)
                {
                    obj[((YamlScalarNode)key).Value ?? ""] = ConvertYaml(value);
                }

                return obj;
            case YamlSequenceNode sequence:
                var array = new JsonArray();
                foreach (var item in sequence.Children)
                {
                    array.Add(ConvertYaml(item));
                }

                return array;
            case YamlScalarNode scalar:
                var text = scalar.Value ?? "";
                if (scalar.Style != YamlDotNet.Core.ScalarStyle.Plain)
                {
                    return JsonValue.Create(text);
                }

                if (text is "null" or "Null" or "NULL" or "~" or "")
                {
                    return null;
                }

                if (text is "true" or "True" or "TRUE")
                {
                    return JsonValue.Create(true);
                }

                if (text is "false" or "False" or "FALSE")
                {
                    return JsonValue.Create(false);
                }

                if (long.TryParse(text, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var integer))
                {
                    return JsonValue.Create(integer);
                }

                if (IsFloat().IsMatch(text) && double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number))
                {
                    return JsonValue.Create(number);
                }

                return JsonValue.Create(text);
            default:
                throw new InvalidOperationException("Unsupported YAML construct (aliases and tags are not supported).");
        }
    }

    private static string? ServerPath(JsonObject root)
    {
        var url = (root["servers"] as JsonArray)?.FirstOrDefault()?["url"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var path = Uri.TryCreate(url, UriKind.Absolute, out var absolute) ? absolute.AbsolutePath : url;
        path = path.TrimEnd('/');
        return path.Length == 0 || path.Contains('{', StringComparison.Ordinal) ? null : path;
    }

    private static string Slug(string value)
    {
        var slug = NonAlphanumeric().Replace(value.Trim().ToLowerInvariant(), "-").Trim('-');
        return slug;
    }

    private static string Sanitize(string value)
    {
        var sanitized = NonName().Replace(value.Trim(), "-").Trim('-');
        return sanitized.Length == 0 ? "operation" : sanitized;
    }

    [GeneratedRegex(@"^[-+]?(\.[0-9]+|[0-9]+(\.[0-9]*)?)([eE][-+]?[0-9]+)?$")]
    private static partial Regex IsFloat();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphanumeric();

    [GeneratedRegex("[^A-Za-z0-9._-]+")]
    private static partial Regex NonName();

    private sealed record Extension(string? Name, string? BasePath, List<string>? Products, BackendDefinition? Backend, PolicySet Policies);
}

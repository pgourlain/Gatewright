// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Text.RegularExpressions;
using Gatewright.Core.Configuration;
using Gatewright.Core.Policies;

namespace Gatewright.Core.Validation;

/// <summary>Validates the structure and consistency of a <see cref="GatewayConfig"/>. All errors are reported, not only the first.</summary>
public sealed partial class GatewayConfigValidator
{
    private static readonly HashSet<string> HttpMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS", "TRACE",
    };

    private readonly PolicyRegistry? _registry;

    /// <summary>Initializes a new instance of the <see cref="GatewayConfigValidator"/> class.</summary>
    /// <param name="registry">The available policy types; when <see langword="null"/>, unknown policy types are not reported.</param>
    public GatewayConfigValidator(PolicyRegistry? registry = null)
    {
        _registry = registry;
    }

    /// <summary>Validates a configuration.</summary>
    /// <param name="config">The configuration.</param>
    /// <returns>All errors found; empty when valid.</returns>
    public IReadOnlyList<ConfigValidationError> Validate(GatewayConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var errors = new List<ConfigValidationError>();

        foreach (var (name, policy) in config.Policies)
        {
            if (policy.Ref is not null)
            {
                Add(errors, "policy.shared-reference", $"policies[{name}]", "A shared policy cannot reference another shared policy.");
            }

            ValidatePolicy(config, policy, $"policies[{name}]", errors);
        }

        ValidateSet(config, config.GlobalPolicies, "globalPolicies", errors);
        ValidateSet(config, config.GatewayPolicies, "gatewayPolicies", errors);

        ValidateHostnames(config, errors);
        ValidateProducts(config, errors);
        ValidateSubscriptions(config, errors);
        ValidateApis(config, errors);
        return errors;
    }

    private static string NormalizePath(string path)
    {
        var trimmed = path.Trim().TrimEnd('/');
        return trimmed.Length == 0 ? "/" : trimmed.ToLowerInvariant();
    }

    private static void Add(List<ConfigValidationError> errors, string code, string path, string message) =>
        errors.Add(new ConfigValidationError(code, path, message));

    private void ValidateProducts(GatewayConfig config, List<ConfigValidationError> errors)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var product in config.Products)
        {
            var path = $"products[{product.Name}]";
            if (string.IsNullOrWhiteSpace(product.Name))
            {
                Add(errors, "product.name-required", "products[]", "Product name is required.");
            }
            else if (!names.Add(product.Name))
            {
                Add(errors, "product.duplicate-name", path, $"Product name '{product.Name}' is used more than once.");
            }

            ValidateSet(config, product.Policies, path + ".policies", errors);
        }
    }

    private static void ValidateHostnames(GatewayConfig config, List<ConfigValidationError> errors)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var hostname in config.Hostnames)
        {
            var path = $"hostnames[{hostname.Name}]";
            if (!HostnameRegex().IsMatch(hostname.Name ?? ""))
            {
                Add(errors, "hostname.invalid", path, "Hostname must be a DNS name, a wildcard such as '*.example.com', or '*'.");
            }
            else if (!names.Add(hostname.Name!))
            {
                Add(errors, "hostname.duplicate", path, $"Hostname '{hostname.Name}' is declared more than once.");
            }

            if (hostname.Certificate is { } certificate)
            {
                var sources = (certificate.File is null ? 0 : 1) + (string.IsNullOrWhiteSpace(certificate.Secret) ? 0 : 1);
                if (sources != 1)
                {
                    Add(errors, "hostname.certificate-source", path + ".certificate", "Exactly one of 'file' or 'secret' must be set.");
                }

                if (certificate.File is { } files && (string.IsNullOrWhiteSpace(files.CertPath) || string.IsNullOrWhiteSpace(files.KeyPath)))
                {
                    Add(errors, "hostname.certificate-files", path + ".certificate.file", "Both certPath and keyPath are required.");
                }

                if (!string.IsNullOrWhiteSpace(certificate.Secret) && !SecretRegex().IsMatch(certificate.Secret))
                {
                    Add(errors, "hostname.certificate-secret", path + ".certificate.secret", "Secret must look like 'namespace/name'.");
                }
            }

            if (hostname.Hsts is { MaxAgeSeconds: < 0 })
            {
                Add(errors, "hostname.hsts", path + ".hsts", "maxAgeSeconds must not be negative.");
            }
        }
    }

    private static void ValidateSubscriptions(GatewayConfig config, List<ConfigValidationError> errors)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        var products = config.Products.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var subscription in config.Subscriptions)
        {
            var path = $"subscriptions[{subscription.Id}]";
            if (!ids.Add(subscription.Id))
            {
                Add(errors, "subscription.duplicate-id", path, $"Subscription id '{subscription.Id}' is used more than once.");
            }

            if (!products.Contains(subscription.Product))
            {
                Add(errors, "subscription.unknown-product", path + ".product", $"Product '{subscription.Product}' does not exist.");
            }

            foreach (var hash in subscription.KeyHashes)
            {
                if (!hashes.Add(hash.ToLowerInvariant()))
                {
                    Add(errors, "subscription.duplicate-key", path + ".keyHashes", "A key hash is used by more than one subscription.");
                }
            }
        }
    }

    private void ValidateApis(GatewayConfig config, List<ConfigValidationError> errors)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var basePaths = new Dictionary<string, string>(StringComparer.Ordinal);
        var productNames = config.Products.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var api in config.Apis)
        {
            var path = $"apis[{api.Name}]";
            if (string.IsNullOrWhiteSpace(api.Name))
            {
                Add(errors, "api.name-required", "apis[]", "API name is required.");
            }
            else if (!names.Add(api.Name))
            {
                Add(errors, "api.duplicate-name", path, $"API name '{api.Name}' is used more than once.");
            }

            if (string.IsNullOrEmpty(api.BasePath) || api.BasePath[0] != '/')
            {
                Add(errors, "api.base-path-invalid", path + ".basePath", "Base path must start with '/'.");
            }
            else
            {
                var key = NormalizePath(api.BasePath);
                if (basePaths.TryGetValue(key, out var other))
                {
                    Add(errors, "api.path-conflict", path + ".basePath", $"Base path '{api.BasePath}' conflicts with API '{other}'.");
                }
                else
                {
                    basePaths[key] = api.Name;
                }
            }

            foreach (var product in api.Products)
            {
                if (!productNames.Contains(product))
                {
                    Add(errors, "api.unknown-product", path + ".products", $"Product '{product}' does not exist.");
                }
            }

            ValidateBackend(api.Backend, path + ".backend", errors);
            ValidateSet(config, api.Policies, path + ".policies", errors);
            ValidateOperations(config, api, path, errors);
        }
    }

    private void ValidateOperations(GatewayConfig config, ApiDefinition api, string apiPath, List<ConfigValidationError> errors)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var routes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var operation in api.Operations)
        {
            var path = $"{apiPath}.operations[{operation.Name}]";
            if (string.IsNullOrWhiteSpace(operation.Name))
            {
                Add(errors, "operation.name-required", apiPath + ".operations[]", "Operation name is required.");
            }
            else if (!names.Add(operation.Name))
            {
                Add(errors, "operation.duplicate-name", path, $"Operation name '{operation.Name}' is used more than once in the API.");
            }

            if (!HttpMethods.Contains(operation.Method ?? ""))
            {
                Add(errors, "operation.method-invalid", path + ".method", $"'{operation.Method}' is not a valid HTTP method.");
            }

            if (string.IsNullOrEmpty(operation.Path) || operation.Path[0] != '/')
            {
                Add(errors, "operation.path-invalid", path + ".path", "Operation path must start with '/'.");
            }
            else if (!routes.Add(operation.Method?.ToUpperInvariant() + " " + RouteShape(operation.Path)))
            {
                Add(errors, "operation.path-conflict", path, $"{operation.Method} {operation.Path} conflicts with another operation of the API.");
            }

            ValidateSet(config, operation.Policies, path + ".policies", errors);
        }
    }

    private static string RouteShape(string path) => ParameterRegex().Replace(NormalizePath(path), "{}");

    private static void ValidateBackend(BackendDefinition backend, string path, List<ConfigValidationError> errors)
    {
        var hasDestinations = backend.Destinations.Count > 0;
        var hasService = !string.IsNullOrWhiteSpace(backend.Service);
        if (hasDestinations == hasService)
        {
            Add(errors, "backend.target", path, "Backend must define either destinations or a service reference.");
        }

        foreach (var destination in backend.Destinations)
        {
            if (!Uri.TryCreate(destination, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            {
                Add(errors, "backend.destination-invalid", path + ".destinations", $"'{destination}' is not an absolute http(s) URL.");
            }
        }

        if (hasService && !ServiceRegex().IsMatch(backend.Service!))
        {
            Add(errors, "backend.service-invalid", path + ".service", $"'{backend.Service}' must look like 'namespace/name:port'.");
        }

        if (backend.Timeout is { } timeout && timeout <= TimeSpan.Zero)
        {
            Add(errors, "backend.timeout-invalid", path + ".timeout", "Timeout must be positive.");
        }
    }

    private void ValidateSet(GatewayConfig config, PolicySet set, string path, List<ConfigValidationError> errors)
    {
        ValidatePhase(config, set.Inbound, path + ".inbound", errors);
        ValidatePhase(config, set.Backend, path + ".backend", errors);
        ValidatePhase(config, set.Outbound, path + ".outbound", errors);
        ValidatePhase(config, set.OnError, path + ".onError", errors);
    }

    private void ValidatePhase(GatewayConfig config, IReadOnlyList<PolicyReference> policies, string path, List<ConfigValidationError> errors)
    {
        for (var i = 0; i < policies.Count; i++)
        {
            ValidatePolicy(config, policies[i], $"{path}[{i}]", errors);
        }
    }

    private void ValidatePolicy(GatewayConfig config, PolicyReference policy, string path, List<ConfigValidationError> errors)
    {
        if (policy.Ref is not null && policy.Type is not null)
        {
            Add(errors, "policy.ambiguous", path, "A policy declares either a type or a reference, not both.");
            return;
        }

        if (policy.Ref is not null)
        {
            if (!config.Policies.ContainsKey(policy.Ref))
            {
                Add(errors, "policy.unknown-reference", path, $"Shared policy '{policy.Ref}' does not exist.");
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(policy.Type))
        {
            Add(errors, "policy.type-required", path, "A policy must declare a type or a reference.");
            return;
        }

        if (_registry is not null && !_registry.TryGet(policy.Type, out _))
        {
            Add(errors, "policy.unknown-type", path, $"Policy type '{policy.Type}' is not registered.");
        }
    }

    [GeneratedRegex(@"\{[^}/]*\}")]
    private static partial Regex ParameterRegex();

    [GeneratedRegex(@"^[^/\s]+/[^/:\s]+:\d{1,5}$")]
    private static partial Regex ServiceRegex();

    [GeneratedRegex(@"^(\*|(\*\.)?([A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?\.)*[A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?)$")]
    private static partial Regex HostnameRegex();

    [GeneratedRegex(@"^[^/\s]+/[^/\s]+$")]
    private static partial Regex SecretRegex();
}

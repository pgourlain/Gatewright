// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Abstractions;
using Gatewright.Core.Security;
using Gatewright.Policies.Internal;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace Gatewright.Policies.ApiKey;

/// <summary>
/// The <c>apiKey</c> policy: reads a key from a header (or query parameter), hashes it, resolves the subscription from the local store and
/// exposes it in <see cref="GatewayContext.Subscription"/>.
/// </summary>
public sealed class ApiKeyPolicy : PolicyBase<ApiKeyConfig>
{
    private const string Schema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "header": { "type": "string", "minLength": 1 },
            "queryParameter": { "type": "string", "minLength": 1 },
            "required": { "type": "boolean" },
            "products": { "type": "array", "items": { "type": "string", "minLength": 1 } },
            "stripFromRequest": { "type": "boolean" }
          }
        }
        """;

    /// <summary>Initializes a new instance of the <see cref="ApiKeyPolicy"/> class.</summary>
    public ApiKeyPolicy()
        : base("apiKey", PolicyPhase.Inbound, Schema)
    {
    }

    /// <inheritdoc/>
    protected override IPolicyInstance Create(ApiKeyConfig config, PolicyCompilationContext context) => new Instance(
        config,
        context.Services.GetRequiredService<ApiKeyHasher>(),
        context.Services.GetRequiredService<ISubscriptionStore>(),
        new HashSet<string>(config.Products, StringComparer.Ordinal));

    private sealed class Instance(ApiKeyConfig config, ApiKeyHasher hasher, ISubscriptionStore store, HashSet<string> products) : IPolicyInstance
    {
        public async ValueTask<PolicyResult> InvokeAsync(GatewayContext context)
        {
            var request = context.HttpContext.Request;
            string? key = request.Headers[config.Header].FirstOrDefault();
            var fromQuery = false;
            if (string.IsNullOrEmpty(key) && config.QueryParameter is not null)
            {
                key = request.Query[config.QueryParameter].FirstOrDefault();
                fromQuery = !string.IsNullOrEmpty(key);
            }

            if (string.IsNullOrEmpty(key))
            {
                return config.Required ? PolicyResult.Reject(401, "api_key_missing", "An API key is required.") : PolicyResult.Continue;
            }

            var subscription = await store.FindByKeyHashAsync(hasher.Hash(key)).ConfigureAwait(false);
            if (subscription is null)
            {
                return PolicyResult.Reject(401, "api_key_invalid", "The API key is not valid.");
            }

            if (!subscription.IsActive)
            {
                return PolicyResult.Reject(403, "subscription_inactive", "The subscription is not active.");
            }

            if (products.Count > 0 && !products.Contains(subscription.ProductId))
            {
                return PolicyResult.Reject(403, "product_not_subscribed", "The subscription does not give access to this API.");
            }

            context.Subscription = subscription;
            if (config.StripFromRequest)
            {
                if (fromQuery)
                {
                    var remaining = QueryHelpers.ParseQuery(request.QueryString.Value)
                        .Where(p => !string.Equals(p.Key, config.QueryParameter, StringComparison.OrdinalIgnoreCase))
                        .SelectMany(p => p.Value.Select(v => KeyValuePair.Create(p.Key, v)));
                    request.QueryString = new QueryBuilder(remaining!).ToQueryString();
                }
                else
                {
                    request.Headers.Remove(config.Header);
                }
            }

            return PolicyResult.Continue;
        }
    }
}

// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Text;
using System.Text.Json;
using Gatewright.Abstractions;
using Gatewright.Policies.Internal;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection;

namespace Gatewright.Policies.Cache;

/// <summary>
/// The <c>cache</c> policy: caches successful <c>GET</c> responses per operation in the ASP.NET Core output cache store, varying by
/// query and headers, with tag-based invalidation. Responses are never shared between authenticated users unless explicitly allowed.
/// </summary>
public sealed class CachePolicy : PolicyBase<CacheConfig>
{
    private const string Schema = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["durationSeconds"],
          "properties": {
            "durationSeconds": { "type": "integer", "minimum": 1 },
            "varyByQuery": { "type": "array", "items": { "type": "string", "minLength": 1 } },
            "varyByHeader": { "type": "array", "items": { "type": "string", "minLength": 1 } },
            "tags": { "type": "array", "items": { "type": "string", "minLength": 1 } },
            "allowAuthenticated": { "type": "boolean" },
            "maxBodyBytes": { "type": "integer", "minimum": 1 }
          }
        }
        """;

    private static readonly HashSet<string> SkippedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Transfer-Encoding", "Connection", "Keep-Alive", "Content-Length", "Content-Type", "Set-Cookie", "Date",
    };

    /// <summary>Initializes a new instance of the <see cref="CachePolicy"/> class.</summary>
    public CachePolicy()
        : base("cache", PolicyPhase.Inbound, Schema)
    {
    }

    /// <inheritdoc/>
    protected override IPolicyInstance Create(CacheConfig config, PolicyCompilationContext context) =>
        new Instance(config, context.Services.GetRequiredService<IOutputCacheStore>());

    private sealed class Instance(CacheConfig config, IOutputCacheStore store) : IPolicyInstance
    {
        private readonly TimeSpan _duration = TimeSpan.FromSeconds(config.DurationSeconds);
        private readonly bool _allQuery = config.VaryByQuery.Contains("*");
        private readonly string[] _queryNames = [.. config.VaryByQuery.Where(q => q != "*").Order(StringComparer.OrdinalIgnoreCase)];
        private readonly string[] _headerNames = [.. config.VaryByHeader.Order(StringComparer.OrdinalIgnoreCase)];

        public async ValueTask<PolicyResult> InvokeAsync(GatewayContext context)
        {
            var http = context.HttpContext;
            if (!HttpMethods.IsGet(http.Request.Method) || IsAuthenticated(context))
            {
                return PolicyResult.Continue;
            }

            var key = BuildKey(context);
            var bytes = await store.GetAsync(key, context.CancellationToken).ConfigureAwait(false);
            if (bytes is not null && JsonSerializer.Deserialize<CacheEntry>(bytes) is { } entry)
            {
                var headers = new Dictionary<string, string>(entry.Headers, StringComparer.OrdinalIgnoreCase) { ["X-Cache"] = "HIT" };
                headers.Remove("Content-Type", out var contentType);
                return PolicyResult.Respond(new PolicyResponse(entry.Status, headers, entry.Body, contentType));
            }

            http.Response.Headers["X-Cache"] = "MISS";
            var tags = BuildTags(context);
            http.Features.Set(new ResponseCaptureFeature(
                config.MaxBodyBytes,
                capture => StoreAsync(key, tags, capture, context.CancellationToken)));
            return PolicyResult.Continue;
        }

        private bool IsAuthenticated(GatewayContext context) =>
            !config.AllowAuthenticated
            && (context.Identity is not null || context.HttpContext.Request.Headers.ContainsKey("Authorization"));

        private async ValueTask StoreAsync(string key, string[] tags, ResponseCapture capture, CancellationToken cancellationToken)
        {
            if (capture.StatusCode != StatusCodes.Status200OK || capture.Headers.ContainsKey("Set-Cookie"))
            {
                return;
            }

            if (capture.Headers.TryGetValue("Cache-Control", out var cacheControl)
                && (cacheControl.Contains("no-store", StringComparison.OrdinalIgnoreCase) || cacheControl.Contains("private", StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            var entry = new CacheEntry { Status = capture.StatusCode, Body = capture.Body.ToArray() };
            foreach (var (name, value) in capture.Headers)
            {
                if (!SkippedHeaders.Contains(name) || name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    entry.Headers[name] = value;
                }
            }

            await store.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(entry), tags, _duration, cancellationToken).ConfigureAwait(false);
        }

        private string[] BuildTags(GatewayContext context)
        {
            var tags = new List<string>(config.Tags.Count + 2) { "api:" + context.Api.Name };
            if (context.Operation is not null)
            {
                tags.Add($"operation:{context.Api.Name}/{context.Operation.Name}");
            }

            tags.AddRange(config.Tags);
            return [.. tags];
        }

        private string BuildKey(GatewayContext context)
        {
            var request = context.HttpContext.Request;
            var builder = new StringBuilder("gw:")
                .Append(context.RouteId).Append('|')
                .Append(request.Path.Value).Append('?');

            if (_allQuery)
            {
                foreach (var pair in request.Query.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
                {
                    builder.Append(pair.Key.ToLowerInvariant()).Append('=').Append(pair.Value.ToString()).Append('&');
                }
            }
            else
            {
                foreach (var name in _queryNames)
                {
                    builder.Append(name.ToLowerInvariant()).Append('=').Append(request.Query[name].ToString()).Append('&');
                }
            }

            builder.Append('|');
            foreach (var name in _headerNames)
            {
                builder.Append(name.ToLowerInvariant()).Append('=').Append(request.Headers[name].ToString()).Append('&');
            }

            return builder.ToString();
        }
    }
}

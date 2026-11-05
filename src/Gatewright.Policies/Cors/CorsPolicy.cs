// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Abstractions;
using Gatewright.Policies.Internal;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Gatewright.Policies.Cors;

/// <summary>
/// The <c>cors</c> policy. The gateway answers preflight requests itself (Inbound) and adds CORS headers to
/// responses (Outbound); declare it in both phases.
/// </summary>
public sealed class CorsPolicy : PolicyBase<CorsConfig>
{
    private const string Schema = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["origins"],
          "properties": {
            "origins": { "type": "array", "minItems": 1, "items": { "type": "string", "minLength": 1 } },
            "methods": { "type": "array", "items": { "type": "string" } },
            "headers": { "type": "array", "items": { "type": "string" } },
            "exposeHeaders": { "type": "array", "items": { "type": "string" } },
            "credentials": { "type": "boolean" },
            "maxAgeSeconds": { "type": "integer", "minimum": 0 }
          }
        }
        """;

    /// <summary>Initializes a new instance of the <see cref="CorsPolicy"/> class.</summary>
    public CorsPolicy()
        : base("cors", PolicyPhase.Inbound | PolicyPhase.Outbound, Schema)
    {
    }

    /// <inheritdoc/>
    protected override IPolicyInstance Create(CorsConfig config, PolicyCompilationContext context)
    {
        var anyOrigin = config.Origins.Contains("*", StringComparer.Ordinal);
        if (anyOrigin && config.Credentials)
        {
            throw new ArgumentException("Credentials cannot be allowed with the '*' origin; list explicit origins.");
        }

        return new Instance(
            anyOrigin,
            new HashSet<string>(config.Origins, StringComparer.OrdinalIgnoreCase),
            string.Join(", ", config.Methods),
            config.Headers.Contains("*", StringComparer.Ordinal) ? null : string.Join(", ", config.Headers),
            string.Join(", ", config.ExposeHeaders),
            config.Credentials,
            config.MaxAgeSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private sealed class Instance(
        bool anyOrigin,
        HashSet<string> origins,
        string methods,
        string? headers,
        string exposeHeaders,
        bool credentials,
        string maxAge) : IPolicyInstance
    {
        public ValueTask<PolicyResult> InvokeAsync(GatewayContext context)
        {
            var request = context.HttpContext.Request;
            if (!request.Headers.TryGetValue("Origin", out StringValues originValues) || originValues.Count == 0)
            {
                return new ValueTask<PolicyResult>(PolicyResult.Continue);
            }

            var origin = originValues.ToString();
            var allowed = anyOrigin || origins.Contains(origin);
            var allowOrigin = anyOrigin ? "*" : origin;

            if (context.Phase == PolicyPhase.Inbound)
            {
                if (HttpMethods.IsOptions(request.Method) && request.Headers.ContainsKey("Access-Control-Request-Method"))
                {
                    return new ValueTask<PolicyResult>(allowed ? Preflight(context, allowOrigin) : PolicyResult.Reject(403, "cors_origin_not_allowed", "Origin is not allowed."));
                }

                return new ValueTask<PolicyResult>(PolicyResult.Continue);
            }

            if (allowed)
            {
                var response = context.HttpContext.Response.Headers;
                response["Access-Control-Allow-Origin"] = allowOrigin;
                if (!anyOrigin)
                {
                    response.Append("Vary", "Origin");
                }

                if (credentials)
                {
                    response["Access-Control-Allow-Credentials"] = "true";
                }

                if (exposeHeaders.Length > 0)
                {
                    response["Access-Control-Expose-Headers"] = exposeHeaders;
                }
            }

            return new ValueTask<PolicyResult>(PolicyResult.Continue);
        }

        private PolicyResult Preflight(GatewayContext context, string allowOrigin)
        {
            var result = new Dictionary<string, string>
            {
                ["Access-Control-Allow-Origin"] = allowOrigin,
                ["Access-Control-Allow-Methods"] = methods,
                ["Access-Control-Max-Age"] = maxAge,
            };
            var allowHeaders = headers ?? context.HttpContext.Request.Headers["Access-Control-Request-Headers"].ToString();
            if (allowHeaders.Length > 0)
            {
                result["Access-Control-Allow-Headers"] = allowHeaders;
            }

            if (!anyOrigin)
            {
                result["Vary"] = "Origin";
            }

            if (credentials)
            {
                result["Access-Control-Allow-Credentials"] = "true";
            }

            return PolicyResult.Respond(new PolicyResponse(204, result));
        }
    }
}

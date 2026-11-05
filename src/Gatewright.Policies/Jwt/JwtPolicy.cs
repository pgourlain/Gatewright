// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Security.Claims;
using System.Text.Json;
using Gatewright.Abstractions;
using Gatewright.Policies.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Gatewright.Policies.Jwt;

/// <summary>
/// The <c>jwt</c> policy: validates bearer tokens (issuer, audience, lifetime, signature) against cached JWKS that a background
/// service keeps fresh, enforces required claims and scopes, and exposes the identity in <see cref="GatewayContext.Identity"/>.
/// </summary>
public sealed class JwtPolicy : PolicyBase<JwtConfig>
{
    private const string Schema = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["issuers"],
          "properties": {
            "required": { "type": "boolean" },
            "issuers": {
              "type": "array",
              "minItems": 1,
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["issuer", "audiences"],
                "oneOf": [ { "required": ["jwksUri"] }, { "required": ["metadataAddress"] } ],
                "properties": {
                  "issuer": { "type": "string", "minLength": 1 },
                  "audiences": { "type": "array", "minItems": 1, "items": { "type": "string", "minLength": 1 } },
                  "jwksUri": { "type": "string", "format": "uri" },
                  "metadataAddress": { "type": "string", "format": "uri" },
                  "refreshIntervalSeconds": { "type": "integer", "minimum": 1 },
                  "clockSkewSeconds": { "type": "integer", "minimum": 0 },
                  "requiredScopes": { "type": "array", "items": { "type": "string", "minLength": 1 } },
                  "requiredClaims": {
                    "type": "object",
                    "additionalProperties": {
                      "oneOf": [
                        { "type": "string" },
                        { "type": "array", "minItems": 1, "items": { "type": "string" } }
                      ]
                    }
                  }
                }
              }
            }
          }
        }
        """;

    private static readonly string[] AllowedAlgorithms =
    [
        SecurityAlgorithms.RsaSha256, SecurityAlgorithms.RsaSha384, SecurityAlgorithms.RsaSha512,
        SecurityAlgorithms.RsaSsaPssSha256, SecurityAlgorithms.RsaSsaPssSha384, SecurityAlgorithms.RsaSsaPssSha512,
        SecurityAlgorithms.EcdsaSha256, SecurityAlgorithms.EcdsaSha384, SecurityAlgorithms.EcdsaSha512,
    ];

    /// <summary>Initializes a new instance of the <see cref="JwtPolicy"/> class.</summary>
    public JwtPolicy()
        : base("jwt", PolicyPhase.Inbound, Schema)
    {
    }

    /// <inheritdoc/>
    protected override IPolicyInstance Create(JwtConfig config, PolicyCompilationContext context)
    {
        var store = context.Services.GetRequiredService<JwksStore>();
        var issuers = new Dictionary<string, IssuerValidator>(StringComparer.Ordinal);
        foreach (var issuer in config.Issuers)
        {
            if (!issuers.TryAdd(issuer.Issuer, IssuerValidator.Create(issuer, store)))
            {
                throw new ArgumentException($"Issuer '{issuer.Issuer}' is declared twice.");
            }
        }

        return new Instance(config.Required, issuers);
    }

    private sealed class IssuerValidator
    {
        private readonly JwksHandle _handle;
        private readonly TokenValidationParameters _parameters;
        private readonly string[] _requiredScopes;
        private readonly (string Name, string[] Values)[] _requiredClaims;

        private IssuerValidator(JwksHandle handle, TokenValidationParameters parameters, string[] scopes, (string, string[])[] claims)
        {
            _handle = handle;
            _parameters = parameters;
            _requiredScopes = scopes;
            _requiredClaims = claims;
        }

        public static IssuerValidator Create(JwtIssuerConfig config, JwksStore store)
        {
            var handle = store.Register(
                config.JwksUri is null ? null : new Uri(config.JwksUri, UriKind.Absolute),
                config.MetadataAddress is null ? null : new Uri(config.MetadataAddress, UriKind.Absolute),
                TimeSpan.FromSeconds(config.RefreshIntervalSeconds));

            var parameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = config.Issuer,
                ValidateAudience = true,
                ValidAudiences = config.Audiences,
                ValidateLifetime = true,
                RequireExpirationTime = true,
                ClockSkew = TimeSpan.FromSeconds(config.ClockSkewSeconds),
                ValidateIssuerSigningKey = true,
                RequireSignedTokens = true,
                ValidAlgorithms = AllowedAlgorithms,
                IssuerSigningKeyResolver = (_, _, kid, _) => SelectKeys(handle, kid),
            };

            var claims = config.RequiredClaims
                .Select(c => (c.Key, c.Value.ValueKind == JsonValueKind.Array
                    ? c.Value.EnumerateArray().Select(v => v.GetString()!).ToArray()
                    : new[] { c.Value.GetString()! }))
                .ToArray();
            return new IssuerValidator(handle, parameters, [.. config.RequiredScopes], claims);
        }

        public async ValueTask<PolicyResult> ValidateAsync(GatewayContext context, string token, JsonWebToken parsed)
        {
            if (!_handle.IsLoaded)
            {
                return PolicyResult.Reject(503, "jwks_unavailable", "Signing keys are not loaded yet.");
            }

            var result = await JwtHandler.ValidateTokenAsync(parsed, _parameters).ConfigureAwait(false);
            if (!result.IsValid)
            {
                if (result.Exception is SecurityTokenSignatureKeyNotFoundException or SecurityTokenInvalidSignatureException)
                {
                    _handle.RequestRefresh();
                }

                return Unauthorized(context, "invalid_token", "Token validation failed.");
            }

            var principal = new ClaimsPrincipal(result.ClaimsIdentity);
            if (!HasScopes(principal))
            {
                context.HttpContext.Response.Headers.WWWAuthenticate = "Bearer error=\"insufficient_scope\"";
                return PolicyResult.Reject(403, "insufficient_scope", "Token lacks a required scope.");
            }

            foreach (var (name, values) in _requiredClaims)
            {
                if (!principal.Claims.Any(c => c.Type == name && Array.IndexOf(values, c.Value) >= 0))
                {
                    return PolicyResult.Reject(403, "claim_mismatch", $"Required claim '{name}' is missing or has an unexpected value.");
                }
            }

            context.Identity = principal;
            return PolicyResult.Continue;
        }

        private static IEnumerable<SecurityKey> SelectKeys(JwksHandle handle, string? kid)
        {
            var keys = handle.Keys;
            if (kid is null)
            {
                return keys;
            }

            var matches = keys.Where(k => k.KeyId == kid).ToArray();
            if (matches.Length == 0)
            {
                handle.RequestRefresh();
            }

            return matches;
        }

        private bool HasScopes(ClaimsPrincipal principal)
        {
            if (_requiredScopes.Length == 0)
            {
                return true;
            }

            var granted = principal.Claims
                .Where(c => c.Type is "scp" or "scope")
                .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .ToHashSet(StringComparer.Ordinal);
            return _requiredScopes.All(granted.Contains);
        }
    }

    private static class JwtHandler
    {
        private static readonly JsonWebTokenHandler Handler = new();

        public static Task<TokenValidationResult> ValidateTokenAsync(JsonWebToken token, TokenValidationParameters parameters) =>
            Handler.ValidateTokenAsync(token, parameters);
    }

    private sealed class Instance(bool required, Dictionary<string, IssuerValidator> issuers) : IPolicyInstance
    {
        public async ValueTask<PolicyResult> InvokeAsync(GatewayContext context)
        {
            var header = context.HttpContext.Request.Headers.Authorization.ToString();
            const string prefix = "Bearer ";
            if (header.Length == 0)
            {
                return required ? Unauthorized(context, "invalid_request", "Missing bearer token.") : PolicyResult.Continue;
            }

            if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return Unauthorized(context, "invalid_request", "Authorization scheme must be Bearer.");
            }

            var token = header[prefix.Length..].Trim();
            JsonWebToken parsed;
            try
            {
                parsed = new JsonWebToken(token);
            }
            catch (ArgumentException)
            {
                return Unauthorized(context, "invalid_token", "Token is malformed.");
            }

            if (!issuers.TryGetValue(parsed.Issuer ?? "", out var validator))
            {
                return Unauthorized(context, "invalid_token", "Token issuer is not trusted.");
            }

            return await validator.ValidateAsync(context, token, parsed).ConfigureAwait(false);
        }
    }

    private static PolicyResult Unauthorized(GatewayContext context, string error, string message)
    {
        context.HttpContext.Response.Headers.WWWAuthenticate = $"Bearer error=\"{error}\"";
        return PolicyResult.Reject(401, error, message);
    }
}

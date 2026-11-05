// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Net;
using Gatewright.Abstractions;
using Gatewright.Policies.Internal;

namespace Gatewright.Policies.IpFilter;

/// <summary>The <c>ipFilter</c> policy: allow/deny lists by IP or CIDR, honoring <c>X-Forwarded-For</c> only behind trusted proxies.</summary>
public sealed class IpFilterPolicy : PolicyBase<IpFilterConfig>
{
    private const string Schema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "allow": { "type": "array", "items": { "type": "string", "minLength": 1 } },
            "deny": { "type": "array", "items": { "type": "string", "minLength": 1 } },
            "trustedProxies": { "type": "array", "items": { "type": "string", "minLength": 1 } }
          }
        }
        """;

    /// <summary>Initializes a new instance of the <see cref="IpFilterPolicy"/> class.</summary>
    public IpFilterPolicy()
        : base("ipFilter", PolicyPhase.Inbound, Schema)
    {
    }

    /// <inheritdoc/>
    protected override IPolicyInstance Create(IpFilterConfig config, PolicyCompilationContext context) =>
        new Instance(new IpNetworkSet(config.Allow), new IpNetworkSet(config.Deny), new IpNetworkSet(config.TrustedProxies));

    private sealed class Instance(IpNetworkSet allow, IpNetworkSet deny, IpNetworkSet trusted) : IPolicyInstance
    {
        public ValueTask<PolicyResult> InvokeAsync(GatewayContext context)
        {
            var client = ResolveClient(context.HttpContext);
            if (client is null)
            {
                return new ValueTask<PolicyResult>(allow.IsEmpty ? PolicyResult.Continue : Forbidden());
            }

            if (deny.Contains(client) || (!allow.IsEmpty && !allow.Contains(client)))
            {
                return new ValueTask<PolicyResult>(Forbidden());
            }

            return new ValueTask<PolicyResult>(PolicyResult.Continue);
        }

        private static PolicyResult Forbidden() => PolicyResult.Reject(403, "ip_forbidden", "Client address is not allowed.");

        private IPAddress? ResolveClient(Microsoft.AspNetCore.Http.HttpContext http)
        {
            var peer = http.Connection.RemoteIpAddress;
            if (peer is null || trusted.IsEmpty || !trusted.Contains(peer))
            {
                return peer;
            }

            // Walk X-Forwarded-For from the right, skipping trusted proxies: the first other address is the client.
            var forwarded = http.Request.Headers["X-Forwarded-For"];
            for (var i = forwarded.Count - 1; i >= 0; i--)
            {
                var parts = forwarded[i]!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                for (var j = parts.Length - 1; j >= 0; j--)
                {
                    if (!IPAddress.TryParse(parts[j], out var hop))
                    {
                        return peer;
                    }

                    if (!trusted.Contains(hop))
                    {
                        return hop;
                    }
                }
            }

            return peer;
        }
    }
}

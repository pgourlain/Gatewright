// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Gatewright.ControlPlane.AdminApi;

/// <summary>Resolves the name of the authenticated caller for revisions and audit entries.</summary>
public sealed class ActorAccessor
{
    private readonly IHttpContextAccessor _http;
    private readonly AdminAuthOptions _options;

    /// <summary>Initializes a new instance of the <see cref="ActorAccessor"/> class.</summary>
    /// <param name="http">The HTTP context accessor.</param>
    /// <param name="options">The authentication options.</param>
    public ActorAccessor(IHttpContextAccessor http, IOptions<AdminAuthOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        _http = http;
        _options = options.Value;
    }

    /// <summary>Gets or sets the actor used when there is no HTTP caller (background jobs such as the automatic OpenAPI import).</summary>
    public string? Override { get; set; }

    /// <summary>Gets the caller name: the configured actor claim, else the object id, else the identity name, else <c>unknown</c>.</summary>
    public string Current
    {
        get
        {
            if (Override is not null)
            {
                return Override;
            }

            var user = _http.HttpContext?.User;
            return user?.FindFirst(_options.ActorClaim)?.Value
                ?? user?.FindFirst("oid")?.Value
                ?? user?.Identity?.Name
                ?? "unknown";
        }
    }
}

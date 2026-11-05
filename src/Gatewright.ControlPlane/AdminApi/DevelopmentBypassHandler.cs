// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gatewright.ControlPlane.AdminApi;

/// <summary>Authenticates every request as <c>dev-user</c> with all roles. Registered only when <see cref="AdminAuthOptions.DevelopmentBypass"/> is set in a Development environment.</summary>
internal sealed class DevelopmentBypassHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "GatewrightDevelopmentBypass";

    private readonly AdminAuthOptions _auth;

    public DevelopmentBypassHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptions<AdminAuthOptions> auth)
        : base(options, logger, encoder)
    {
        _auth = auth.Value;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(_auth.ActorClaim, "dev-user"),
                new Claim(_auth.RoleClaim, AdminRoles.Admin),
            ],
            SchemeName,
            _auth.ActorClaim,
            _auth.RoleClaim);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}

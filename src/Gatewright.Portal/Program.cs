// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Security.Claims;
using Gatewright.Portal.Components;
using Gatewright.Portal.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<PortalOptions>(builder.Configuration.GetSection(PortalOptions.SectionName));
var portal = builder.Configuration.GetSection(PortalOptions.SectionName).Get<PortalOptions>() ?? new PortalOptions();
var development = string.Equals(portal.Auth.Mode, "Development", StringComparison.OrdinalIgnoreCase);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<TokenProvider>();
builder.Services.AddHttpClient<IPortalApi, PortalApi>();
builder.Services.AddHttpClient<IGatewayConsole, GatewayConsole>();
builder.Services.AddHttpContextAccessor();

if (development)
{
    builder.Services.AddAuthentication("Development").AddScheme<AuthenticationSchemeOptions, DevelopmentAuthHandler>("Development", null);
}
else
{
    if (string.IsNullOrWhiteSpace(portal.Auth.Authority) || string.IsNullOrWhiteSpace(portal.Auth.ClientId))
    {
        throw new InvalidOperationException("Portal:Auth:Authority and Portal:Auth:ClientId are required in Oidc mode. Configure them, or set Portal:Auth:Mode=Development for a non-production try-out.");
    }

    builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie()
        .AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
        {
            options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.Authority = portal.Auth.Authority;
            options.ClientId = portal.Auth.ClientId;
            options.ClientSecret = portal.Auth.ClientSecret;
            options.ResponseType = OpenIdConnectResponseType.Code;
            options.UsePkce = true;
            options.SaveTokens = true;
            options.MapInboundClaims = false;
            options.GetClaimsFromUserInfoEndpoint = false;
            options.Scope.Clear();
            options.Scope.Add("openid");
            options.Scope.Add("profile");
            if (!string.IsNullOrEmpty(portal.Auth.Scope))
            {
                options.Scope.Add(portal.Auth.Scope);
            }

            options.TokenValidationParameters.NameClaimType = "preferred_username";
            options.TokenValidationParameters.RoleClaimType = portal.Auth.RoleClaim;
        });
}

builder.Services.AddAuthorization();

var app = builder.Build();
if (development)
{
    LoggerMessage.Define(LogLevel.Warning, new EventId(1, "DevelopmentAuth"), "Portal:Auth:Mode=Development: sign-in is disabled and every visitor is an administrator. Never use this in production.")(app.Logger, null);
}

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapGet("/healthz", () => "ok").AllowAnonymous();
if (!development)
{
    app.MapGet("/signin", () => Results.Challenge(new AuthenticationProperties { RedirectUri = "/" }, [OpenIdConnectDefaults.AuthenticationScheme])).AllowAnonymous();
    app.MapGet("/signout", () => Results.SignOut(new AuthenticationProperties { RedirectUri = "/" }, [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme])).AllowAnonymous();
}

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();

/// <summary>Entry point marker, exposed for tests.</summary>
public partial class Program;

internal sealed class DevelopmentAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public DevelopmentAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, System.Text.Encodings.Web.UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = new ClaimsIdentity([new Claim("preferred_username", "dev-user"), new Claim("roles", "Admin")], "Development", "preferred_username", "roles");
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "Development")));
    }
}

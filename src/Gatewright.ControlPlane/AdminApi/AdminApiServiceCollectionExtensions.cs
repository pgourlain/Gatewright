// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Text.Json.Serialization;
using Gatewright.ControlPlane.Revisions;
using Gatewright.Core.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Gatewright.ControlPlane.AdminApi;

/// <summary>Registers the Admin API: services, authentication (Entra ID or any OIDC provider), role authorization and OpenAPI.</summary>
public static class AdminApiServiceCollectionExtensions
{
    /// <summary>Registers the Admin API.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration; <c>ControlPlane:Auth</c> is bound to <see cref="AdminAuthOptions"/>.</param>
    /// <param name="environment">The host environment; the development bypass is refused outside Development.</param>
    /// <returns>The service collection.</returns>
    /// <exception cref="InvalidOperationException">The development bypass is enabled outside the Development environment.</exception>
    public static IServiceCollection AddAdminApi(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var auth = new AdminAuthOptions();
        configuration.GetSection(AdminAuthOptions.SectionName).Bind(auth);
        if (auth.DevelopmentBypass && !environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "ControlPlane:Auth:DevelopmentBypass accepts every caller as an administrator and is only allowed when ASPNETCORE_ENVIRONMENT is Development.");
        }

        services.Configure<AdminAuthOptions>(configuration.GetSection(AdminAuthOptions.SectionName));
        services.AddHttpContextAccessor();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(new ApiKeyHasher(null));
        services.AddScoped<ConfigExporter>();
        services.AddScoped<RevisionService>();
        services.AddScoped<ConfigPorter>();
        services.AddScoped<ApiImportService>();
        services.AddScoped<ActorAccessor>();
        services.AddScoped<ChangeRecorder>();
        services.TryAddSingleton<IRevocationFeed, InMemoryRevocationFeed>();
        services.AddScoped<RevocationPublisher>();
        services.AddOpenApi();
        services.Configure<JsonOptions>(o =>
        {
            o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        var authentication = services.AddAuthentication(auth.DevelopmentBypass ? DevelopmentBypassHandler.SchemeName : JwtBearerDefaults.AuthenticationScheme);
        if (auth.DevelopmentBypass)
        {
            authentication.AddScheme<AuthenticationSchemeOptions, DevelopmentBypassHandler>(DevelopmentBypassHandler.SchemeName, null);
        }
        else
        {
            authentication.AddJwtBearer();
            services.AddSingleton<IConfigureOptions<JwtBearerOptions>, ConfigureJwtBearer>();
        }

        services.AddAuthorizationBuilder()
            .AddPolicy(AdminRoles.ReaderPolicy, p => p.RequireRole(AdminRoles.Reader, AdminRoles.Publisher, AdminRoles.Admin))
            .AddPolicy(AdminRoles.PublisherPolicy, p => p.RequireRole(AdminRoles.Publisher, AdminRoles.Admin))
            .AddPolicy(AdminRoles.AdminPolicy, p => p.RequireRole(AdminRoles.Admin));
        return services;
    }

    private sealed class ConfigureJwtBearer(IOptions<AdminAuthOptions> auth) : IConfigureNamedOptions<JwtBearerOptions>
    {
        public void Configure(JwtBearerOptions options) => Configure(JwtBearerDefaults.AuthenticationScheme, options);

        public void Configure(string? name, JwtBearerOptions options)
        {
            if (name != JwtBearerDefaults.AuthenticationScheme)
            {
                return;
            }

            var settings = auth.Value;
            options.Authority = settings.Authority;
            options.Audience = settings.Audience;
            options.MapInboundClaims = false;
            options.TokenValidationParameters.NameClaimType = settings.ActorClaim;
            options.TokenValidationParameters.RoleClaimType = settings.RoleClaim;
        }
    }
}

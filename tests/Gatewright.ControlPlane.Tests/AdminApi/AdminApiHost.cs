// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Gatewright.Abstractions;
using Gatewright.ControlPlane.AdminApi;
using Gatewright.ControlPlane.Data;
using Gatewright.Core;
using Gatewright.Core.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Gatewright.ControlPlane.Tests.AdminApi;

/// <summary>A policy type accepting any configuration, so tests can exercise validation of policy references.</summary>
internal sealed class AcceptAnythingPolicy(string type) : IGatewayPolicy
{
    public string Type => type;

    public PolicyPhase Phases => PolicyPhase.Inbound | PolicyPhase.Backend | PolicyPhase.Outbound;

    public JsonElement ConfigSchema { get; } = JsonElement.Parse("{}");

    public IPolicyInstance Compile(JsonElement config, PolicyCompilationContext context) => new NoOp();

    private sealed class NoOp : IPolicyInstance
    {
        public ValueTask<PolicyResult> InvokeAsync(GatewayContext context) => new(PolicyResult.Continue);
    }
}

/// <summary>An in-memory Admin API (TestServer + SQLite) accepting JWTs signed with a test key.</summary>
internal sealed class AdminApiHost : IAsyncDisposable
{
    public const string Audience = "api://gatewright-admin";
    public const string Issuer = "https://test-issuer.example";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly WebApplication _app;
    private readonly SqliteConnection _connection;
    private readonly RsaSecurityKey _key;

    private AdminApiHost(WebApplication app, SqliteConnection connection, FakeTimeProvider time, RsaSecurityKey key)
    {
        _app = app;
        _connection = connection;
        _key = key;
        Time = time;
        Client = app.GetTestClient();
    }

    public HttpClient Client { get; }

    public FakeTimeProvider Time { get; }

    public IServiceProvider Services => _app.Services;

    public static async Task<AdminApiHost> StartAsync(
        Dictionary<string, string?>? settings = null,
        string environment = "Production",
        Action<IServiceCollection>? configure = null)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-02-01T09:00:00Z", System.Globalization.CultureInfo.InvariantCulture));

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ControlPlane:Auth:Audience"] = Audience,
        });
        if (settings is not null)
        {
            builder.Configuration.AddInMemoryCollection(settings);
        }

        builder.Services.AddSingleton<TimeProvider>(time);
        builder.Services.AddDbContext<GatewrightDbContext>(o => o.UseSqlite(connection));
        builder.Services.AddSingleton<IGatewayPolicy>(new AcceptAnythingPolicy("rateLimit"));
        builder.Services.AddGatewrightCore();
        builder.Services.AddSingleton(new ApiKeyHasher("test-pepper"));
        builder.Services.AddAdminApi(builder.Configuration, builder.Environment);
        configure?.Invoke(builder.Services);

        var key = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "test" };
        builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.Authority = null;
            options.RequireHttpsMetadata = false;
            options.Configuration = new OpenIdConnectConfiguration();
            options.TokenValidationParameters.ValidIssuer = Issuer;
            options.TokenValidationParameters.ValidAudience = Audience;
            options.TokenValidationParameters.IssuerSigningKey = key;
        });

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapAdminApi();
        await app.StartAsync();

        using (var scope = app.Services.CreateScope())
        {
            await ControlPlaneDatabase.InitializeAsync(scope.ServiceProvider.GetRequiredService<GatewrightDbContext>(), seedSample: false, CancellationToken.None);
        }

        var result = new AdminApiHost(app, connection, time, key);
        result.Authenticate(AdminRoles.Admin);
        return result;
    }

    public string Token(string[] roles, string actor = "alice@example.com", string audience = Audience, string issuer = Issuer, DateTime? expires = null)
    {
        var claims = new Dictionary<string, object>
        {
            ["preferred_username"] = actor,
            ["roles"] = roles,
            ["sub"] = actor,
        };
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Expires = expires ?? DateTime.UtcNow.AddMinutes(30),
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.RsaSha256),
            Claims = claims,
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    public void Authenticate(params string[] roles) =>
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(roles));

    public Task<HttpResponseMessage> PostAsync<T>(string url, T body) => Client.PostAsJsonAsync(url, body, Json);

    public Task<HttpResponseMessage> PutAsync<T>(string url, T body) => Client.PutAsJsonAsync(url, body, Json);

    public static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        JsonElement.Parse(await response.Content.ReadAsStringAsync());

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.DisposeAsync();
        await _connection.DisposeAsync();
    }
}

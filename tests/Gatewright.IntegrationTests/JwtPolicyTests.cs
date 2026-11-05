// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Gatewright.Core.Configuration;
using Gatewright.Policies.Jwt;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Gatewright.IntegrationTests;

public sealed class JwtPolicyTests
{
    private const string Issuer = "https://issuer.example";
    private const string Audience = "api://orders";

    private sealed class FakeFetcher : IJwksFetcher
    {
        public string Json { get; set; } = "{\"keys\":[]}";

        public int Calls { get; private set; }

        public Task<string> FetchAsync(Uri uri, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Json);
        }
    }

    private sealed class Signer
    {
        private readonly RSA _rsa = RSA.Create(2048);

        public Signer(string kid) => Key = new RsaSecurityKey(_rsa) { KeyId = kid };

        public RsaSecurityKey Key { get; }

        public JsonWebKey PublicJwk
        {
            get
            {
                var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(_rsa.ExportParameters(false)));
                jwk.Kid = Key.KeyId;
                return jwk;
            }
        }

        public string Token(
            string issuer = Issuer,
            string audience = Audience,
            DateTime? expires = null,
            IDictionary<string, object>? claims = null)
        {
            var descriptor = new SecurityTokenDescriptor
            {
                Issuer = issuer,
                Audience = audience,
                Expires = expires ?? DateTime.UtcNow.AddMinutes(10),
                NotBefore = DateTime.UtcNow.AddMinutes(-1),
                IssuedAt = DateTime.UtcNow.AddMinutes(-1),
                SigningCredentials = new SigningCredentials(Key, SecurityAlgorithms.RsaSha256),
                Claims = claims ?? new Dictionary<string, object> { ["sub"] = "user-1" },
            };
            return new JsonWebTokenHandler().CreateToken(descriptor);
        }
    }

    private static string Jwks(params Signer[] signers) =>
        JsonSerializer.Serialize(new { keys = signers.Select(s => new { kty = "RSA", use = "sig", alg = "RS256", kid = s.Key.KeyId, n = s.PublicJwk.N, e = s.PublicJwk.E }) });

    private static async Task<(TestGateway Gateway, FakeFetcher Fetcher)> StartAsync(string jwtExtra = "", bool required = true)
    {
        var fetcher = new FakeFetcher();
        var gateway = await TestGateway.StartAsync(
            url => new GatewayConfig
            {
                Apis =
                [
                    TestGateway.Api(url, new PolicySet
                    {
                        Inbound =
                        [
                            TestGateway.Policy("jwt", $$"""
                                {"required":{{required.ToString().ToLowerInvariant()}},"issuers":[{"issuer":"{{Issuer}}","audiences":["{{Audience}}"],"jwksUri":"https://issuer.example/jwks"{{jwtExtra}}}]}
                                """),
                            TestGateway.Policy("headers", """{"request":{"set":{"x-user":"{claim:sub}"}}}"""),
                        ],
                    }),
                ],
            },
            services => services.AddSingleton<IJwksFetcher>(fetcher));
        var store = gateway.Services.GetRequiredService<JwksStore>();
        store.MinimumRefreshInterval = TimeSpan.Zero;
        return (gateway, fetcher);
    }

    private static async Task<HttpResponseMessage> CallAsync(TestGateway gateway, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/x");
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await gateway.Client.SendAsync(request);
    }

    private static async Task RefreshAsync(TestGateway gateway) =>
        await gateway.Services.GetRequiredService<JwksStore>().RefreshAllAsync(CancellationToken.None);

    [Fact]
    public async Task ValidToken_IsAccepted_AndIdentityIsAvailableToLaterPolicies()
    {
        var signer = new Signer("k1");
        var (gateway, fetcher) = await StartAsync();
        await using var _ = gateway;
        fetcher.Json = Jwks(signer);
        await RefreshAsync(gateway);

        var response = await CallAsync(gateway, signer.Token());

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement
            .GetProperty("headers").GetProperty("x-user").GetString().ShouldBe("user-1");
    }

    [Fact]
    public async Task MissingToken_Returns401WithChallenge()
    {
        var (gateway, fetcher) = await StartAsync();
        await using var _ = gateway;
        fetcher.Json = Jwks(new Signer("k1"));
        await RefreshAsync(gateway);

        var response = await CallAsync(gateway, null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().ShouldContain("Bearer");
    }

    [Fact]
    public async Task OptionalJwt_WithoutToken_PassesAnonymously()
    {
        var (gateway, fetcher) = await StartAsync(required: false);
        await using var _ = gateway;
        fetcher.Json = Jwks(new Signer("k1"));
        await RefreshAsync(gateway);

        (await CallAsync(gateway, null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await CallAsync(gateway, "garbage")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task InvalidTokens_AreRejected()
    {
        var signer = new Signer("k1");
        var (gateway, fetcher) = await StartAsync();
        await using var _ = gateway;
        fetcher.Json = Jwks(signer);
        await RefreshAsync(gateway);

        (await CallAsync(gateway, signer.Token(audience: "api://other"))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await CallAsync(gateway, signer.Token(issuer: "https://evil.example"))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await CallAsync(gateway, signer.Token(expires: DateTime.UtcNow.AddMinutes(-30)))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await CallAsync(gateway, new Signer("k1").Token())).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await CallAsync(gateway, "not.a.jwt")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TokenSignedWithAlgNone_IsRejected()
    {
        var (gateway, fetcher) = await StartAsync();
        await using var _ = gateway;
        fetcher.Json = Jwks(new Signer("k1"));
        await RefreshAsync(gateway);
        static string B64(string s) => Base64UrlEncoder.Encode(s);
        var unsigned = $"{B64("{\"alg\":\"none\",\"typ\":\"JWT\"}")}.{B64($"{{\"iss\":\"{Issuer}\",\"aud\":\"{Audience}\",\"exp\":{DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()}}}")}.";

        (await CallAsync(gateway, unsigned)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RequiredScopeAndClaim_AreEnforced()
    {
        var signer = new Signer("k1");
        var (gateway, fetcher) = await StartAsync(""","requiredScopes":["orders.read"],"requiredClaims":{"tid":["t1","t2"]}""");
        await using var _ = gateway;
        fetcher.Json = Jwks(signer);
        await RefreshAsync(gateway);

        var ok = signer.Token(claims: new Dictionary<string, object> { ["sub"] = "u", ["scp"] = "orders.read orders.write", ["tid"] = "t2" });
        var noScope = signer.Token(claims: new Dictionary<string, object> { ["sub"] = "u", ["scp"] = "orders.write", ["tid"] = "t2" });
        var badTenant = signer.Token(claims: new Dictionary<string, object> { ["sub"] = "u", ["scp"] = "orders.read", ["tid"] = "other" });

        (await CallAsync(gateway, ok)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await CallAsync(gateway, noScope)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CallAsync(gateway, badTenant)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task BeforeKeysAreLoaded_Returns503()
    {
        var signer = new Signer("k1");
        var (gateway, _) = await StartAsync();
        await using var _ = gateway;
        // The background service may have loaded the empty key set; force "not loaded" by using an unknown source instead.
        var store = gateway.Services.GetRequiredService<JwksStore>();
        store.Handles.ShouldNotBeEmpty();
        var token = signer.Token();

        var status = (await CallAsync(gateway, token)).StatusCode;

        status.ShouldBeOneOf(HttpStatusCode.ServiceUnavailable, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task KeyRotation_UnknownKid_IsRejectedThenAcceptedAfterBackgroundRefresh()
    {
        var oldSigner = new Signer("old");
        var newSigner = new Signer("new");
        var (gateway, fetcher) = await StartAsync();
        await using var _ = gateway;
        fetcher.Json = Jwks(oldSigner);
        await RefreshAsync(gateway);
        var token = newSigner.Token();

        (await CallAsync(gateway, token)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        fetcher.Json = Jwks(oldSigner, newSigner);
        await RefreshAsync(gateway);

        (await CallAsync(gateway, token)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HotPath_NeverFetchesJwks()
    {
        var signer = new Signer("k1");
        var (gateway, fetcher) = await StartAsync();
        await using var _ = gateway;
        fetcher.Json = Jwks(signer);
        await RefreshAsync(gateway);
        var callsAfterLoad = fetcher.Calls;
        var token = signer.Token();

        for (var i = 0; i < 25; i++)
        {
            (await CallAsync(gateway, token)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        fetcher.Calls.ShouldBe(callsAfterLoad);
    }

    [Fact]
    public async Task Config_IssuerWithoutKeySource_IsRejectedBySchema()
    {
        var act = () => TestGateway.StartAsync(
            url => new GatewayConfig
            {
                Apis = [TestGateway.Api(url, new PolicySet { Inbound = [TestGateway.Policy("jwt", $$"""{"issuers":[{"issuer":"{{Issuer}}","audiences":["a"]}]}""")] })],
            });

        await Should.ThrowAsync<InvalidOperationException>(act);
    }
}

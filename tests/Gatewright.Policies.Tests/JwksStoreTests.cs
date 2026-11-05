// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;
using System.Text.Json;
using Gatewright.Policies.Jwt;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;

namespace Gatewright.Policies.Tests;

public sealed class JwksStoreTests
{
    private sealed class Fetcher(Func<Uri, string> respond) : IJwksFetcher
    {
        public List<Uri> Requested { get; } = [];

        public Task<string> FetchAsync(Uri uri, CancellationToken cancellationToken)
        {
            Requested.Add(uri);
            return Task.FromResult(respond(uri));
        }
    }

    private static string Jwks(string kid)
    {
        using var rsa = RSA.Create(2048);
        var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(rsa.ExportParameters(false)));
        return JsonSerializer.Serialize(new { keys = new[] { new { kty = "RSA", use = "sig", kid, n = jwk.N, e = jwk.E } } });
    }

    private static JwksStore Store(Fetcher fetcher) => new(fetcher, TimeProvider.System, NullLogger<JwksStore>.Instance);

    [Fact]
    public async Task Refresh_JwksUri_LoadsSigningKeysWithKid()
    {
        var fetcher = new Fetcher(_ => Jwks("k1"));
        var store = Store(fetcher);
        var handle = store.Register(new Uri("https://issuer/jwks"), null, TimeSpan.FromHours(1));

        await store.RefreshDueAsync(CancellationToken.None);

        handle.IsLoaded.ShouldBeTrue();
        handle.Keys.ShouldHaveSingleItem().KeyId.ShouldBe("k1");
    }

    [Fact]
    public async Task Refresh_MetadataAddress_DiscoversJwksUri()
    {
        var fetcher = new Fetcher(uri => uri.AbsolutePath.EndsWith("openid-configuration", StringComparison.Ordinal)
            ? """{"jwks_uri":"https://issuer/keys"}"""
            : Jwks("k2"));
        var store = Store(fetcher);
        var handle = store.Register(null, new Uri("https://issuer/.well-known/openid-configuration"), TimeSpan.FromHours(1));

        await store.RefreshDueAsync(CancellationToken.None);

        handle.Keys.ShouldHaveSingleItem().KeyId.ShouldBe("k2");
        fetcher.Requested.Select(u => u.AbsolutePath).ShouldBe(["/.well-known/openid-configuration", "/keys"]);
    }

    [Fact]
    public async Task Refresh_FetchFails_KeepsPreviousKeys()
    {
        var fail = false;
        var fetcher = new Fetcher(_ => fail ? throw new HttpRequestException("down") : Jwks("k1"));
        var store = Store(fetcher);
        var handle = store.Register(new Uri("https://issuer/jwks"), null, TimeSpan.Zero);
        await store.RefreshDueAsync(CancellationToken.None);

        fail = true;
        await store.RefreshDueAsync(CancellationToken.None);

        handle.Keys.ShouldHaveSingleItem().KeyId.ShouldBe("k1");
    }

    [Fact]
    public async Task Refresh_NotDue_DoesNotFetchAgain()
    {
        var fetcher = new Fetcher(_ => Jwks("k1"));
        var store = Store(fetcher);
        store.Register(new Uri("https://issuer/jwks"), null, TimeSpan.FromHours(1));

        await store.RefreshDueAsync(CancellationToken.None);
        await store.RefreshDueAsync(CancellationToken.None);

        fetcher.Requested.Count.ShouldBe(1);
    }

    [Fact]
    public void Register_SameSourceTwice_ReturnsSameHandle()
    {
        var store = Store(new Fetcher(_ => ""));

        store.Register(new Uri("https://i/jwks"), null, TimeSpan.FromHours(1))
            .ShouldBeSameAs(store.Register(new Uri("https://i/jwks"), null, TimeSpan.FromHours(1)));
    }

    [Fact]
    public void Register_NeitherOrBothSources_Throws()
    {
        var store = Store(new Fetcher(_ => ""));

        Should.Throw<ArgumentException>(() => store.Register(null, null, TimeSpan.FromHours(1)));
        Should.Throw<ArgumentException>(() => store.Register(new Uri("https://a"), new Uri("https://b"), TimeSpan.FromHours(1)));
    }
}

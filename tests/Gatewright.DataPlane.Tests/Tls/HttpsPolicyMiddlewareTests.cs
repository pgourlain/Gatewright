// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Configuration;
using Gatewright.Core.Snapshots;
using Gatewright.DataPlane.Tls;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Gatewright.DataPlane.Tests.Tls;

public sealed class HttpsPolicyMiddlewareTests
{
    private static (HttpsPolicyMiddleware Middleware, Func<bool> NextCalled) Create(IEnumerable<HostnameDefinition> hostnames, TlsOptions? options = null)
    {
        var store = new CertificateStore(NullLogger<CertificateStore>.Instance);
        store.Apply(new SecurityData { Hostnames = [.. hostnames] });
        var called = false;
        var middleware = new HttpsPolicyMiddleware(
            _ =>
            {
                called = true;
                return Task.CompletedTask;
            },
            store,
            Options.Create(options ?? new TlsOptions()));
        return (middleware, () => called);
    }

    private static DefaultHttpContext Request(string host, bool https, string path = "/orders/1", string query = "")
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = https ? "https" : "http";
        context.Request.Host = new HostString(host);
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query);
        return context;
    }

    [Fact]
    public async Task Http_HostnameWithRedirect_Gets308ToHttpsKeepingPathAndQuery()
    {
        var (middleware, nextCalled) = Create([new HostnameDefinition { Name = "api.example.com", RedirectHttpToHttps = true }]);
        var context = Request("api.example.com", https: false, query: "?a=1");

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.ShouldBe(StatusCodes.Status308PermanentRedirect);
        context.Response.Headers.Location.ToString().ShouldBe("https://api.example.com/orders/1?a=1");
        nextCalled().ShouldBeFalse();
    }

    [Fact]
    public async Task Redirect_UsesTheConfiguredPublicPort()
    {
        var (middleware, _) = Create([new HostnameDefinition { Name = "api.example.com", RedirectHttpToHttps = true }], new TlsOptions { HttpsPort = 8443 });
        var context = Request("api.example.com:8080", https: false);

        await middleware.InvokeAsync(context);

        context.Response.Headers.Location.ToString().ShouldBe("https://api.example.com:8443/orders/1");
    }

    [Fact]
    public async Task Http_HostnameWithoutRedirect_PassesThrough()
    {
        var (middleware, nextCalled) = Create([new HostnameDefinition { Name = "api.example.com" }]);

        await middleware.InvokeAsync(Request("api.example.com", https: false));

        nextCalled().ShouldBeTrue();
    }

    [Fact]
    public async Task Https_WithHsts_AddsTheHeaderWhenTheResponseStarts()
    {
        var hsts = new HstsDefinition { MaxAgeSeconds = 3600, IncludeSubDomains = true, Preload = true };
        var (middleware, nextCalled) = Create([new HostnameDefinition { Name = "api.example.com", Hsts = hsts }]);
        var context = Request("api.example.com", https: true);
        var feature = new TestResponseFeature();
        context.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpResponseFeature>(feature);

        await middleware.InvokeAsync(context);
        await feature.StartAsync();

        nextCalled().ShouldBeTrue();
        context.Response.Headers["Strict-Transport-Security"].ToString().ShouldBe("max-age=3600; includeSubDomains; preload");
    }

    [Theory]
    [InlineData("/healthz")]
    [InlineData("/readyz")]
    [InlineData("/gw/config")]
    public async Task OperationalEndpoints_AreNeverRedirected(string path)
    {
        var (middleware, nextCalled) = Create([new HostnameDefinition { Name = "*", RedirectHttpToHttps = true }]);

        await middleware.InvokeAsync(Request("10.0.0.5", https: false, path));

        nextCalled().ShouldBeTrue();
    }

    [Fact]
    public async Task UnknownHost_IsLeftAlone()
    {
        var (middleware, nextCalled) = Create([new HostnameDefinition { Name = "api.example.com", RedirectHttpToHttps = true }]);

        await middleware.InvokeAsync(Request("other.org", https: false));

        nextCalled().ShouldBeTrue();
    }

    private sealed class TestResponseFeature : Microsoft.AspNetCore.Http.Features.IHttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _starting = [];

        public int StatusCode { get; set; } = 200;

        public string? ReasonPhrase { get; set; }

        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

        public Stream Body { get; set; } = Stream.Null;

        public bool HasStarted { get; private set; }

        public void OnStarting(Func<object, Task> callback, object state) => _starting.Add((callback, state));

        public void OnCompleted(Func<object, Task> callback, object state)
        {
        }

        public async Task StartAsync()
        {
            HasStarted = true;
            foreach (var (callback, state) in _starting)
            {
                await callback(state);
            }
        }
    }
}

// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Bunit;
using Gatewright.ControlPlane.AdminApi;
using Gatewright.ControlPlane.Revisions;
using Gatewright.Core.Configuration;
using Gatewright.Core.Policies;
using Gatewright.Portal.Components.Pages;
using Gatewright.Portal.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Gatewright.Portal.Tests;

public sealed class PortalPagesTests : BunitContext
{
    private readonly FakePortalApi _api = new();

    public PortalPagesTests()
    {
        Services.AddSingleton<IPortalApi>(_api);
        Services.AddSingleton(new TokenProvider());
    }

    private void SignIn(params string[] roles)
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("alice");
        auth.SetRoles(roles);
    }

    private static ApiResponse Orders(PolicySet? policies = null) => new(
        "orders",
        "Orders",
        "/orders",
        "v1",
        new BackendDefinition { Destinations = ["http://orders:8080"] },
        policies ?? PolicySet.Empty,
        ["starter"],
        [new OperationResponse("list", "GET", "/", PolicySet.Empty), new OperationResponse("get", "GET", "/{id}", PolicySet.Empty)]);

    [Fact]
    public void Dashboard_ShowsEnvironmentsNodesAndValidationErrors()
    {
        SignIn("Reader");
        _api.Nodes.Add(new NodeResponse("dp-1", "prod", 2, "h", 3, false, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null));
        _api.Validation = (false, ["[api.path-conflict] apis[a]: conflict"]);

        var cut = Render<Dashboard>();
        cut.WaitForState(() => cut.FindAll("#environments, .error").Count > 0);

        cut.Find("#environments").TextContent.ShouldContain("nothing published");
        cut.Find("#environments").TextContent.ShouldContain("3");
        cut.Find("#nodes").TextContent.ShouldContain("dp-1");
        cut.Find("#nodes").TextContent.ShouldContain("behind");
        cut.Find("#validation-errors").TextContent.ShouldContain("api.path-conflict");
    }

    [Fact]
    public void Dashboard_ApiFailure_ShowsTheErrorInsteadOfCrashing()
    {
        SignIn("Reader");
        _api.Failure = new PortalApiException("The control plane cannot be reached");

        var cut = Render<Dashboard>();
        cut.WaitForState(() => cut.FindAll("#environments, .error").Count > 0);

        cut.Find(".error").TextContent.ShouldContain("cannot be reached");
    }

    [Fact]
    public void Apis_ListsAndFiltersBySearch()
    {
        SignIn("Reader");
        _api.Apis.Add(Orders());
        _api.Apis.Add(Orders() with { Name = "invoices", BasePath = "/invoices", Operations = [new OperationResponse("pay", "POST", "/pay", PolicySet.Empty)] });

        var cut = Render<Apis>();
        cut.FindAll("#apis tbody tr").Count.ShouldBe(2);

        cut.Find("input[type=search]").Input("pay");

        var rows = cut.FindAll("#apis tbody tr");
        rows.Count.ShouldBe(1);
        rows[0].TextContent.ShouldContain("invoices");
    }

    [Fact]
    public void ApiDetail_ShowsOperationsWithTheirEffectivePolicies()
    {
        SignIn("Admin");
        var global = new PolicySet { Inbound = [new PolicyReference { Type = "rateLimit" }] };
        _api.Apis.Add(Orders());
        _api.Draft = new GatewayConfig
        {
            GlobalPolicies = global,
            Apis = [new ApiDefinition { Name = "orders", BasePath = "/orders", Backend = new BackendDefinition { Destinations = ["http://b"] }, Operations = [new OperationDefinition { Name = "list", Method = "GET", Path = "/" }, new OperationDefinition { Name = "get", Method = "GET", Path = "/{id}" }] }],
        };

        var cut = Render<ApiDetail>(p => p.Add(c => c.Name, "orders"));
        cut.WaitForElement("#operations");

        cut.Find("#operations").TextContent.ShouldContain("rateLimit (Global)");
    }

    [Fact]
    public void ApiDetail_AdminAddsAPolicyFromTheCatalogEditsItsFormAndSaves()
    {
        SignIn("Admin");
        _api.Apis.Add(Orders());
        _api.Draft = new GatewayConfig { Apis = [new ApiDefinition { Name = "orders", BasePath = "/orders", Backend = new BackendDefinition { Destinations = ["http://b"] } }] };
        var cut = Render<ApiDetail>(p => p.Add(c => c.Name, "orders"));
        cut.WaitForElement("#operations");

        cut.Find("select.add").Change("rateLimit");
        cut.Find("[data-field=limit] input").Change("42");
        cut.Find("[data-field=shared] input").Change(true);
        cut.Find("[data-field=partitionBy] select").Change("global");
        cut.Find("#save").Click();

        var saved = _api.SavedApis.ShouldHaveSingleItem();
        var policy = saved.Request.Policies!.Inbound.ShouldHaveSingleItem();
        policy.Type.ShouldBe("rateLimit");
        policy.Config!.Value.GetProperty("limit").GetInt64().ShouldBe(42);
        policy.Config.Value.GetProperty("shared").GetBoolean().ShouldBeTrue();
        policy.Config.Value.GetProperty("partitionBy").GetString().ShouldBe("global");
    }

    [Fact]
    public void ApiDetail_OperationScope_SavesTheOperation()
    {
        SignIn("Admin");
        _api.Apis.Add(Orders());
        _api.Draft = new GatewayConfig { Apis = [new ApiDefinition { Name = "orders", BasePath = "/orders", Backend = new BackendDefinition { Destinations = ["http://b"] } }] };
        var cut = Render<ApiDetail>(p => p.Add(c => c.Name, "orders"));
        cut.WaitForElement("#operations");

        cut.Find("#scope").Change("get");
        cut.FindAll("select.add")[1].Change("timeout");
        cut.Find("#save").Click();

        var saved = _api.SavedOperations.ShouldHaveSingleItem();
        saved.Op.ShouldBe("get");
        saved.Request.Policies!.Backend.ShouldHaveSingleItem().Type.ShouldBe("timeout");
    }

    [Fact]
    public void ApiDetail_Reader_CannotEdit()
    {
        SignIn("Reader");
        _api.Apis.Add(Orders(new PolicySet { Inbound = [new PolicyReference { Type = "rateLimit" }] }));
        _api.Draft = new GatewayConfig { Apis = [new ApiDefinition { Name = "orders", BasePath = "/orders", Backend = new BackendDefinition { Destinations = ["http://b"] } }] };

        var cut = Render<ApiDetail>(p => p.Add(c => c.Name, "orders"));
        cut.WaitForElement("#operations");

        cut.FindAll("#save").ShouldBeEmpty();
        cut.FindAll("select.add").ShouldBeEmpty();
        cut.Markup.ShouldContain("rateLimit");
    }

    [Fact]
    public void ApiDetail_PolicyOrderCanBeChanged()
    {
        SignIn("Admin");
        _api.Apis.Add(Orders(new PolicySet { Inbound = [new PolicyReference { Type = "rateLimit" }, new PolicyReference { Type = "rateLimit", Inheritance = PolicyInheritance.Override }] }));
        _api.Draft = new GatewayConfig { Apis = [new ApiDefinition { Name = "orders", BasePath = "/orders", Backend = new BackendDefinition { Destinations = ["http://b"] } }] };
        var cut = Render<ApiDetail>(p => p.Add(c => c.Name, "orders"));
        cut.WaitForElement("#operations");

        cut.FindAll("[data-policy] button")[1].Click();
        cut.Find("#save").Click();

        _api.SavedApis.Single().Request.Policies!.Inbound.Select(p => p.Inheritance).ShouldBe([PolicyInheritance.Override, PolicyInheritance.Inherit]);
    }

    [Fact]
    public void Import_PreviewThenApplyShowsTheReport()
    {
        SignIn("Admin");
        _api.Services.Add(new DiscoveredServiceResponse("shop", "orders", [new DiscoveredPortResponse(80, "shop/orders:80", 2)], null, null, null));
        _api.ImportReport = new ApiImportReport("orders", true, false, ["basePath"], ["GET /orders (list)"], [], [], 0, ["careful"], []);
        var cut = Render<ImportApi>();
        cut.WaitForElement("#preview");

        cut.Find("#document").Input("openapi: 3.0.3");
        cut.Find("#service").Change("shop/orders:80");
        cut.Find("#product").Change("starter");
        cut.Find("#preview").Click();

        _api.LastImportQuery!.ShouldContain("service=shop%2Forders%3A80");
        _api.LastImportQuery!.ShouldContain("dryRun=true");
        cut.Find("#report").TextContent.ShouldContain("Preview");
        cut.Find("#report").TextContent.ShouldContain("added: GET /orders (list)");
        cut.Find("#apply").HasAttribute("disabled").ShouldBeFalse();

        cut.Find("#apply").Click();

        _api.LastImportQuery!.ShouldNotContain("dryRun");
        cut.Find("#report").TextContent.ShouldNotContain("Preview");
    }

    [Fact]
    public void Import_ServerRefusal_IsShown()
    {
        SignIn("Admin");
        var cut = Render<ImportApi>();
        cut.WaitForElement("#preview");

        cut.Find("#preview").Click();

        cut.Find(".error").TextContent.ShouldContain("no report configured");
    }

    [Fact]
    public void Subscriptions_KeyIsShownOnceAfterGenerationAndCanBeRotatedAndRevoked()
    {
        SignIn("Admin");
        _api.Subscriptions.Add(new SubscriptionResponse("acme-gold", "acme", "gold", null, true, [new KeyResponse("primary", DateTimeOffset.UtcNow, null)]));

        var cut = Render<Subscriptions>();
        cut.WaitForElement("#subscriptions");
        cut.Find(".create-secondary").Click();
        cut.Find("#issued").TextContent.ShouldContain("gw_secret-value");

        cut.Find(".rotate-primary").Click();
        cut.Find(".revoke-primary").Click();

        cut.FindAll("#issued").ShouldBeEmpty();
        _api.KeyOperations.ShouldBe(["create:acme-gold:secondary", "rotate:acme-gold:primary", "revoke:acme-gold:primary"]);
    }

    [Fact]
    public void Subscriptions_ReaderSeesNoKeyActions()
    {
        SignIn("Reader");
        _api.Subscriptions.Add(new SubscriptionResponse("acme-gold", "acme", "gold", null, true, [new KeyResponse("primary", DateTimeOffset.UtcNow, null)]));

        var cut = Render<Subscriptions>();
        cut.WaitForElement("#subscriptions");

        cut.FindAll("button").ShouldBeEmpty();
    }

    [Fact]
    public void Revisions_DiffTwoSelectedRevisions()
    {
        SignIn("Publisher");
        _api.Revisions.Add(new RevisionResponse(2, "h2", "api.create invoices", "alice", DateTimeOffset.UtcNow));
        _api.Revisions.Add(new RevisionResponse(1, "h1", "api.create orders", "alice", DateTimeOffset.UtcNow));
        var cut = Render<Revisions>();
        cut.WaitForElement("#revisions");
        cut.WaitForState(() => cut.FindAll(".pick").Count > 0 || _api.Revisions.Count == 0);

        cut.FindAll(".pick")[0].Change(true);
        cut.FindAll(".pick")[1].Change(true);
        cut.Find("#diff").Click();

        cut.Find("#diffTable").TextContent.ShouldContain("apis.invoices");
    }

    [Fact]
    public void Revisions_PublisherPublishesAndRollsBackTheSelectedRevision()
    {
        SignIn("Publisher");
        _api.Revisions.Add(new RevisionResponse(2, "h2", "x", "alice", DateTimeOffset.UtcNow));
        var cut = Render<Revisions>();
        cut.WaitForElement("#revisions");
        cut.WaitForState(() => cut.FindAll(".pick").Count > 0 || _api.Revisions.Count == 0);
        cut.Find(".pick").Change(true);

        cut.Find(".publish-prod").Click();
        cut.Find("#message").TextContent.ShouldContain("Published revision 2 in prod as version 9");
        cut.Find(".rollback-dev").Click();

        _api.Publications.ShouldBe([("prod", 2, false), ("dev", 2, true)]);
    }

    [Fact]
    public void Revisions_ReaderCannotPublish()
    {
        SignIn("Reader");
        _api.Revisions.Add(new RevisionResponse(2, "h2", "x", "alice", DateTimeOffset.UtcNow));
        var cut = Render<Revisions>();
        cut.WaitForElement("#revisions");
        cut.WaitForState(() => cut.FindAll(".pick").Count > 0 || _api.Revisions.Count == 0);
        cut.Find(".pick").Change(true);

        cut.FindAll("button[class^=publish]").ShouldBeEmpty();
        cut.FindAll("#publishLatest").ShouldBeEmpty();
    }

    [Fact]
    public void Console_ShowsTheStatusAndThePerPolicyTrace()
    {
        SignIn("Reader");
        Services.AddSingleton<IGatewayConsole>(new FakeConsole());
        var cut = Render<TestConsole>();

        cut.Find("#path").Change("/orders/1");
        cut.Find("#headers").Change("x-test: 1");
        cut.Find("#send").Click();

        cut.Find("#status").TextContent.ShouldContain("429");
        var trace = cut.Find("#trace").TextContent;
        trace.ShouldContain("rateLimit");
        trace.ShouldContain("Reject");
        cut.Find("#responseBody").TextContent.ShouldContain("slow down");
    }

    [Fact]
    public void PortalUser_DerivesCapabilitiesFromRoles()
    {
        PortalUser Make(params string[] roles) =>
            PortalUser.From(new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(roles.Select(r => new System.Security.Claims.Claim("roles", r)), "test")));

        Make("Admin").CanEdit.ShouldBeTrue();
        Make("Publisher").CanPublish.ShouldBeTrue();
        Make("Publisher").CanEdit.ShouldBeFalse();
        Make("Reader").CanRead.ShouldBeTrue();
        Make("Reader").CanPublish.ShouldBeFalse();
        Make().CanRead.ShouldBeFalse();
    }

    private sealed class FakeConsole : IGatewayConsole
    {
        public Task<ConsoleResponse> SendAsync(ConsoleRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConsoleResponse(
                429,
                ["Retry-After: 5"],
                "slow down",
                [new PolicyTraceEntry("headers", "Inbound", 0.2, "Continue"), new PolicyTraceEntry("rateLimit", "Inbound", 0.4, "Reject")],
                3.2));
    }
}

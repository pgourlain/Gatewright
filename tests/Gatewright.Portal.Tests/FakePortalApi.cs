// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Text.Json;
using Gatewright.ControlPlane.AdminApi;
using Gatewright.ControlPlane.Revisions;
using Gatewright.Core.Configuration;
using Gatewright.Portal.Services;

namespace Gatewright.Portal.Tests;

internal sealed class FakePortalApi : IPortalApi
{
    public List<ApiResponse> Apis { get; } = [];

    public GatewayConfig Draft { get; set; } = new();

    public List<PolicyTypeInfo> Types { get; } =
    [
        new("rateLimit", ["Inbound"], JsonElement.Parse("""{"type":"object","required":["limit"],"properties":{"limit":{"type":"integer","description":"Requests per period"},"partitionBy":{"enum":["subscription","ip","global"]},"shared":{"type":"boolean"}}}""")),
        new("timeout", ["Backend"], JsonElement.Parse("""{"type":"object","properties":{"seconds":{"type":"number"}}}""")),
    ];

    public List<DiscoveredServiceResponse> Services { get; } = [];

    public List<SubscriptionResponse> Subscriptions { get; } = [];

    public List<ProductResponse> Products { get; } = [];

    public List<ConsumerResponse> Consumers { get; } = [];

    public List<RevisionResponse> Revisions { get; } = [];

    public List<(string Name, long? Current)> Environments { get; } = [("dev", null), ("prod", 3)];

    public List<NodeResponse> Nodes { get; } = [];

    public List<(string Api, ApiRequest Request)> SavedApis { get; } = [];

    public List<(string Api, string Op, OperationRequest Request)> SavedOperations { get; } = [];

    public List<(string Environment, long Revision, bool Rollback)> Publications { get; } = [];

    public List<string> KeyOperations { get; } = [];

    public ApiImportReport? ImportReport { get; set; }

    public string? LastImportQuery { get; private set; }

    public (bool Valid, IReadOnlyList<string> Errors) Validation { get; set; } = (true, []);

    public Exception? Failure { get; set; }

    private T Guard<T>(T value) => Failure is null ? value : throw Failure;

    public Task<GatewayConfig> GetDraftAsync(CancellationToken ct = default) => Task.FromResult(Guard(Draft));

    public Task<IReadOnlyList<ApiResponse>> GetApisAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ApiResponse>>(Guard(Apis));

    public Task<ApiResponse?> GetApiAsync(string name, CancellationToken ct = default) => Task.FromResult(Guard(Apis.FirstOrDefault(a => a.Name == name)));

    public Task SaveApiAsync(string name, ApiRequest request, CancellationToken ct = default)
    {
        SavedApis.Add((name, request));
        return Task.CompletedTask;
    }

    public Task SaveOperationAsync(string api, string name, OperationRequest request, CancellationToken ct = default)
    {
        SavedOperations.Add((api, name, request));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PolicyTypeInfo>> GetPolicyTypesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PolicyTypeInfo>>(Types);

    public Task<IReadOnlyList<DiscoveredServiceResponse>> GetDiscoveredServicesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<DiscoveredServiceResponse>>(Services);

    public Task<ApiImportReport> ImportOpenApiAsync(string document, string query, CancellationToken ct = default)
    {
        LastImportQuery = query;
        var report = ImportReport ?? throw new PortalApiException("no report configured");
        return Task.FromResult(report with { Applied = !query.Contains("dryRun=true", StringComparison.Ordinal) });
    }

    public Task<IReadOnlyList<ProductResponse>> GetProductsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ProductResponse>>(Products);

    public Task SaveProductAsync(string name, ProductRequest request, bool create, CancellationToken ct = default)
    {
        Products.Add(new ProductResponse(name, request.RequiresSubscription, PolicySet.Empty, [], []));
        return Task.CompletedTask;
    }

    public Task SavePlanAsync(string product, string name, PlanRequest request, CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<ConsumerResponse>> GetConsumersAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ConsumerResponse>>(Consumers);

    public Task CreateConsumerAsync(ConsumerRequest request, CancellationToken ct = default)
    {
        Consumers.Add(new ConsumerResponse(request.Name, request.Contact));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SubscriptionResponse>> GetSubscriptionsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SubscriptionResponse>>(Subscriptions);

    public Task CreateSubscriptionAsync(SubscriptionRequest request, CancellationToken ct = default) => Task.CompletedTask;

    public Task<KeyIssued> IssueKeyAsync(string subscription, string label, bool rotate, CancellationToken ct = default)
    {
        KeyOperations.Add($"{(rotate ? "rotate" : "create")}:{subscription}:{label}");
        return Task.FromResult(new KeyIssued(label, "gw_secret-value"));
    }

    public Task RevokeKeyAsync(string subscription, string label, CancellationToken ct = default)
    {
        KeyOperations.Add($"revoke:{subscription}:{label}");
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RevisionResponse>> GetRevisionsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<RevisionResponse>>(Revisions);

    public Task<IReadOnlyList<DiffEntry>> GetDiffAsync(long fromRevision, long toRevision, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<DiffEntry>>([new DiffEntry(DiffKind.Added, "apis.invoices", null, "{}")]);

    public Task<IReadOnlyList<(string Name, long? Current)>> GetEnvironmentsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<(string, long?)>>(Guard(Environments));

    public Task<PublicationResponse> PublishAsync(string environment, long revision, bool rollback, CancellationToken ct = default)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        Publications.Add((environment, revision, rollback));
        return Task.FromResult(new PublicationResponse(9, revision, rollback ? "Rollback" : "Publish", "hash", "me", DateTimeOffset.UtcNow));
    }

    public Task<IReadOnlyList<AuditResponse>> GetAuditAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AuditResponse>>([new AuditResponse(DateTimeOffset.UtcNow, "alice", "api.create", "api", "orders", "revision 1")]);

    public Task<IReadOnlyList<NodeResponse>> GetNodesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<NodeResponse>>(Guard(Nodes));

    public Task<(bool Valid, IReadOnlyList<string> Errors)> ValidateDraftAsync(CancellationToken ct = default) => Task.FromResult(Validation);
}

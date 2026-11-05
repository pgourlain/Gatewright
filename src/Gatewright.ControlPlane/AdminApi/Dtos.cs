// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Configuration;

namespace Gatewright.ControlPlane.AdminApi;

/// <summary>Body to create or replace an API.</summary>
public sealed record ApiRequest
{
    /// <summary>Gets the unique name (letters, digits, <c>.</c>, <c>_</c>, <c>-</c>).</summary>
    public string Name { get; init; } = "";

    /// <summary>Gets a display name.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Gets the base path, starting with <c>/</c>.</summary>
    public string BasePath { get; init; } = "";

    /// <summary>Gets the API version.</summary>
    public string? Version { get; init; }

    /// <summary>Gets the backend.</summary>
    public BackendDefinition Backend { get; init; } = new();

    /// <summary>Gets the policies declared at API scope.</summary>
    public PolicySet? Policies { get; init; }

    /// <summary>Gets the names of the products the API belongs to.</summary>
    public List<string> Products { get; init; } = [];
}

/// <summary>An API as returned by the Admin API.</summary>
/// <param name="Name">The unique name.</param>
/// <param name="DisplayName">The display name.</param>
/// <param name="BasePath">The base path.</param>
/// <param name="Version">The version.</param>
/// <param name="Backend">The backend.</param>
/// <param name="Policies">The API-scope policies.</param>
/// <param name="Products">The product names.</param>
/// <param name="Operations">The operations.</param>
public sealed record ApiResponse(
    string Name,
    string? DisplayName,
    string BasePath,
    string? Version,
    BackendDefinition Backend,
    PolicySet Policies,
    IReadOnlyList<string> Products,
    IReadOnlyList<OperationResponse> Operations);

/// <summary>Body to create or replace an operation.</summary>
public sealed record OperationRequest
{
    /// <summary>Gets the name, unique within the API.</summary>
    public string Name { get; init; } = "";

    /// <summary>Gets the HTTP method.</summary>
    public string Method { get; init; } = "GET";

    /// <summary>Gets the path template relative to the API base path.</summary>
    public string Path { get; init; } = "/";

    /// <summary>Gets the operation-scope policies.</summary>
    public PolicySet? Policies { get; init; }
}

/// <summary>An operation as returned by the Admin API.</summary>
/// <param name="Name">The name.</param>
/// <param name="Method">The HTTP method.</param>
/// <param name="Path">The path template.</param>
/// <param name="Policies">The operation-scope policies.</param>
public sealed record OperationResponse(string Name, string Method, string Path, PolicySet Policies);

/// <summary>Body to create or replace a product.</summary>
public sealed record ProductRequest
{
    /// <summary>Gets the unique name.</summary>
    public string Name { get; init; } = "";

    /// <summary>Gets a value indicating whether calls require a subscription.</summary>
    public bool RequiresSubscription { get; init; }

    /// <summary>Gets the product-scope policies.</summary>
    public PolicySet? Policies { get; init; }
}

/// <summary>A product as returned by the Admin API.</summary>
/// <param name="Name">The unique name.</param>
/// <param name="RequiresSubscription">Whether calls require a subscription.</param>
/// <param name="Policies">The product-scope policies.</param>
/// <param name="Apis">The API names.</param>
/// <param name="Plans">The plan names.</param>
public sealed record ProductResponse(string Name, bool RequiresSubscription, PolicySet Policies, IReadOnlyList<string> Apis, IReadOnlyList<string> Plans);

/// <summary>Body to create or replace a plan.</summary>
public sealed record PlanRequest
{
    /// <summary>Gets the name, unique within the product.</summary>
    public string Name { get; init; } = "";

    /// <summary>Gets a description.</summary>
    public string? Description { get; init; }
}

/// <summary>A plan as returned by the Admin API.</summary>
/// <param name="Name">The name.</param>
/// <param name="Description">The description.</param>
public sealed record PlanResponse(string Name, string? Description);

/// <summary>Body to create or replace a consumer.</summary>
public sealed record ConsumerRequest
{
    /// <summary>Gets the unique name.</summary>
    public string Name { get; init; } = "";

    /// <summary>Gets a contact address.</summary>
    public string? Contact { get; init; }
}

/// <summary>A consumer as returned by the Admin API.</summary>
/// <param name="Name">The name.</param>
/// <param name="Contact">The contact address.</param>
public sealed record ConsumerResponse(string Name, string? Contact);

/// <summary>Body to create or replace a subscription.</summary>
public sealed record SubscriptionRequest
{
    /// <summary>Gets the unique name (the subscription id in the gateway configuration).</summary>
    public string Name { get; init; } = "";

    /// <summary>Gets the consumer name.</summary>
    public string Consumer { get; init; } = "";

    /// <summary>Gets the product name.</summary>
    public string Product { get; init; } = "";

    /// <summary>Gets the plan name, if any.</summary>
    public string? Plan { get; init; }

    /// <summary>Gets a value indicating whether the subscription is active.</summary>
    public bool Active { get; init; } = true;
}

/// <summary>A subscription as returned by the Admin API. Keys are never returned, only their labels and state.</summary>
/// <param name="Name">The name.</param>
/// <param name="Consumer">The consumer name.</param>
/// <param name="Product">The product name.</param>
/// <param name="Plan">The plan name.</param>
/// <param name="Active">Whether the subscription is active.</param>
/// <param name="Keys">The keys.</param>
public sealed record SubscriptionResponse(string Name, string Consumer, string Product, string? Plan, bool Active, IReadOnlyList<KeyResponse> Keys);

/// <summary>The state of a subscription key.</summary>
/// <param name="Label">The label (<c>primary</c>, <c>secondary</c>).</param>
/// <param name="CreatedAt">When the key was created.</param>
/// <param name="RevokedAt">When the key was revoked, or <see langword="null"/>.</param>
public sealed record KeyResponse(string Label, DateTimeOffset CreatedAt, DateTimeOffset? RevokedAt);

/// <summary>A newly generated key. The plaintext value is shown once and never stored.</summary>
/// <param name="Label">The label.</param>
/// <param name="Key">The plaintext key.</param>
public sealed record KeyIssued(string Label, string Key);

/// <summary>The global and gateway-wide policy sets.</summary>
public sealed record ScopePoliciesDto
{
    /// <summary>Gets the policies applied to every API.</summary>
    public PolicySet Global { get; init; } = PolicySet.Empty;

    /// <summary>Gets the policies applied to every API of this gateway.</summary>
    public PolicySet Gateway { get; init; } = PolicySet.Empty;
}

/// <summary>A revision as listed by the Admin API.</summary>
/// <param name="Number">The sequence number.</param>
/// <param name="Hash">The content hash.</param>
/// <param name="Summary">What changed.</param>
/// <param name="CreatedBy">Who changed it.</param>
/// <param name="CreatedAt">When.</param>
public sealed record RevisionResponse(long Number, string Hash, string Summary, string CreatedBy, DateTimeOffset CreatedAt);

/// <summary>Body of a publication or rollback request.</summary>
public sealed record PublishRequest
{
    /// <summary>Gets the revision number to publish or restore.</summary>
    public long Revision { get; init; }
}

/// <summary>A publication as returned by the Admin API.</summary>
/// <param name="Number">The publication number (the configuration version seen by data planes).</param>
/// <param name="Revision">The published revision number.</param>
/// <param name="Kind">Publish or Rollback.</param>
/// <param name="Hash">The snapshot hash.</param>
/// <param name="PublishedBy">Who published.</param>
/// <param name="PublishedAt">When.</param>
public sealed record PublicationResponse(long Number, long Revision, string Kind, string Hash, string PublishedBy, DateTimeOffset PublishedAt);

/// <summary>An audit entry as returned by the Admin API.</summary>
/// <param name="Timestamp">When.</param>
/// <param name="Actor">Who.</param>
/// <param name="Action">What.</param>
/// <param name="EntityType">On which kind of object.</param>
/// <param name="EntityId">On which object.</param>
/// <param name="Details">Details.</param>
public sealed record AuditResponse(DateTimeOffset Timestamp, string Actor, string Action, string EntityType, string EntityId, string Details);

/// <summary>A data plane node and the configuration version it applied.</summary>
/// <param name="NodeId">The node identifier.</param>
/// <param name="Environment">The environment it follows.</param>
/// <param name="AppliedVersion">The version it applied.</param>
/// <param name="AppliedHash">The hash it applied.</param>
/// <param name="LatestVersion">The active publication of its environment.</param>
/// <param name="InSync">Whether the node applied the active publication.</param>
/// <param name="Connected">Whether it currently has an open stream.</param>
/// <param name="ConnectedAt">When it last connected.</param>
/// <param name="LastSeen">When it was last heard from.</param>
/// <param name="LastError">The last refusal it reported.</param>
public sealed record NodeResponse(
    string NodeId,
    string Environment,
    long AppliedVersion,
    string AppliedHash,
    long LatestVersion,
    bool InSync,
    bool Connected,
    DateTimeOffset ConnectedAt,
    DateTimeOffset LastSeen,
    string? LastError);

/// <summary>A service found by discovery.</summary>
/// <param name="Namespace">The namespace.</param>
/// <param name="Name">The service name.</param>
/// <param name="Ports">The ports that can be used as backend.</param>
/// <param name="OpenApiPath">The OpenAPI document path, if declared.</param>
/// <param name="BasePath">The requested base path, if declared.</param>
/// <param name="Product">The requested product, if declared.</param>
public sealed record DiscoveredServiceResponse(
    string Namespace,
    string Name,
    IReadOnlyList<DiscoveredPortResponse> Ports,
    string? OpenApiPath,
    string? BasePath,
    string? Product);

/// <summary>A discovered service port and how many ready endpoints back it.</summary>
/// <param name="Port">The service port.</param>
/// <param name="Reference">The value to use as <c>backend.service</c>.</param>
/// <param name="ReadyEndpoints">The number of ready endpoints.</param>
public sealed record DiscoveredPortResponse(int Port, string Reference, int ReadyEndpoints);

/// <summary>Body to create or replace a hostname.</summary>
public sealed record HostnameRequest
{
    /// <summary>Gets the certificate source (file paths or a secret reference).</summary>
    public CertificateSource? Certificate { get; init; }

    /// <summary>Gets a value indicating whether plain HTTP is redirected to HTTPS.</summary>
    public bool RedirectHttpToHttps { get; init; }

    /// <summary>Gets the HSTS settings.</summary>
    public HstsDefinition? Hsts { get; init; }
}

/// <summary>A hostname as returned by the Admin API.</summary>
/// <param name="Name">The hostname or wildcard.</param>
/// <param name="Certificate">The certificate source.</param>
/// <param name="RedirectHttpToHttps">Whether HTTP is redirected to HTTPS.</param>
/// <param name="Hsts">The HSTS settings.</param>
public sealed record HostnameResponse(string Name, CertificateSource? Certificate, bool RedirectHttpToHttps, HstsDefinition? Hsts);

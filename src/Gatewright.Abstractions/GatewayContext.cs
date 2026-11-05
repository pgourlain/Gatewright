// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Gatewright.Abstractions;

/// <summary>The state shared by all policies while a request goes through the pipeline.</summary>
public sealed class GatewayContext
{
    private Dictionary<string, object?>? _variables;

    /// <summary>Initializes a new instance of the <see cref="GatewayContext"/> class.</summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="routeId">The identifier of the matched route.</param>
    /// <param name="api">The matched API.</param>
    /// <param name="operation">The matched operation, or <see langword="null"/> when the route covers the whole API.</param>
    /// <param name="cancellationToken">A token cancelled when the request is aborted or a policy times out.</param>
    public GatewayContext(
        HttpContext httpContext,
        string routeId,
        ApiInfo api,
        OperationInfo? operation = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(routeId);
        ArgumentNullException.ThrowIfNull(api);
        HttpContext = httpContext;
        RouteId = routeId;
        Api = api;
        Operation = operation;
        CancellationToken = cancellationToken;
    }

    /// <summary>Gets the current HTTP context.</summary>
    public HttpContext HttpContext { get; }

    /// <summary>Gets the identifier of the matched route.</summary>
    public string RouteId { get; }

    /// <summary>Gets the matched API.</summary>
    public ApiInfo Api { get; }

    /// <summary>Gets the matched operation, or <see langword="null"/> when the route covers the whole API.</summary>
    public OperationInfo? Operation { get; }

    /// <summary>Gets or sets the authenticated identity, set by authentication policies.</summary>
    public ClaimsPrincipal? Identity { get; set; }

    /// <summary>Gets or sets the subscription, set by the API key policy.</summary>
    public SubscriptionInfo? Subscription { get; set; }

    /// <summary>Gets or sets the phase currently being executed.</summary>
    public PolicyPhase Phase { get; set; }

    /// <summary>Gets the token cancelled when the request is aborted or the current policy times out. Replaced by the pipeline around each policy call.</summary>
    public CancellationToken CancellationToken { get; set; }

    /// <summary>Gets or sets the failure being handled; set by the pipeline before the <see cref="PolicyPhase.OnError"/> phase runs.</summary>
    public PolicyFailure? Failure { get; set; }

    /// <summary>Gets the per-request variables policies can use to exchange data. Created on first access.</summary>
    public IDictionary<string, object?> Variables => _variables ??= new Dictionary<string, object?>(StringComparer.Ordinal);
}

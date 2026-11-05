// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Gatewright.ControlPlane.AdminApi;
using Gatewright.ControlPlane.Revisions;
using Gatewright.Core.Configuration;
using Microsoft.Extensions.Options;

namespace Gatewright.Portal.Services;

/// <summary>A failure reported by the Admin API, with a message fit to show.</summary>
public sealed class PortalApiException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="PortalApiException"/> class.</summary>
    public PortalApiException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PortalApiException"/> class.</summary>
    /// <param name="message">The message.</param>
    public PortalApiException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PortalApiException"/> class.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public PortalApiException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>A policy type offered by the catalog.</summary>
/// <param name="Type">The type name.</param>
/// <param name="Phases">The phases it supports.</param>
/// <param name="Schema">The JSON Schema of its configuration.</param>
public sealed record PolicyTypeInfo(string Type, IReadOnlyList<string> Phases, JsonElement Schema);

/// <summary>What the portal needs from the Admin API.</summary>
public interface IPortalApi
{
    Task<GatewayConfig> GetDraftAsync(CancellationToken ct = default);

    Task<IReadOnlyList<ApiResponse>> GetApisAsync(CancellationToken ct = default);

    Task<ApiResponse?> GetApiAsync(string name, CancellationToken ct = default);

    Task SaveApiAsync(string name, ApiRequest request, CancellationToken ct = default);

    Task SaveOperationAsync(string api, string name, OperationRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<PolicyTypeInfo>> GetPolicyTypesAsync(CancellationToken ct = default);

    Task<IReadOnlyList<DiscoveredServiceResponse>> GetDiscoveredServicesAsync(CancellationToken ct = default);

    Task<ApiImportReport> ImportOpenApiAsync(string document, string query, CancellationToken ct = default);

    Task<IReadOnlyList<ProductResponse>> GetProductsAsync(CancellationToken ct = default);

    Task SaveProductAsync(string name, ProductRequest request, bool create, CancellationToken ct = default);

    Task SavePlanAsync(string product, string name, PlanRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<ConsumerResponse>> GetConsumersAsync(CancellationToken ct = default);

    Task CreateConsumerAsync(ConsumerRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<SubscriptionResponse>> GetSubscriptionsAsync(CancellationToken ct = default);

    Task CreateSubscriptionAsync(SubscriptionRequest request, CancellationToken ct = default);

    Task<KeyIssued> IssueKeyAsync(string subscription, string label, bool rotate, CancellationToken ct = default);

    Task RevokeKeyAsync(string subscription, string label, CancellationToken ct = default);

    Task<IReadOnlyList<RevisionResponse>> GetRevisionsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<DiffEntry>> GetDiffAsync(long fromRevision, long toRevision, CancellationToken ct = default);

    Task<IReadOnlyList<(string Name, long? Current)>> GetEnvironmentsAsync(CancellationToken ct = default);

    Task<PublicationResponse> PublishAsync(string environment, long revision, bool rollback, CancellationToken ct = default);

    Task<IReadOnlyList<AuditResponse>> GetAuditAsync(CancellationToken ct = default);

    Task<IReadOnlyList<NodeResponse>> GetNodesAsync(CancellationToken ct = default);

    Task<(bool Valid, IReadOnlyList<string> Errors)> ValidateDraftAsync(CancellationToken ct = default);
}

/// <summary>HTTP implementation of <see cref="IPortalApi"/>: calls the Admin API on behalf of the signed-in user.</summary>
public sealed class PortalApi : IPortalApi
{
    private static readonly JsonSerializerOptions Json = GatewayJson.SerializerOptions;

    private readonly HttpClient _http;

    /// <summary>Initializes a new instance of the <see cref="PortalApi"/> class.</summary>
    /// <param name="http">The HTTP client.</param>
    /// <param name="options">The portal options.</param>
    /// <param name="tokens">The access token holder.</param>
    public PortalApi(HttpClient http, IOptions<PortalOptions> options, TokenProvider tokens)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(tokens);
        _http = http;
        _http.BaseAddress ??= new Uri(options.Value.AdminApiBaseUrl.TrimEnd('/') + "/admin/v1/");
        Tokens = tokens;
    }

    private TokenProvider Tokens { get; }

    public Task<GatewayConfig> GetDraftAsync(CancellationToken ct = default) => GetAsync<GatewayConfig>("draft", ct);

    public Task<IReadOnlyList<ApiResponse>> GetApisAsync(CancellationToken ct = default) => GetAsync<IReadOnlyList<ApiResponse>>("apis", ct);

    public async Task<ApiResponse?> GetApiAsync(string name, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "apis/" + Uri.EscapeDataString(name));
        using var response = await SendAsync(request, ct, allowNotFound: true).ConfigureAwait(false);
        return response.StatusCode == System.Net.HttpStatusCode.NotFound ? null : await ReadAsync<ApiResponse>(response, ct).ConfigureAwait(false);
    }

    public Task SaveApiAsync(string name, ApiRequest request, CancellationToken ct = default) => SendOkAsync(HttpMethod.Put, "apis/" + Uri.EscapeDataString(name), request, ct);

    public Task SaveOperationAsync(string api, string name, OperationRequest request, CancellationToken ct = default) =>
        SendOkAsync(HttpMethod.Put, $"apis/{Uri.EscapeDataString(api)}/operations/{Uri.EscapeDataString(name)}", request, ct);

    public Task<IReadOnlyList<PolicyTypeInfo>> GetPolicyTypesAsync(CancellationToken ct = default) => GetAsync<IReadOnlyList<PolicyTypeInfo>>("policy-types", ct);

    public Task<IReadOnlyList<DiscoveredServiceResponse>> GetDiscoveredServicesAsync(CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<DiscoveredServiceResponse>>("discovery/services", ct);

    public async Task<ApiImportReport> ImportOpenApiAsync(string document, string query, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "apis/import" + query) { Content = new StringContent(document, Encoding.UTF8, "application/yaml") };
        using var response = await SendAsync(request, ct).ConfigureAwait(false);
        return await ReadAsync<ApiImportReport>(response, ct).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<ProductResponse>> GetProductsAsync(CancellationToken ct = default) => GetAsync<IReadOnlyList<ProductResponse>>("products", ct);

    public Task SaveProductAsync(string name, ProductRequest request, bool create, CancellationToken ct = default) =>
        create ? SendOkAsync(HttpMethod.Post, "products", request with { Name = name }, ct) : SendOkAsync(HttpMethod.Put, "products/" + Uri.EscapeDataString(name), request, ct);

    public Task SavePlanAsync(string product, string name, PlanRequest request, CancellationToken ct = default) =>
        SendOkAsync(HttpMethod.Put, $"products/{Uri.EscapeDataString(product)}/plans/{Uri.EscapeDataString(name)}", request, ct);

    public Task<IReadOnlyList<ConsumerResponse>> GetConsumersAsync(CancellationToken ct = default) => GetAsync<IReadOnlyList<ConsumerResponse>>("consumers", ct);

    public Task CreateConsumerAsync(ConsumerRequest request, CancellationToken ct = default) => SendOkAsync(HttpMethod.Post, "consumers", request, ct);

    public Task<IReadOnlyList<SubscriptionResponse>> GetSubscriptionsAsync(CancellationToken ct = default) => GetAsync<IReadOnlyList<SubscriptionResponse>>("subscriptions", ct);

    public Task CreateSubscriptionAsync(SubscriptionRequest request, CancellationToken ct = default) => SendOkAsync(HttpMethod.Post, "subscriptions", request, ct);

    public async Task<KeyIssued> IssueKeyAsync(string subscription, string label, bool rotate, CancellationToken ct = default)
    {
        var path = $"subscriptions/{Uri.EscapeDataString(subscription)}/keys/{Uri.EscapeDataString(label)}" + (rotate ? "/rotate" : "");
        using var response = await SendAsync(HttpMethod.Post, path, null, ct).ConfigureAwait(false);
        return await ReadAsync<KeyIssued>(response, ct).ConfigureAwait(false);
    }

    public Task RevokeKeyAsync(string subscription, string label, CancellationToken ct = default) =>
        SendOkAsync(HttpMethod.Delete, $"subscriptions/{Uri.EscapeDataString(subscription)}/keys/{Uri.EscapeDataString(label)}", null, ct);

    public Task<IReadOnlyList<RevisionResponse>> GetRevisionsAsync(CancellationToken ct = default) => GetAsync<IReadOnlyList<RevisionResponse>>("revisions", ct);

    public async Task<IReadOnlyList<DiffEntry>> GetDiffAsync(long fromRevision, long toRevision, CancellationToken ct = default)
    {
        var items = await GetAsync<List<DiffWire>>($"revisions/{fromRevision}/diff/{toRevision}", ct).ConfigureAwait(false);
        return [.. items.Select(i => new DiffEntry(Enum.Parse<DiffKind>(i.Kind), i.Path, i.Before, i.After))];
    }

    public async Task<IReadOnlyList<(string Name, long? Current)>> GetEnvironmentsAsync(CancellationToken ct = default)
    {
        var items = await GetAsync<List<EnvironmentWire>>("environments", ct).ConfigureAwait(false);
        return [.. items.Select(i => (i.Name, i.CurrentPublication))];
    }

    public async Task<PublicationResponse> PublishAsync(string environment, long revision, bool rollback, CancellationToken ct = default)
    {
        var path = $"environments/{Uri.EscapeDataString(environment)}/" + (rollback ? "rollback" : "publications");
        using var response = await SendAsync(HttpMethod.Post, path, new PublishRequest { Revision = revision }, ct).ConfigureAwait(false);
        return await ReadAsync<PublicationResponse>(response, ct).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<AuditResponse>> GetAuditAsync(CancellationToken ct = default) => GetAsync<IReadOnlyList<AuditResponse>>("audit", ct);

    public Task<IReadOnlyList<NodeResponse>> GetNodesAsync(CancellationToken ct = default) => GetAsync<IReadOnlyList<NodeResponse>>("nodes", ct);

    public async Task<(bool Valid, IReadOnlyList<string> Errors)> ValidateDraftAsync(CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Post, "validate", null, ct).ConfigureAwait(false);
        var result = await ReadAsync<ValidateWire>(response, ct).ConfigureAwait(false);
        return (result.Valid, result.Errors);
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Get, path, null, ct).ConfigureAwait(false);
        return await ReadAsync<T>(response, ct).ConfigureAwait(false);
    }

    private async Task SendOkAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var response = await SendAsync(method, path, body, ct).ConfigureAwait(false);
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: Json);
        }

        return SendAsync(request, ct);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct, bool allowNotFound = false)
    {
        if (!string.IsNullOrEmpty(Tokens.AccessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Tokens.AccessToken);
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new PortalApiException("The control plane cannot be reached: " + ex.Message, ex);
        }

        if (response.IsSuccessStatusCode || (allowNotFound && response.StatusCode == System.Net.HttpStatusCode.NotFound))
        {
            return response;
        }

        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        response.Dispose();
        throw new PortalApiException(Describe(response.StatusCode, text));
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct) =>
        await response.Content.ReadFromJsonAsync<T>(Json, ct).ConfigureAwait(false) ?? throw new PortalApiException("Empty response from the control plane.");

    private static string Describe(System.Net.HttpStatusCode status, string body)
    {
        if (status == System.Net.HttpStatusCode.Unauthorized)
        {
            return "Your session is not authenticated. Sign in again.";
        }

        if (status == System.Net.HttpStatusCode.Forbidden)
        {
            return "Your account lacks the role required for this action.";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                return string.Join(" ", errors.EnumerateObject().SelectMany(f => f.Value.EnumerateArray().Select(e => $"{f.Name}: {e.GetString()}")));
            }

            if (document.RootElement.TryGetProperty("detail", out var detail))
            {
                return detail.GetString() ?? status.ToString();
            }
        }
        catch (JsonException)
        {
            // Not JSON.
        }

        return $"{(int)status} {status}";
    }

    private sealed record DiffWire(string Kind, string Path, string? Before, string? After);

    private sealed record EnvironmentWire(string Name, long? CurrentPublication);

    private sealed record ValidateWire(bool Valid, List<string> Errors);
}

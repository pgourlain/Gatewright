// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Gatewright.Core.Policies;
using Microsoft.Extensions.Options;

namespace Gatewright.Portal.Services;

/// <summary>A request typed in the test console.</summary>
/// <param name="Method">The HTTP method.</param>
/// <param name="Path">The path and query, for example <c>/orders/1?x=2</c>.</param>
/// <param name="Headers">Header lines (<c>Name: value</c>).</param>
/// <param name="Body">The request body.</param>
public sealed record ConsoleRequest(string Method, string Path, IReadOnlyList<string> Headers, string? Body);

/// <summary>The answer shown by the test console.</summary>
/// <param name="StatusCode">The HTTP status.</param>
/// <param name="Headers">Response headers.</param>
/// <param name="Body">The response body.</param>
/// <param name="Trace">The policy trace (duration and result per policy), empty when the gateway returned none.</param>
/// <param name="ElapsedMs">Total round-trip time.</param>
public sealed record ConsoleResponse(int StatusCode, IReadOnlyList<string> Headers, string Body, IReadOnlyList<PolicyTraceEntry> Trace, double ElapsedMs);

/// <summary>Sends test requests through the gateway and reads the per-policy trace it returns.</summary>
public interface IGatewayConsole
{
    /// <summary>Sends a request.</summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The response with its trace.</returns>
    Task<ConsoleResponse> SendAsync(ConsoleRequest request, CancellationToken cancellationToken = default);
}

/// <summary>HTTP implementation of <see cref="IGatewayConsole"/>.</summary>
public sealed class GatewayConsole : IGatewayConsole
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly PortalOptions _options;

    /// <summary>Initializes a new instance of the <see cref="GatewayConsole"/> class.</summary>
    /// <param name="http">The HTTP client.</param>
    /// <param name="options">The portal options.</param>
    public GatewayConsole(HttpClient http, IOptions<PortalOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        _http = http;
        _options = options.Value;
    }

    /// <inheritdoc/>
    public async Task<ConsoleResponse> SendAsync(ConsoleRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var path = request.Path.StartsWith('/') ? request.Path : "/" + request.Path;
        using var message = new HttpRequestMessage(new HttpMethod(request.Method), new Uri(_options.GatewayBaseUrl.TrimEnd('/') + path));
        foreach (var line in request.Headers)
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0)
            {
                var name = line[..colon].Trim();
                var value = line[(colon + 1)..].Trim();
                if (!message.Headers.TryAddWithoutValidation(name, value))
                {
                    message.Content ??= new StringContent(request.Body ?? "");
                    message.Content.Headers.TryAddWithoutValidation(name, value);
                }
            }
        }

        if (!string.IsNullOrEmpty(request.Body))
        {
            message.Content ??= new StringContent(request.Body, Encoding.UTF8, "application/json");
        }

        if (!string.IsNullOrEmpty(_options.GatewayDiagnosticsToken))
        {
            message.Headers.TryAddWithoutValidation("x-gatewright-trace", _options.GatewayDiagnosticsToken);
        }

        var watch = Stopwatch.StartNew();
        using var response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        watch.Stop();

        var trace = new List<PolicyTraceEntry>();
        if (response.Headers.TryGetValues("x-gatewright-trace-result", out var values))
        {
            trace = JsonSerializer.Deserialize<List<PolicyTraceEntry>>(Convert.FromBase64String(values.First()), Json) ?? [];
        }

        var headers = response.Headers.Concat(response.Content.Headers)
            .Where(h => h.Key != "x-gatewright-trace-result")
            .Select(h => $"{h.Key}: {string.Join(", ", h.Value)}")
            .ToList();
        return new ConsoleResponse((int)response.StatusCode, headers, body, trace, Math.Round(watch.Elapsed.TotalMilliseconds, 1));
    }
}

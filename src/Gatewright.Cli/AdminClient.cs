// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Gatewright.Cli;

/// <summary>A failure talking to the Admin API, with a message fit to print.</summary>
public sealed class CliException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="CliException"/> class.</summary>
    /// <param name="message">The message.</param>
    public CliException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="CliException"/> class.</summary>
    public CliException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="CliException"/> class.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public CliException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Thin client over the Admin API.</summary>
public sealed class AdminClient
{
    private readonly HttpClient _http;

    /// <summary>Initializes a new instance of the <see cref="AdminClient"/> class.</summary>
    /// <param name="http">The HTTP client (its base address is set from the settings when empty).</param>
    /// <param name="settings">The server and token.</param>
    /// <exception cref="CliException">No server is configured.</exception>
    public AdminClient(HttpClient http, CliSettings settings)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(settings);
        _http = http;
        if (_http.BaseAddress is null)
        {
            _http.BaseAddress = new Uri((settings.Server ?? throw new CliException("No server configured. Run 'gatewright login --server <url> --token <token>' or set GATEWRIGHT_SERVER.")).TrimEnd('/') + "/");
        }

        if (!string.IsNullOrEmpty(settings.Token))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.Token);
        }
    }

    /// <summary>Sends a request and returns the response body, failing with a readable message on errors.</summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The path relative to the server, for example <c>admin/v1/apis</c>.</param>
    /// <param name="body">The request body, or <see langword="null"/>.</param>
    /// <param name="contentType">The body content type.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The response body.</returns>
    public async Task<string> SendAsync(HttpMethod method, string path, string? body = null, string contentType = "application/json", CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(method, path.TrimStart('/'));
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, contentType);
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new CliException($"Cannot reach the server: {ex.Message}", ex);
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return text;
            }

            throw new CliException(Describe(response.StatusCode, text));
        }
    }

    private static string Describe(HttpStatusCode status, string body)
    {
        switch (status)
        {
            case HttpStatusCode.Unauthorized:
                return "Not authenticated (401). Run 'gatewright login' with a valid token.";
            case HttpStatusCode.Forbidden:
                return "Forbidden (403): your account lacks the role required for this command.";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var lines = new List<string>();
            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                foreach (var field in errors.EnumerateObject())
                {
                    lines.AddRange(field.Value.EnumerateArray().Select(e => $"{field.Name}: {e.GetString()}"));
                }
            }
            else if (root.TryGetProperty("detail", out var detail))
            {
                lines.Add(detail.GetString() ?? "");
            }

            if (lines.Count > 0)
            {
                return $"{(int)status} {status}:\n  " + string.Join("\n  ", lines);
            }
        }
        catch (JsonException)
        {
            // Not JSON: fall through.
        }

        return $"{(int)status} {status}: {body}";
    }
}

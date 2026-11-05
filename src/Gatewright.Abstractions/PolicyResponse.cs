// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Abstractions;

/// <summary>A response produced directly by a policy (for example a mock or a CORS preflight answer).</summary>
public sealed class PolicyResponse
{
    private static readonly IReadOnlyDictionary<string, string> NoHeaders = new Dictionary<string, string>();

    /// <summary>Initializes a new instance of the <see cref="PolicyResponse"/> class.</summary>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="headers">Response headers, or <see langword="null"/> for none.</param>
    /// <param name="body">The response body.</param>
    /// <param name="contentType">The content type of the body, or <see langword="null"/> when there is no body.</param>
    public PolicyResponse(
        int statusCode,
        IReadOnlyDictionary<string, string>? headers = null,
        ReadOnlyMemory<byte> body = default,
        string? contentType = null)
    {
        if (statusCode is < 100 or > 999)
        {
            throw new ArgumentOutOfRangeException(nameof(statusCode), statusCode, "Status code must be between 100 and 999.");
        }

        StatusCode = statusCode;
        Headers = headers ?? NoHeaders;
        Body = body;
        ContentType = contentType;
    }

    /// <summary>Gets the HTTP status code.</summary>
    public int StatusCode { get; }

    /// <summary>Gets the response headers.</summary>
    public IReadOnlyDictionary<string, string> Headers { get; }

    /// <summary>Gets the response body.</summary>
    public ReadOnlyMemory<byte> Body { get; }

    /// <summary>Gets the content type of the body.</summary>
    public string? ContentType { get; }
}

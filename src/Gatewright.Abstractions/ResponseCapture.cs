// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Abstractions;

/// <summary>A copy of a backend response taken while it is streamed to the client.</summary>
public sealed class ResponseCapture
{
    /// <summary>Initializes a new instance of the <see cref="ResponseCapture"/> class.</summary>
    /// <param name="statusCode">The status code.</param>
    /// <param name="headers">The response headers (multiple values joined with a comma).</param>
    /// <param name="body">The full body.</param>
    public ResponseCapture(int statusCode, IReadOnlyDictionary<string, string> headers, ReadOnlyMemory<byte> body)
    {
        ArgumentNullException.ThrowIfNull(headers);
        StatusCode = statusCode;
        Headers = headers;
        Body = body;
    }

    /// <summary>Gets the status code.</summary>
    public int StatusCode { get; }

    /// <summary>Gets the response headers.</summary>
    public IReadOnlyDictionary<string, string> Headers { get; }

    /// <summary>Gets the full body.</summary>
    public ReadOnlyMemory<byte> Body { get; }
}

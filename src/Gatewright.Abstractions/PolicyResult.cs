// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Abstractions;

/// <summary>The outcome of a policy invocation.</summary>
public sealed class PolicyResult
{
    private PolicyResult(PolicyResultKind kind, int statusCode, string? code, string? message, PolicyResponse? response)
    {
        Kind = kind;
        StatusCode = statusCode;
        Code = code;
        Message = message;
        Response = response;
    }

    /// <summary>Gets the shared result meaning "continue with the next policy". It does not allocate.</summary>
    public static PolicyResult Continue { get; } = new(PolicyResultKind.Continue, 0, null, null, null);

    /// <summary>Gets the kind of result.</summary>
    public PolicyResultKind Kind { get; }

    /// <summary>Gets the HTTP status code for <see cref="PolicyResultKind.Reject"/> and <see cref="PolicyResultKind.Respond"/> results; 0 otherwise.</summary>
    public int StatusCode { get; }

    /// <summary>Gets the stable machine-readable error code of a rejection, or <see langword="null"/>.</summary>
    public string? Code { get; }

    /// <summary>Gets the human-readable message of a rejection, or <see langword="null"/>.</summary>
    public string? Message { get; }

    /// <summary>Gets the response of a <see cref="PolicyResultKind.Respond"/> result, or <see langword="null"/>.</summary>
    public PolicyResponse? Response { get; }

    /// <summary>Creates a result that rejects the request.</summary>
    /// <param name="statusCode">The HTTP status code returned to the client.</param>
    /// <param name="code">A stable machine-readable error code.</param>
    /// <param name="message">A human-readable message.</param>
    /// <returns>A rejection result.</returns>
    public static PolicyResult Reject(int statusCode, string code, string message)
    {
        if (statusCode is < 400 or > 599)
        {
            throw new ArgumentOutOfRangeException(nameof(statusCode), statusCode, "Rejection status code must be between 400 and 599.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(message);
        return new PolicyResult(PolicyResultKind.Reject, statusCode, code, message, null);
    }

    /// <summary>Creates a result that answers the request directly, without calling the backend.</summary>
    /// <param name="response">The response to return.</param>
    /// <returns>A respond result.</returns>
    public static PolicyResult Respond(PolicyResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return new PolicyResult(PolicyResultKind.Respond, response.StatusCode, null, null, response);
    }
}

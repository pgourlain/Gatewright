// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Abstractions;

namespace Gatewright.Core.Snapshots;

/// <summary>
/// The only policy of the pipeline of a blocked API (an API synchronized from Gateway API that has no product, an unknown product or no policy, ADR 0017).
/// It answers <c>503</c> with a stable code and never reveals the reason to the client; the reason is stored in the request variables for the logs.
/// </summary>
public sealed class BlockedApiPolicy : IPolicyInstance
{
    /// <summary>The problem code sent to the client.</summary>
    public const string Code = "api-not-configured";

    /// <summary>The request variable that holds the reason, read by the data plane for its logs.</summary>
    public const string ReasonVariable = "gw.blocked.reason";

    private readonly string _reason;

    /// <summary>Initializes a new instance of the <see cref="BlockedApiPolicy"/> class.</summary>
    /// <param name="reason">Why the API is blocked, such as <c>MissingProduct</c>.</param>
    public BlockedApiPolicy(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        _reason = reason;
    }

    /// <summary>Gets why the API is blocked.</summary>
    public string Reason => _reason;

    /// <inheritdoc/>
    public ValueTask<PolicyResult> InvokeAsync(GatewayContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Variables[ReasonVariable] = _reason;
        return ValueTask.FromResult(PolicyResult.Reject(503, Code, "This API is not configured."));
    }
}

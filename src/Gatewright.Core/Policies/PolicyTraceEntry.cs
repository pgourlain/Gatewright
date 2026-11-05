// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Core.Policies;

/// <summary>One policy invocation recorded for a traced request.</summary>
/// <param name="Policy">The policy type.</param>
/// <param name="Phase">The phase it ran in.</param>
/// <param name="DurationMs">How long it took, in milliseconds.</param>
/// <param name="Result">What it decided: <c>Continue</c>, <c>Reject</c>, <c>Respond</c>, <c>Error</c>, <c>Timeout</c> or <c>Aborted</c>.</param>
public sealed record PolicyTraceEntry(string Policy, string Phase, double DurationMs, string Result)
{
    /// <summary>The key under which a trace list (<c>List&lt;PolicyTraceEntry&gt;</c>) is stored in <c>GatewayContext.Variables</c> to request tracing.</summary>
    public const string VariableKey = "gw.trace";
}

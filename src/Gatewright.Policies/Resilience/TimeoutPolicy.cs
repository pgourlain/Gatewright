// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Abstractions;
using Gatewright.Policies.Internal;

namespace Gatewright.Policies.Resilience;

/// <summary>Configuration of the <c>timeout</c> policy.</summary>
public sealed class TimeoutConfig
{
    /// <summary>Gets or sets the maximum time, in seconds, the backend may take to answer.</summary>
    public double Seconds { get; set; }
}

/// <summary>The <c>timeout</c> policy: bounds the backend call and answers <c>504</c> when it is exceeded before the response started.</summary>
public sealed class TimeoutPolicy : PolicyBase<TimeoutConfig>
{
    private const string Schema = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["seconds"],
          "properties": { "seconds": { "type": "number", "exclusiveMinimum": 0 } }
        }
        """;

    /// <summary>Initializes a new instance of the <see cref="TimeoutPolicy"/> class.</summary>
    public TimeoutPolicy()
        : base("timeout", PolicyPhase.Backend, Schema)
    {
    }

    /// <inheritdoc/>
    protected override IPolicyInstance Create(TimeoutConfig config, PolicyCompilationContext context) =>
        new Instance(TimeSpan.FromSeconds(config.Seconds));

    private sealed class Instance(TimeSpan timeout) : IBackendPolicyInstance
    {
        public ValueTask<PolicyResult> InvokeAsync(GatewayContext context) => new(PolicyResult.Continue);

        public async ValueTask<PolicyResult> InvokeBackendAsync(GatewayContext context, BackendCall invokeBackend)
        {
            var http = context.HttpContext;
            var original = http.RequestAborted;
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(original);
            cts.CancelAfter(timeout);
            http.RequestAborted = cts.Token;
            PolicyResult result;
            try
            {
                result = await invokeBackend(context).ConfigureAwait(false);
            }
            finally
            {
                http.RequestAborted = original;
            }

            if (cts.IsCancellationRequested && !original.IsCancellationRequested && !http.Response.HasStarted)
            {
                return PolicyResult.Reject(504, "backend_timeout", "The backend did not answer in time.");
            }

            return result;
        }
    }
}

// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Globalization;
using Gatewright.Abstractions;
using Gatewright.Policies.Internal;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Forwarder;

namespace Gatewright.Policies.Resilience;

/// <summary>Configuration of the <c>circuitBreaker</c> policy.</summary>
public sealed class CircuitBreakerConfig
{
    /// <summary>Gets or sets how many consecutive failures open the circuit.</summary>
    public int FailureThreshold { get; set; } = 5;

    /// <summary>Gets or sets how long the circuit stays open before one trial call is allowed, in seconds.</summary>
    public int BreakSeconds { get; set; } = 30;

    /// <summary>Gets or sets the backend status codes counted as failures.</summary>
    public List<int> FailureStatuses { get; set; } = [502, 503, 504];
}

/// <summary>
/// The <c>circuitBreaker</c> policy: stops calling a failing backend (one breaker per cluster, shared by its operations) and answers <c>503</c>
/// immediately while the circuit is open. Declare it before <c>retry</c> and <c>timeout</c>.
/// </summary>
public sealed class CircuitBreakerPolicy : PolicyBase<CircuitBreakerConfig>
{
    private const string Schema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "failureThreshold": { "type": "integer", "minimum": 1 },
            "breakSeconds": { "type": "integer", "minimum": 1 },
            "failureStatuses": { "type": "array", "items": { "type": "integer", "minimum": 400, "maximum": 599 } }
          }
        }
        """;

    /// <summary>Initializes a new instance of the <see cref="CircuitBreakerPolicy"/> class.</summary>
    public CircuitBreakerPolicy()
        : base("circuitBreaker", PolicyPhase.Backend, Schema)
    {
    }

    /// <inheritdoc/>
    protected override IPolicyInstance Create(CircuitBreakerConfig config, PolicyCompilationContext context) => new Instance(
        config,
        context.Services.GetService<CircuitBreakerRegistry>() ?? new CircuitBreakerRegistry(),
        context.Services.GetService<TimeProvider>() ?? TimeProvider.System);

    private sealed class Instance(CircuitBreakerConfig config, CircuitBreakerRegistry registry, TimeProvider time) : IBackendPolicyInstance
    {
        private readonly HashSet<int> _failureStatuses = [.. config.FailureStatuses];

        public ValueTask<PolicyResult> InvokeAsync(GatewayContext context) => new(PolicyResult.Continue);

        public async ValueTask<PolicyResult> InvokeBackendAsync(GatewayContext context, BackendCall invokeBackend)
        {
            // One breaker per cluster: YARP clusters are named after their API.
            var breaker = registry.GetOrCreate(context.Api.Name, config.FailureThreshold, TimeSpan.FromSeconds(config.BreakSeconds), time);
            if (!breaker.TryAcquire(out var retryAfter))
            {
                context.HttpContext.Response.Headers["Retry-After"] =
                    Math.Max(1, (long)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                return PolicyResult.Reject(503, "circuit_open", "The backend is temporarily unavailable.");
            }

            PolicyResult result;
            try
            {
                result = await invokeBackend(context).ConfigureAwait(false);
            }
            catch
            {
                breaker.RecordFailure();
                throw;
            }

            if (context.HttpContext.RequestAborted.IsCancellationRequested)
            {
                // The client left: says nothing about the backend health. Release a half-open trial as a failure-neutral outcome.
                breaker.RecordSuccess();
                return result;
            }

            var failed = (result.Kind == PolicyResultKind.Reject && result.Code == "backend_timeout")
                || context.HttpContext.Features.Get<IForwarderErrorFeature>() is not null
                || _failureStatuses.Contains(context.HttpContext.Response.StatusCode);
            if (failed)
            {
                breaker.RecordFailure();
            }
            else
            {
                breaker.RecordSuccess();
            }

            return result;
        }
    }
}

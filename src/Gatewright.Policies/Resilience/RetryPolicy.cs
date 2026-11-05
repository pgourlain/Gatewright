// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Abstractions;
using Gatewright.Policies.Internal;
using Microsoft.AspNetCore.Http;
using Yarp.ReverseProxy.Forwarder;

namespace Gatewright.Policies.Resilience;

/// <summary>Configuration of the <c>retry</c> policy.</summary>
public sealed class RetryConfig
{
    /// <summary>Gets or sets the total number of attempts, the first one included.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Gets or sets the first backoff delay in milliseconds; it doubles at each retry, with jitter.</summary>
    public int BackoffMilliseconds { get; set; } = 100;

    /// <summary>Gets or sets the largest backoff delay in milliseconds.</summary>
    public int MaxBackoffMilliseconds { get; set; } = 2000;

    /// <summary>Gets or sets the retried HTTP methods. Only idempotent methods are accepted.</summary>
    public List<string> Methods { get; set; } = ["GET", "HEAD", "OPTIONS", "PUT", "DELETE"];

    /// <summary>Gets or sets the largest request body, in bytes, that is buffered to allow a retry. Requests with larger bodies are not retried.</summary>
    public int MaxBufferedBodyBytes { get; set; } = 1_048_576;
}

/// <summary>
/// The <c>retry</c> policy: retries idempotent requests, with exponential backoff and jitter, when the backend cannot be reached or times out
/// before any response bytes were sent to the client. Declare it before <c>timeout</c> so that each attempt gets its own timeout.
/// </summary>
public sealed class RetryPolicy : PolicyBase<RetryConfig>
{
    private const string Schema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "maxAttempts": { "type": "integer", "minimum": 1, "maximum": 10 },
            "backoffMilliseconds": { "type": "integer", "minimum": 0 },
            "maxBackoffMilliseconds": { "type": "integer", "minimum": 0 },
            "methods": { "type": "array", "minItems": 1, "items": { "enum": ["GET", "HEAD", "OPTIONS", "PUT", "DELETE"] } },
            "maxBufferedBodyBytes": { "type": "integer", "minimum": 0 }
          }
        }
        """;

    /// <summary>Initializes a new instance of the <see cref="RetryPolicy"/> class.</summary>
    public RetryPolicy()
        : base("retry", PolicyPhase.Backend, Schema)
    {
    }

    /// <inheritdoc/>
    protected override IPolicyInstance Create(RetryConfig config, PolicyCompilationContext context) =>
        new Instance(config, new HashSet<string>(config.Methods, StringComparer.OrdinalIgnoreCase));

    private sealed class Instance(RetryConfig config, HashSet<string> methods) : IBackendPolicyInstance
    {
        public ValueTask<PolicyResult> InvokeAsync(GatewayContext context) => new(PolicyResult.Continue);

        public async ValueTask<PolicyResult> InvokeBackendAsync(GatewayContext context, BackendCall invokeBackend)
        {
            var http = context.HttpContext;
            if (config.MaxAttempts <= 1 || !methods.Contains(http.Request.Method) || !PrepareBody(http.Request))
            {
                return await invokeBackend(context).ConfigureAwait(false);
            }

            var baseline = http.Response.Headers.ToDictionary(h => h.Key, h => h.Value);
            PolicyResult result;
            for (var attempt = 1; ; attempt++)
            {
                result = await invokeBackend(context).ConfigureAwait(false);
                if (attempt >= config.MaxAttempts || !IsRetryable(result, http))
                {
                    return result;
                }

                await Task.Delay(Backoff(attempt), http.RequestAborted).ConfigureAwait(false);
                Reset(http, baseline);
            }
        }

        private static bool IsRetryable(PolicyResult result, HttpContext http)
        {
            if (http.Response.HasStarted || http.RequestAborted.IsCancellationRequested)
            {
                return false;
            }

            if (result.Kind == PolicyResultKind.Reject && result.Code == "backend_timeout")
            {
                return true;
            }

            var error = http.Features.Get<IForwarderErrorFeature>()?.Error;
            return error is ForwarderError.Request or ForwarderError.RequestTimedOut or ForwarderError.ResponseHeaders;
        }

        private bool PrepareBody(HttpRequest request)
        {
            var hasBody = request.ContentLength is > 0 || request.Headers.ContainsKey("Transfer-Encoding");
            if (!hasBody)
            {
                return true;
            }

            if (request.ContentLength is not { } length || length > config.MaxBufferedBodyBytes)
            {
                return false;
            }

            request.EnableBuffering(bufferThreshold: 30 * 1024, bufferLimit: config.MaxBufferedBodyBytes);
            return true;
        }

        private TimeSpan Backoff(int attempt)
        {
            var exponential = config.BackoffMilliseconds * Math.Pow(2, attempt - 1);
            var capped = Math.Min(exponential, config.MaxBackoffMilliseconds);
            return TimeSpan.FromMilliseconds(capped * (0.5 + Random.Shared.NextDouble()));
        }

        private static void Reset(HttpContext http, Dictionary<string, Microsoft.Extensions.Primitives.StringValues> baseline)
        {
            if (http.Request.Body.CanSeek)
            {
                http.Request.Body.Position = 0;
            }

            http.Response.StatusCode = StatusCodes.Status200OK;
            http.Response.Headers.Clear();
            foreach (var (name, value) in baseline)
            {
                http.Response.Headers[name] = value;
            }

            http.Features.Set<IForwarderErrorFeature>(null);
        }
    }
}

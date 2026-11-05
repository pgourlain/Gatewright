// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Text;
using Gatewright.Abstractions;
using Gatewright.Policies.Internal;

namespace Gatewright.Policies.Mock;

/// <summary>The <c>mockResponse</c> policy: answers with a static response without calling the backend (useful for APIs under design).</summary>
public sealed class MockResponsePolicy : PolicyBase<MockResponseConfig>
{
    private const string Schema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "status": { "type": "integer", "minimum": 100, "maximum": 599 },
            "headers": { "type": "object", "additionalProperties": { "type": "string" } },
            "contentType": { "type": "string", "minLength": 1 },
            "body": { "type": "string" }
          }
        }
        """;

    /// <summary>Initializes a new instance of the <see cref="MockResponsePolicy"/> class.</summary>
    public MockResponsePolicy()
        : base("mockResponse", PolicyPhase.Inbound, Schema)
    {
    }

    /// <inheritdoc/>
    protected override IPolicyInstance Create(MockResponseConfig config, PolicyCompilationContext context)
    {
        // Built once: the same immutable response is returned to every request.
        var body = config.Body is null ? ReadOnlyMemory<byte>.Empty : Encoding.UTF8.GetBytes(config.Body);
        var response = new PolicyResponse(
            config.Status,
            new Dictionary<string, string>(config.Headers),
            body,
            body.IsEmpty ? null : config.ContentType);
        return new Instance(PolicyResult.Respond(response));
    }

    private sealed class Instance(PolicyResult result) : IPolicyInstance
    {
        public ValueTask<PolicyResult> InvokeAsync(GatewayContext context) => new(result);
    }
}

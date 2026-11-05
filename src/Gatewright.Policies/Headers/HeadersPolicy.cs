// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Abstractions;
using Gatewright.Policies.Internal;
using Microsoft.AspNetCore.Http;

namespace Gatewright.Policies.Headers;

/// <summary>The <c>headers</c> policy: set, append and remove headers on the request (Inbound) and on the response (Outbound).</summary>
public sealed class HeadersPolicy : PolicyBase<HeadersConfig>
{
    private const string Schema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "request": { "$ref": "#/$defs/operations" },
            "response": { "$ref": "#/$defs/operations" }
          },
          "$defs": {
            "operations": {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "set": { "type": "object", "additionalProperties": { "type": "string" } },
                "append": { "type": "object", "additionalProperties": { "type": "string" } },
                "remove": { "type": "array", "items": { "type": "string", "minLength": 1 } }
              }
            }
          }
        }
        """;

    /// <summary>Initializes a new instance of the <see cref="HeadersPolicy"/> class.</summary>
    public HeadersPolicy()
        : base("headers", PolicyPhase.Inbound | PolicyPhase.Outbound, Schema)
    {
    }

    /// <inheritdoc/>
    protected override IPolicyInstance Create(HeadersConfig config, PolicyCompilationContext context) =>
        new Instance(Compiled.From(config.Request), Compiled.From(config.Response));

    private sealed class Compiled
    {
        private readonly (string Name, VariableTemplate Value)[] _set;
        private readonly (string Name, VariableTemplate Value)[] _append;
        private readonly string[] _remove;

        private Compiled(
            (string Name, VariableTemplate Value)[] set,
            (string Name, VariableTemplate Value)[] append,
            string[] remove)
        {
            _set = set;
            _append = append;
            _remove = remove;
        }

        public static Compiled From(HeaderOperations? operations) => new(
            [.. (operations?.Set ?? []).Select(p => (p.Key, VariableTemplate.Parse(p.Value)))],
            [.. (operations?.Append ?? []).Select(p => (p.Key, VariableTemplate.Parse(p.Value)))],
            [.. operations?.Remove ?? []]);

        public void Apply(IHeaderDictionary headers, GatewayContext context)
        {
            foreach (var name in _remove)
            {
                headers.Remove(name);
            }

            foreach (var (name, value) in _set)
            {
                headers[name] = value.Evaluate(context);
            }

            foreach (var (name, value) in _append)
            {
                headers.Append(name, value.Evaluate(context));
            }
        }
    }

    private sealed class Instance(Compiled request, Compiled response) : IPolicyInstance
    {
        public ValueTask<PolicyResult> InvokeAsync(GatewayContext context)
        {
            switch (context.Phase)
            {
                case PolicyPhase.Inbound:
                    request.Apply(context.HttpContext.Request.Headers, context);
                    break;
                case PolicyPhase.Outbound:
                    response.Apply(context.HttpContext.Response.Headers, context);
                    break;
            }

            return new ValueTask<PolicyResult>(PolicyResult.Continue);
        }
    }
}

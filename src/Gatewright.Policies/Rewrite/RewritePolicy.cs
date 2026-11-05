// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Text.RegularExpressions;
using Gatewright.Abstractions;
using Gatewright.Policies.Internal;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.WebUtilities;

namespace Gatewright.Policies.Rewrite;

/// <summary>
/// The <c>rewrite</c> policy: rewrites the request path (regex, relative to the API base path) and query string before the request
/// is forwarded. The API base path is stripped afterwards as usual, so the backend sees the rewritten relative path.
/// </summary>
public sealed class RewritePolicy : PolicyBase<RewriteConfig>
{
    private const string Schema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "path": {
              "type": "object",
              "additionalProperties": false,
              "required": ["regex", "replacement"],
              "properties": {
                "regex": { "type": "string", "minLength": 1 },
                "replacement": { "type": "string" }
              }
            },
            "query": {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "set": { "type": "object", "additionalProperties": { "type": "string" } },
                "remove": { "type": "array", "items": { "type": "string", "minLength": 1 } }
              }
            }
          }
        }
        """;

    /// <summary>Initializes a new instance of the <see cref="RewritePolicy"/> class.</summary>
    public RewritePolicy()
        : base("rewrite", PolicyPhase.Inbound, Schema)
    {
    }

    /// <inheritdoc/>
    protected override IPolicyInstance Create(RewriteConfig config, PolicyCompilationContext context)
    {
        // The match timeout protects the gateway from catastrophic backtracking on hostile paths.
        var regex = config.Path is null
            ? null
            : new Regex(config.Path.Regex, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        return new Instance(
            regex,
            config.Path?.Replacement ?? "",
            [.. (config.Query?.Set ?? []).Select(p => (p.Key, VariableTemplate.Parse(p.Value)))],
            new HashSet<string>(config.Query?.Remove ?? [], StringComparer.OrdinalIgnoreCase));
    }

    private sealed class Instance(
        Regex? regex,
        string replacement,
        (string Name, VariableTemplate Value)[] set,
        HashSet<string> remove) : IPolicyInstance
    {
        public ValueTask<PolicyResult> InvokeAsync(GatewayContext context)
        {
            var request = context.HttpContext.Request;
            if (regex is not null)
            {
                RewritePath(context, request);
            }

            if (set.Length > 0 || remove.Count > 0)
            {
                RewriteQuery(context, request);
            }

            return new ValueTask<PolicyResult>(PolicyResult.Continue);
        }

        private void RewritePath(GatewayContext context, HttpRequest request)
        {
            var basePath = context.Api.BasePath.TrimEnd('/');
            var full = request.Path.Value ?? "/";
            var hasBase = basePath.Length > 0 && full.StartsWith(basePath, StringComparison.OrdinalIgnoreCase);
            var relative = hasBase ? full[basePath.Length..] : full;
            if (relative.Length == 0)
            {
                relative = "/";
            }

            string rewritten;
            try
            {
                rewritten = regex!.Replace(relative, replacement, 1);
            }
            catch (RegexMatchTimeoutException)
            {
                return;
            }

            if (rewritten.Length == 0 || rewritten[0] != '/')
            {
                rewritten = "/" + rewritten;
            }

            request.Path = new PathString((hasBase ? basePath : string.Empty) + rewritten);
        }

        private void RewriteQuery(GatewayContext context, HttpRequest request)
        {
            var parameters = new List<KeyValuePair<string, string>>();
            foreach (var (key, values) in QueryHelpers.ParseQuery(request.QueryString.Value))
            {
                if (remove.Contains(key) || Array.Exists(set, s => string.Equals(s.Name, key, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                foreach (var value in values)
                {
                    parameters.Add(new KeyValuePair<string, string>(key, value ?? string.Empty));
                }
            }

            foreach (var (name, value) in set)
            {
                parameters.Add(new KeyValuePair<string, string>(name, value.Evaluate(context)));
            }

            request.QueryString = new QueryBuilder(parameters).ToQueryString();
        }
    }
}

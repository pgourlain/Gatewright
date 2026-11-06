// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Text;
using Gatewright.Abstractions;

namespace Gatewright.Policies.Internal;

/// <summary>
/// A header or value template parsed once at compile time. Supported variables:
/// <c>{subscription.id}</c>, <c>{subscription.product}</c>, <c>{subscription.plan}</c>, <c>{claim:type}</c>,
/// <c>{api.name}</c>, <c>{operation.name}</c>, <c>{request.method}</c>, <c>{request.path}</c>, <c>{header:name}</c>, <c>{var:name}</c>.
/// Unknown or unresolved variables evaluate to an empty string.
/// </summary>
internal sealed class VariableTemplate
{
    private readonly Segment[] _segments;

    private VariableTemplate(Segment[] segments)
    {
        _segments = segments;
        IsStatic = segments.All(s => s.Variable is null);
    }

    public bool IsStatic { get; }

    public static VariableTemplate Parse(string template)
    {
        ArgumentNullException.ThrowIfNull(template);
        var segments = new List<Segment>();
        var literal = new StringBuilder();
        var i = 0;
        while (i < template.Length)
        {
            var c = template[i];
            if (c == '{')
            {
                var end = template.IndexOf('}', i + 1);
                if (end > i + 1)
                {
                    if (literal.Length > 0)
                    {
                        segments.Add(new Segment(literal.ToString(), null));
                        literal.Clear();
                    }

                    segments.Add(new Segment(null, template[(i + 1)..end]));
                    i = end + 1;
                    continue;
                }
            }

            literal.Append(c);
            i++;
        }

        if (literal.Length > 0)
        {
            segments.Add(new Segment(literal.ToString(), null));
        }

        return new VariableTemplate([.. segments]);
    }

    public string Evaluate(GatewayContext context)
    {
        if (_segments.Length == 1 && _segments[0].Variable is null)
        {
            return _segments[0].Literal!;
        }

        var builder = new StringBuilder();
        foreach (var segment in _segments)
        {
            builder.Append(segment.Variable is null ? segment.Literal : StripControlCharacters(Resolve(segment.Variable, context)));
        }

        return builder.ToString();
    }

    /// <summary>Removes CR, LF and other control characters from a resolved value so that attacker-controlled input (claims, request headers) cannot inject headers.</summary>
    internal static string StripControlCharacters(string value)
    {
        var index = value.AsSpan().IndexOfAnyExceptInRange('\u0020', '\u007E');
        if (index < 0)
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (c == '\t' || (c >= ' ' && c != '\u007F' && !char.IsControl(c)))
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private static string Resolve(string variable, GatewayContext context)
    {
        switch (variable)
        {
            case "subscription.id": return context.Subscription?.Id ?? "";
            case "subscription.product": return context.Subscription?.ProductId ?? "";
            case "subscription.plan": return context.Subscription?.Plan ?? "";
            case "api.name": return context.Api.Name;
            case "operation.name": return context.Operation?.Name ?? "";
            case "request.method": return context.HttpContext.Request.Method;
            case "request.path": return context.HttpContext.Request.Path.Value ?? "";
        }

        var colon = variable.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0)
        {
            return "";
        }

        var name = variable[(colon + 1)..];
        switch (variable[..colon])
        {
            case "claim": return context.Identity?.FindFirst(name)?.Value ?? "";
            case "header": return context.HttpContext.Request.Headers[name].ToString();
            case "var": return context.Variables.TryGetValue(name, out var value) ? value?.ToString() ?? "" : "";
            default: return "";
        }
    }

    private readonly record struct Segment(string? Literal, string? Variable);
}

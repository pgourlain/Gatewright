// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Policies.Rewrite;

/// <summary>Configuration of the <c>rewrite</c> policy.</summary>
public sealed class RewriteConfig
{
    /// <summary>Gets or sets the path rewrite, applied to the path relative to the API base path.</summary>
    public PathRewrite? Path { get; set; }

    /// <summary>Gets or sets the query-string changes.</summary>
    public QueryRewrite? Query { get; set; }
}

/// <summary>A regular-expression path rewrite.</summary>
public sealed class PathRewrite
{
    /// <summary>Gets or sets the .NET regular expression matched against the relative path (for example <c>^/items/(?&lt;id&gt;.*)$</c>).</summary>
    public string Regex { get; set; } = "";

    /// <summary>Gets or sets the replacement, which may use groups (<c>$1</c>, <c>${id}</c>).</summary>
    public string Replacement { get; set; } = "";
}

/// <summary>Query-string changes.</summary>
public sealed class QueryRewrite
{
    /// <summary>Gets or sets parameters replaced (or added); values may contain variables such as <c>{subscription.id}</c>.</summary>
    public Dictionary<string, string>? Set { get; set; }

    /// <summary>Gets or sets names of parameters to remove.</summary>
    public List<string>? Remove { get; set; }
}

// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Abstractions;

/// <summary>Identifies the API operation a request was routed to.</summary>
public sealed class OperationInfo
{
    /// <summary>Initializes a new instance of the <see cref="OperationInfo"/> class.</summary>
    /// <param name="name">The operation name, unique within its API.</param>
    /// <param name="method">The HTTP method.</param>
    /// <param name="pathTemplate">The path template relative to the API base path.</param>
    public OperationInfo(string name, string method, string pathTemplate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentNullException.ThrowIfNull(pathTemplate);
        Name = name;
        Method = method;
        PathTemplate = pathTemplate;
    }

    /// <summary>Gets the operation name.</summary>
    public string Name { get; }

    /// <summary>Gets the HTTP method.</summary>
    public string Method { get; }

    /// <summary>Gets the path template relative to the API base path.</summary>
    public string PathTemplate { get; }
}

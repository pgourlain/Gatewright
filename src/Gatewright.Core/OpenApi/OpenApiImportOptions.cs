// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Configuration;

namespace Gatewright.Core.OpenApi;

/// <summary>Choices made at import time. They take precedence over what the document declares in <c>x-gateway</c>.</summary>
public sealed record OpenApiImportOptions
{
    /// <summary>Gets the API name. Defaults to <c>x-gateway.name</c>, then a slug of <c>info.title</c>.</summary>
    public string? Name { get; init; }

    /// <summary>Gets the base path. Defaults to <c>x-gateway.basePath</c>, then the path of the first server, then <c>/&lt;name&gt;</c>.</summary>
    public string? BasePath { get; init; }

    /// <summary>Gets the backend. Defaults to <c>x-gateway.backend</c>; one of them is required.</summary>
    public BackendDefinition? Backend { get; init; }

    /// <summary>Gets the products the API belongs to. Defaults to <c>x-gateway.products</c>.</summary>
    public IReadOnlyList<string>? Products { get; init; }
}

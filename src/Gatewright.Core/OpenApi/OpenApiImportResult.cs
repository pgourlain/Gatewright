// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Configuration;

namespace Gatewright.Core.OpenApi;

/// <summary>The outcome of reading an OpenAPI document.</summary>
/// <param name="Api">The API described by the document, or <see langword="null"/> when there are errors.</param>
/// <param name="DisplayName">The title of the document.</param>
/// <param name="Products">The products requested by the document or the options.</param>
/// <param name="Warnings">Things that were ignored or defaulted.</param>
/// <param name="Errors">Why the document cannot be imported.</param>
public sealed record OpenApiImportResult(
    ApiDefinition? Api,
    string? DisplayName,
    IReadOnlyList<string> Products,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors)
{
    /// <summary>Gets a value indicating whether the document was read successfully.</summary>
    public bool IsSuccess => Errors.Count == 0 && Api is not null;
}

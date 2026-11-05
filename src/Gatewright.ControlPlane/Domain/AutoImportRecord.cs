// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.ControlPlane.Domain;

/// <summary>Remembers the last OpenAPI document imported automatically for a discovered service, to skip unchanged ones.</summary>
public sealed class AutoImportRecord
{
    /// <summary>Gets or sets the service reference (<c>namespace/name:port</c>), the primary key.</summary>
    public string ServiceRef { get; set; } = "";

    /// <summary>Gets or sets the hash of the last document successfully imported.</summary>
    public string DocumentHash { get; set; } = "";

    /// <summary>Gets or sets when the last import happened.</summary>
    public DateTimeOffset ImportedAt { get; set; }

    /// <summary>Gets or sets the last error, or <see langword="null"/> after a success.</summary>
    public string? LastError { get; set; }
}

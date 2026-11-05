// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Portal.Services;

/// <summary>Holds the access token of the signed-in user for the lifetime of a Blazor circuit; set when the circuit starts.</summary>
public sealed class TokenProvider
{
    /// <summary>Gets or sets the access token for the Admin API, or <see langword="null"/> in development mode.</summary>
    public string? AccessToken { get; set; }
}

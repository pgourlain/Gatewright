// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Policies.Jwt;

/// <summary>Downloads JWKS and OpenID Connect discovery documents. Only used from background refresh, never on the request path.</summary>
public interface IJwksFetcher
{
    /// <summary>Downloads a document as text.</summary>
    /// <param name="uri">The document address.</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <returns>The document content.</returns>
    Task<string> FetchAsync(Uri uri, CancellationToken cancellationToken);
}

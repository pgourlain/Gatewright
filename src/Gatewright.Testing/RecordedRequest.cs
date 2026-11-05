// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Testing;

/// <summary>A request received by a <see cref="SimulatedBackend"/>.</summary>
/// <param name="Method">The HTTP method.</param>
/// <param name="Path">The path.</param>
/// <param name="Query">The query string, including the leading <c>?</c>, or empty.</param>
/// <param name="Headers">The request headers, with lowercase names.</param>
/// <param name="Body">The request body as text.</param>
public sealed record RecordedRequest(string Method, string Path, string Query, IReadOnlyDictionary<string, string> Headers, string Body);

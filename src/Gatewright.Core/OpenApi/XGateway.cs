// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Core.OpenApi;

/// <summary>The <c>x-gateway</c> OpenAPI extension, version 1. See <c>docs/user/x-gateway.md</c>.</summary>
public static class XGateway
{
    /// <summary>The extension name, at document and operation level.</summary>
    public const string Extension = "x-gateway";

    /// <summary>The version marker extension (<c>x-gateway-version</c>) at document level.</summary>
    public const string VersionExtension = "x-gateway-version";

    /// <summary>The version of the extension this build understands.</summary>
    public const int CurrentVersion = 1;
}

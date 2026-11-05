// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.ControlPlane.Distribution;

/// <summary>Automatic OpenAPI import of discovered services, bound from <c>Kubernetes:AutoImport</c>.</summary>
public sealed class AutoImportOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Kubernetes:AutoImport";

    /// <summary>Gets or sets a value indicating whether services with an <c>openapi-path</c> annotation are imported automatically.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the environments a successful import is published to automatically. Empty (default): the API is only updated in the draft
    /// and a human publishes. Use it for <c>dev</c>, not for <c>prod</c>.
    /// </summary>
    public List<string> PublishTo { get; set; } = [];

    /// <summary>Gets or sets how often discovered services are checked, in seconds.</summary>
    public int PollSeconds { get; set; } = 10;

    /// <summary>
    /// Gets or sets the address template used to fetch documents: <c>{name}</c>, <c>{namespace}</c> and <c>{port}</c> are replaced.
    /// The default uses cluster DNS, which is stable across pod restarts.
    /// </summary>
    public string UrlTemplate { get; set; } = "http://{name}.{namespace}.svc:{port}";

    /// <summary>Gets or sets the HTTP timeout when fetching a document, in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 10;
}

// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Configuration;

namespace Gatewright.ControlPlane.Revisions;

/// <summary>
/// The declarative document describing a whole environment, for GitOps and for promotion between environments:
/// the gateway configuration plus the catalog details the configuration does not carry.
/// </summary>
public sealed record DeclarativeExport
{
    /// <summary>The current format identifier.</summary>
    public const string CurrentFormat = "gatewright/v1";

    /// <summary>Gets the format identifier; imports of other formats are refused.</summary>
    public string Format { get; init; } = CurrentFormat;

    /// <summary>Gets the gateway configuration.</summary>
    public GatewayConfig Config { get; init; } = new();

    /// <summary>Gets the display names of APIs, keyed by API name.</summary>
    public Dictionary<string, string> ApiDisplayNames { get; init; } = [];

    /// <summary>Gets the plans of products.</summary>
    public List<ExportedPlan> Plans { get; init; } = [];

    /// <summary>Gets the consumers.</summary>
    public List<ExportedConsumer> Consumers { get; init; } = [];

    /// <summary>Gets the consumer of each subscription, keyed by subscription id.</summary>
    public Dictionary<string, string> SubscriptionConsumers { get; init; } = [];
}

/// <summary>A plan in a <see cref="DeclarativeExport"/>.</summary>
/// <param name="Product">The product name.</param>
/// <param name="Name">The plan name.</param>
/// <param name="Description">The description.</param>
public sealed record ExportedPlan(string Product, string Name, string? Description);

/// <summary>A consumer in a <see cref="DeclarativeExport"/>.</summary>
/// <param name="Name">The consumer name.</param>
/// <param name="Contact">The contact address.</param>
public sealed record ExportedConsumer(string Name, string? Contact);

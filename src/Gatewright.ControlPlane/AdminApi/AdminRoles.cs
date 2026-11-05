// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.ControlPlane.AdminApi;

/// <summary>The roles of the Admin API and the authorization policies built from them. Admin includes Publisher, which includes Reader.</summary>
public static class AdminRoles
{
    /// <summary>Can change the configuration (APIs, products, subscriptions, policies).</summary>
    public const string Admin = "Admin";

    /// <summary>Can publish and roll back configurations to environments.</summary>
    public const string Publisher = "Publisher";

    /// <summary>Can read everything.</summary>
    public const string Reader = "Reader";

    /// <summary>Authorization policy requiring at least <see cref="Reader"/>.</summary>
    public const string ReaderPolicy = "Gatewright.Reader";

    /// <summary>Authorization policy requiring at least <see cref="Publisher"/>.</summary>
    public const string PublisherPolicy = "Gatewright.Publisher";

    /// <summary>Authorization policy requiring <see cref="Admin"/>.</summary>
    public const string AdminPolicy = "Gatewright.Admin";
}

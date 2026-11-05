// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.ControlPlane.AdminApi;

/// <summary>The labels of subscription keys: one primary and one secondary allow a rotation without downtime.</summary>
public static class KeyLabels
{
    /// <summary>The primary key.</summary>
    public const string Primary = "primary";

    /// <summary>The secondary key, used while rotating the primary.</summary>
    public const string Secondary = "secondary";
}

// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.ControlPlane.Domain;

/// <summary>Why a revision became the active one of an environment.</summary>
public enum PublicationKind
{
    /// <summary>A revision published for the first time or moving forward.</summary>
    Publish = 0,

    /// <summary>A previously published revision restored.</summary>
    Rollback = 1,
}

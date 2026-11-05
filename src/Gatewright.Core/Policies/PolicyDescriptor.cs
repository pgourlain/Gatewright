// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Text.Json;
using Gatewright.Abstractions;

namespace Gatewright.Core.Policies;

/// <summary>Describes an available policy type: used by the catalog endpoint, the portal and the CLI.</summary>
/// <param name="Type">The policy type name.</param>
/// <param name="Phases">The phases in which it can run.</param>
/// <param name="Schema">The JSON Schema of its configuration.</param>
public sealed record PolicyDescriptor(string Type, PolicyPhase Phases, JsonElement Schema);

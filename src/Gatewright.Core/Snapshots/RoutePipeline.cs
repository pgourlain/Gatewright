// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Abstractions;
using Gatewright.Core.Policies;

namespace Gatewright.Core.Snapshots;

/// <summary>What the data plane needs to run policies for one YARP route.</summary>
/// <param name="RouteId">The YARP route id.</param>
/// <param name="Api">The API the route belongs to.</param>
/// <param name="Operation">The operation, or <see langword="null"/> for an API-level route.</param>
/// <param name="Pipeline">The compiled policy pipeline.</param>
public sealed record RoutePipeline(string RouteId, ApiInfo Api, OperationInfo? Operation, PolicyPipeline Pipeline);

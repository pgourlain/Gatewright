# ADR 0005: D5 — Data plane keeps the last known good configuration

- Status: Accepted
- Date: 2026-11-04

## Context

A control plane outage must never interrupt traffic.

## Decision

The data plane keeps the last valid snapshot in memory and keeps serving it when the control plane is unreachable or a new snapshot is rejected.

## Consequences

Resilience at the cost of possible temporary configuration divergence, observable through applied-version metrics.

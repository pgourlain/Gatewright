# ADR 0001: D1 — Source of truth is the control plane database, not CRDs

- Status: Accepted
- Date: 2026-11-04

## Context

Configuration must be simple to declare for web technicians and independent of the Kubernetes version and of the Gateway API version installed on the cluster.

## Decision

The control plane database (PostgreSQL) is the single source of truth for APIs, policies, products, subscriptions, revisions and environments. No CRD is required.

## Consequences

Simple declaration through portal, OpenAPI import and code-first. Gatewright also works outside Kubernetes. GitOps relies on declarative export/import instead of CRDs.

# ADR 0002: D2 — Kubernetes is used for discovery only

- Status: Accepted
- Date: 2026-11-04

## Context

The cluster already knows which Services, endpoints and TLS secrets exist; Gatewright should reuse that without becoming a Kubernetes-only product.

## Decision

Kubernetes provides discovery only: Services annotated `gatewright.io/expose`, EndpointSlices and TLS Secrets. Only the control plane reads the Kubernetes API.

## Consequences

Works the same without Kubernetes (static destinations). The data plane needs no Kubernetes RBAC.

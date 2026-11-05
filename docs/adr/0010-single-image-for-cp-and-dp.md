# ADR 0010: D10 — Same image for control plane and data plane

- Status: Accepted
- Date: 2026-11-04

## Context

The control plane must know the schemas of custom policies to validate them.

## Decision

Control plane and data plane run the same image, including the customer's image when customized.

## Consequences

Consistent validation; one artifact to build, scan and deploy.

# Architecture Decision Records

- [0001](0001-source-of-truth-in-control-plane-database.md) — D1 — Source of truth is the control plane database, not CRDs
- [0002](0002-kubernetes-for-discovery-only.md) — D2 — Kubernetes is used for discovery only
- [0003](0003-control-plane-data-plane-split.md) — D3 — Control plane / data plane split in a single binary
- [0004](0004-grpc-distribution.md) — D4 — Configuration distribution over a custom gRPC protocol
- [0005](0005-last-known-good-config.md) — D5 — Data plane keeps the last known good configuration
- [0006](0006-phased-policy-pipeline-with-scope-inheritance.md) — D6 — Phased policy pipeline with scope inheritance
- [0007](0007-policies-compiled-at-load.md) — D7 — Policies are compiled when the configuration loads
- [0008](0008-redis-batched-rate-limiting.md) — D8 — Rate limiting with local buckets and Redis token batches
- [0009](0009-extensibility-through-distribution-model.md) — D9 — Extensibility through the distribution model
- [0010](0010-single-image-for-cp-and-dp.md) — D10 — Same image for control plane and data plane
- [0011](0011-tls-termination-flexibility.md) — D11 — TLS may be terminated upstream or by Gatewright
- [0012](0012-mit-license.md) — Choice of the MIT license

A decision that contradicts an accepted ADR requires a new ADR superseding it.

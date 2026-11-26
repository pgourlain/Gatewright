# ADR 0017: Kubernetes Gateway API as an optional configuration source

- Status: Accepted (2026-11-26, maintainer)
- Date: 2026-11-26

## Context

Gatewright is declared through the web portal, OpenAPI import and code-first packages. This suits
one team that operates everything. On a cluster shared by several teams, each team wants to declare
its own routes next to its workloads, with the Kubernetes RBAC it already uses and in GitOps
pipelines. The Kubernetes Gateway API (`HTTPRoute`) is the standard way to do that.

Constraints from CLAUDE.md §2:

- **D1**: the source of truth is the control plane database, not CRDs.
- **D2**: Kubernetes is used only for discovery, and Gatewright must also work outside Kubernetes.
- **Goal 2**: no dependency on CRDs or on the cluster's Gateway API version.

Precedent in the code base: the control plane already turns a discovered, annotated Service into an
API (`OpenApiAutoImportService`). It imports when the document hash changes and removes operations
the document no longer declares (`removeMissing`). Two gaps matter here:

- When a Service disappears, nothing removes its API. It stays in the database, orphaned.
- An API carries no marker of where it came from, so a manual edit is overwritten by the next import.

## Decision (proposed)

Gateway API becomes an **optional source** that feeds the database, in the same spirit as the
annotation discovery. It does not replace the database, and nothing requires it.

1. **Opt-in and isolated.** `GatewayApi:Enabled` (default `false`). Only the control plane reads
   `HTTPRoute`, and only the leader (Lease) synchronizes. The CRDs are needed only when the source is
   enabled. A cluster without them, or a non-Kubernetes install, is unaffected.
2. **Scope of the first version.** `HTTPRoute` (v1) only. Routes whose `parentRefs` do not target a
   Gateway of the Gatewright `GatewayClass` are ignored, so Gatewright coexists with other controllers.
   `Gateway` objects only select routes; listeners and TLS stay configured in Gatewright (D11).
3. **Mapping.** One `HTTPRoute` (`namespace/name`) becomes one API with `Source=GatewayApi` and
   `SourceRef=namespace/name` (the `uid` is stored for information). Rules and matches become
   operations, `backendRefs` become backend Service references (resolved through the existing
   EndpointSlice discovery), `hostnames` become hostnames. A feature that cannot be mapped is
   reported on the route, never silently ignored.
4. **Reconciliation, not events.** The state is level-triggered: a watch gives low latency, and a
   periodic full list repairs missed events. A change is detected with `metadata.generation`. Changes
   are coalesced over a short window so that a bulk update produces one revision, not one per route.
   Each sync creates a revision (`api.gatewayapi-sync`, actor `system:gatewayapi`), validated in full
   before any snapshot, then published to the configured environments (`PublishTo`).
5. **Modification.** The desired API is recomputed from the route and diffed with the stored one.
   No difference means no revision. An invalid result is refused by validation, the data plane keeps
   its last known good configuration (D5), and the error is reported.
6. **Deletion.** A `Deleted` event removes the API in a new revision, so rollback stays possible.
   A missed event is repaired by the periodic list: an API with `Source=GatewayApi` whose `SourceRef`
   is not listed any more is removed. Safeguards against mass deletion:
   - reconcile only after a complete, successful list (never after an API error or an unexplained
     empty result);
   - require absence over several consecutive reconciliations or a grace delay;
   - cap the deletions per cycle, beyond which the sync stops and raises an alert.
   No finalizer is added to user resources: a control plane outage must never block their deletion.
7. **Ownership.** An API from this source is read-only in the Admin API and the portal. A "detach"
   action turns it into a manual API and stops its synchronization. This needs a new field and an EF
   migration (SQLite and PostgreSQL).
8. **Policies stay in the database.** Kubernetes owns the routing shape (hostnames, matches,
   backends, filters). The database owns the policies, and the sync never changes or removes them
   (they are removed only with the API). A route joins a product with the annotation
   `gatewright.io/product`, and inherits the product's policies (D6).
9. **Fail closed.** An API from this source with no product, an unknown product, or a product with no
   effective policy is **blocked**. It stays in the snapshot as a real route and answers `503` with
   `application/problem+json`, a stable code (`api-not-configured`) and the request id. The detailed
   reason (`MissingProduct`, `ProductNotFound`, `NoPolicy`) is never sent to the client: it goes to
   logs correlated by request id, a `blocked_requests{reason}` metric, the Admin API and, if accepted,
   the route status. The state is computed when the snapshot is built, not when the route is
   imported, so it clears by itself once the product gets policies. It is compiled at load time (D7),
   with no per-request cost. This rule applies **only** to APIs from this source; every other API
   keeps its current behaviour.

## Consequences

- Teams declare routes with their usual tooling, and Gatewright stays usable without Kubernetes.
- D1 holds (the database is still the truth), but D2 widens from discovery to a declarative source
  of routing shape. This is the reason for this ADR.
- A new field on the API entity and a new migration are required.
- Rollback to an older revision can be undone by the next reconciliation (see open questions).
- The auto-import gap (orphan APIs when a Service disappears) is the same problem as item 6 and is
  tracked in the Backlog so both can share the ownership and reconciliation code.

## Alternatives rejected

- **CRDs as the source of truth**: contradicts D1 and goal 2.
- **Gateway API as the only model**: it cannot express the policy pipeline and quotas, and it would
  tie the product to a cluster feature.
- **Finalizers on routes**: a control plane outage would block deletions.
- **Fail open when no policy is attached**: an API would be public by accident.

## Open questions

1. **"No policy":** this ADR blocks an API whose product has no effective policy. Should it also
   require an authentication policy (`jwt` or `apiKey`), with public APIs served by an admin-created
   "public" product? Safer, but one more rule.
2. **Status write-back:** the route status (`Accepted`, `ResolvedRefs`) is the best feedback for a
   team without portal access, but it needs a patch right on `httproutes/status`, which relaxes
   "the control plane only reads the Kubernetes API" (CLAUDE.md §6).
3. **Environment:** which environment does a route publish to? `PublishTo` as the auto-import does,
   or a mapping from the parent Gateway to a Gatewright environment.
4. **Rollback:** a "pause sync" per environment or per API, so that a rollback is not overwritten.
5. **Filters:** which `HTTPRoute` filters are supported in the first version, and the equivalent
   built-in policies.
6. **Out of scope here:** multi-tenancy (teams, scoped roles), `GRPCRoute` / `TLSRoute`, and deriving
   listeners from `Gateway` objects.

## Decisions taken at implementation (2026-11-26)

The open questions above were settled as follows, the simplest option each time:

1. **No policy**: an API is blocked when it has no product, an unknown product, or no policy at product,
   API or operation scope (global and gateway policies apply to everything and do not count). Requiring
   an authentication policy is not enforced.
2. **Status write-back**: implemented, but off by default (`GatewayApi:WriteStatus=false`). The Helm chart
   grants the `httproutes/status` patch right only when it is enabled.
3. **Environment**: the routes publish to `GatewayApi:PublishTo`, as the auto-import does with its own list.
4. **Rollback**: no "pause sync" yet. A rollback of an API from this source is overwritten by the next
   reconciliation; the documentation says so.
5. **Filters**: none in this version. A route that uses a filter, a header or query match, a regular
   expression path, several distinct backends or a weight is reported `Accepted=False`
   (`UnsupportedValue`) and not published, never half-mapped.
6. **Hostnames**: the data plane had no host matching, so `hostnames` could not be mapped. An optional
   `hosts` list is added to the API definition (additive, `gatewright/v1` stays) and becomes the YARP route
   `Hosts`.
7. **Product reference**: the requested product name is stored with the API and resolved when the
   configuration is exported, so an unknown product never makes a whole configuration invalid, and an API
   unblocks itself when its product appears.
8. **Gateway selection**: the control plane watches `Gateway` objects and keeps the routes whose parent is
   a Gateway with `spec.gatewayClassName` equal to `GatewayApi:GatewayClassName` (default `gatewright`).

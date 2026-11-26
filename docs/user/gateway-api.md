# Declaring APIs with Kubernetes Gateway API

On a cluster shared by several teams, each team can declare its routes with a standard `HTTPRoute` next to its workloads, with the Kubernetes RBAC it already uses and its usual GitOps tooling. Gatewright reads those routes and feeds its database with them. This is optional: the database stays the source of truth ([ADR 0017](../adr/0017-gateway-api-optional-source.md)), nothing requires the Gateway API, and Gatewright still works outside Kubernetes.

## What you need

- `Kubernetes:Enabled=true` (see [Declaring APIs](declaring-apis.md#4-kubernetes-auto-discovery)) and a control plane that uses the database (mode `ControlPlane`, or `All` with the `Database` config source).
- The Gateway API CRDs (`gateway.networking.k8s.io/v1`, standard channel: `Gateway` and `HTTPRoute`) installed in the cluster. Without them the watcher logs a warning and retries.
- A `Gateway` object whose `spec.gatewayClassName` equals `GatewayApi:GatewayClassName` (default `gatewright`). The platform team creates it; Gatewright does not create `Gateway` or `GatewayClass` objects, and listeners and TLS stay configured in Gatewright.
- `GatewayApi:Enabled=true`. With the Helm chart: `--set gatewayApi.enabled=true`, which also grants the read rights on `gateways` and `httproutes`.

## Options

Environment variables use `__` for the section separator, for example `GatewayApi__Enabled`.

| Option | Default | Meaning |
|---|---|---|
| `GatewayApi:Enabled` | `false` | Reads `Gateway` and `HTTPRoute` objects. |
| `GatewayApi:GatewayClassName` | `gatewright` | Class of the Gateways whose routes are served. |
| `GatewayApi:PublishTo` | empty | Environments a synchronized change is published to. Empty: a draft revision only. |
| `GatewayApi:DebounceMilliseconds` | `500` | How long changes are gathered before one reconciliation. |
| `GatewayApi:ReconcileSeconds` | `30` | Period of the full reconciliation (repairs missed events and counts absences). |
| `GatewayApi:DeletionGraceReconciliations` | `3` | Consecutive reconciliations that must miss a route before its API is deleted. |
| `GatewayApi:MaxDeletionsPerCycle` | `5` | Most APIs deleted in one reconciliation. Beyond it nothing is deleted and an error is logged. |
| `GatewayApi:WriteStatus` | `false` | Writes the outcome to the route status (needs the `httproutes/status` patch right). |

Only the control plane leader reads the cluster and writes to the database.

## What a route becomes

A route attached to a served Gateway (`parentRefs` with the Gateway kind, in its own namespace unless the parent names another one) becomes one API:

- **Name**: `<namespace>.<name>` of the route.
- **Hosts**: `spec.hostnames`. No hostname means any host.
- **Backend**: the single Service referenced by `backendRefs`, as `<namespace>/<service>:<port>`. The Service needs no `gatewright.io/expose` annotation; its endpoints are discovered because a served route references it.
- **Base path and operations**: the base path is the longest common path prefix of the matches. One `PathPrefix` match without a method is a catch-all API under that path. Otherwise each match becomes operations: `Exact` is an exact path, `PathPrefix` also matches everything below it, and a match without a method expands to `GET`, `HEAD`, `POST`, `PUT`, `PATCH`, `DELETE` and `OPTIONS`. Request matching then follows the usual YARP route precedence.
- **Product**: the annotation `gatewright.io/product` on the route (the prefix is `Kubernetes:AnnotationPrefix`).

### Supported subset

A route that uses anything else is **not applied** and is reported on its status (`Accepted=False`, `UnsupportedValue`); it is never half-mapped.

| Not supported | Why it matters |
|---|---|
| Filters (rule or backend level), `timeouts` | Policies live in Gatewright, not in the route. |
| Header and query parameter matches, `RegularExpression` paths, the `CONNECT` method | No equivalent in the route model. |
| Several distinct backends, weights of `0` | One API has one backend. The same Service in several rules is fine. |
| A backend in another namespace, a kind other than `Service` | `ReferenceGrant` is not handled. |

## Blocked until configured

Kubernetes owns the routing shape; Gatewright owns the policies. A route carries no policy, so an API from this source **serves nothing until it has a product with policies**. It is blocked, and answers:

```
HTTP/1.1 503 Service Unavailable
Content-Type: application/problem+json

{"type":"api-not-configured","title":"This API is not configured.","status":503,"instance":"<trace id>"}
```

The client never sees the cause. It is available to the operator:

| Cause | Meaning | Fix |
|---|---|---|
| `MissingProduct` | No `gatewright.io/product` annotation. | Add it. |
| `ProductNotFound` | The product does not exist. | Create the product or fix the name. |
| `NoPolicy` | The product, the API and its operations have no policy. Global and gateway policies apply to everything and do not count. | Add policies to the product. |

Where to look, starting from the `instance` of the response: the data plane log (`Route … is blocked (<cause>)`), the `gatewright.rejections` metric with the code `api-not-configured`, and, with `GatewayApi:WriteStatus`, the route status (`kubectl describe httproute`). The state is computed when the configuration is built, so the API serves again as soon as its product gets a policy; the route does not need to change. Only APIs from this source are blocked this way; APIs declared by hand keep their behaviour.

## Changes, deletions and safeguards

- **Modification**: a changed route (`metadata.generation`) updates its API in one revision (`api.gatewayapi-sync`, actor `system:gatewayapi`). Unchanged routes cost nothing. A route that becomes unsupported or conflicting keeps its last good API serving, and the problem is reported.
- **Deletion**: when a route disappears its API is deleted, in a new revision, after `DeletionGraceReconciliations` consecutive reconciliations that did not find it. A missed watch event is repaired the same way. Nothing is deleted after an API server error, because a reconciliation only runs after a complete, successful listing. If more than `MaxDeletionsPerCycle` APIs would be deleted at once, none is and an error is logged.
- **Conflicts**: a route whose API name, or hosts and base path, match another API is refused (`Accepted=False`, reason `Conflict`). Two APIs may share a base path on different hosts.
- **Publication**: with an empty `GatewayApi:PublishTo`, changes wait as a draft revision until someone publishes them. Several changes of one cycle make one revision.

## Ownership

An API from this source is **read-only** in the Admin API and the portal: changing or deleting it, or one of its operations, answers `409`, and an OpenAPI import over it is refused (`400`). Change the route instead. `POST /admin/v1/apis/{name}/detach` turns it into an API declared by hand and stops its synchronization; its requested product stays linked so inherited policies keep applying. `GET /admin/v1/apis` shows the `source` and the `hosts` of each API.

A rollback of an environment is not protected: the next synchronized change creates a revision from the current database and, when `PublishTo` is set, publishes it over the rollback.

## Route status

With `GatewayApi:WriteStatus=true`, Gatewright writes to `status.parents` of each route, for each served parent, under the controller name `gatewright.io/gateway-controller` (entries of other controllers are kept):

| Condition | Meaning |
|---|---|
| `Accepted` | `True` when the route is applied, `False` with a reason (`UnsupportedValue`, `Conflict`) and a message otherwise. |
| `gatewright.io/Configured` | `True` when the API serves, `False` with the cause above when it answers `503`. |

`ResolvedRefs` is not written: Gatewright does not check that the backend Service exists.

## Kubernetes rights

The chart adds them only when `gatewayApi.enabled` is set: `get`, `list`, `watch` on `gateways` and `httproutes` (cluster-wide, like the discovery rights) and, with `gatewayApi.writeStatus`, `patch` on `httproutes/status`. The data plane has no Kubernetes rights.

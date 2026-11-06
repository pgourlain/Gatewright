#!/usr/bin/env bash
# Copyright (c) Gatewright contributors.
# SPDX-License-Identifier: MIT
#
# Phase 14.2 targets, validated on a real (if undersized) Kubernetes cluster: scale-out,
# scale-in and a rolling update of the data plane produce zero request errors, and a published
# config change reaches every one of 50 data-plane pods within the 5 s target (D4/D5, 6.5's
# config_version_applied convergence). Requires kind, kubectl, helm, dotnet and Docker.
#
#   kind create cluster --name gatewright && ./scripts/k8s-scale-test.sh
#
# Resource requests/limits are deliberately tiny (--set below): this validates *behavior*
# (no dropped requests, convergence time) on a small dev cluster, not per-pod throughput
# (that is tests/load's job, at 14.2's 5,000 RPS / 2 vCPU target). Needs ~1 vCPU / ~1 GiB free
# for 50 data-plane pods at these overrides; lower DP_REPLICAS on a smaller machine.
# KEEP_NAMESPACE=1 skips the final cleanup, to inspect a failed run.
set -euo pipefail

cd "$(dirname "$0")/.."

cluster="${GATEWRIGHT_PERF_CLUSTER:-gatewright-test}"
context="kind-$cluster"
namespace="gatewright-perf"
release="gatewright-perf"
image_tag="perf-$(date +%s)"
dp_replicas="${DP_REPLICAS:-50}"
diagnostics_token="perf-diagnostics"
admin_port=18180
dp_port=18280
converge_timeout="${CONVERGE_TIMEOUT_SECONDS:-5}"

log() { echo "==> $*"; }

for tool in kind kubectl helm dotnet docker curl; do
  command -v "$tool" >/dev/null || { echo "Missing required tool: $tool" >&2; exit 1; }
done

if ! kind get clusters | grep -qx "$cluster"; then
  log "Creating kind cluster '$cluster'"
  kind create cluster --name "$cluster" --wait 180s
fi

pids=()
cleanup() {
  for pid in "${pids[@]:-}"; do kill "$pid" 2>/dev/null || true; done
  if [[ "${KEEP_NAMESPACE:-0}" == "1" ]]; then
    log "KEEP_NAMESPACE=1: leaving namespace '$namespace' for inspection"
    return
  fi
  log "Cleaning up"
  helm --kube-context "$context" uninstall "$release" --namespace "$namespace" >/dev/null 2>&1 || true
  kubectl --context "$context" delete namespace "$namespace" --wait=false >/dev/null 2>&1 || true
  docker rmi "gatewright-host:$image_tag" "gatewright-sample-orders:$image_tag" >/dev/null 2>&1 || true
}
trap cleanup EXIT

log "Building gatewright-host:$image_tag and the backend-orders sample image"
dotnet publish src/Gatewright.Host -c Release -t:PublishContainer \
  -p:ContainerImageTag="$image_tag" -p:MinVerSkip=true >/dev/null
docker build -t "gatewright-sample-orders:$image_tag" -f Dockerfile \
  --build-arg PROJECT=samples/backend-orders --build-arg ASSEMBLY=backend-orders . >/dev/null

log "Loading images into '$cluster'"
kind load docker-image "gatewright-host:$image_tag" --name "$cluster"
kind load docker-image "gatewright-sample-orders:$image_tag" --name "$cluster"

log "Creating namespace '$namespace'"
kubectl --context "$context" create namespace "$namespace" --dry-run=client -o yaml | kubectl --context "$context" apply -f - >/dev/null

log "Deploying the backend-orders sample"
kubectl --context "$context" --namespace "$namespace" apply -f - <<YAML >/dev/null
apiVersion: apps/v1
kind: Deployment
metadata:
  name: backend-orders
spec:
  replicas: 1
  selector:
    matchLabels: { app: backend-orders }
  template:
    metadata:
      labels: { app: backend-orders }
    spec:
      containers:
        - name: backend-orders
          image: gatewright-sample-orders:$image_tag
          imagePullPolicy: Never
          ports:
            - containerPort: 8080
---
apiVersion: v1
kind: Service
metadata:
  name: backend-orders
  annotations:
    gatewright.io/expose: "true"
spec:
  selector: { app: backend-orders }
  ports:
    - port: 80
      targetPort: 8080
YAML
kubectl --context "$context" --namespace "$namespace" rollout status deployment/backend-orders --timeout=120s >/dev/null

log "Installing the chart ($release, gateway.mode=separate, dataPlane.replicaCount=1 to start)"
helm --kube-context "$context" install "$release" deploy/helm/gatewright \
  --namespace "$namespace" \
  --set gateway.mode=separate \
  --set gateway.diagnosticsToken="$diagnostics_token" \
  --set image.repository=gatewright-host \
  --set image.tag="$image_tag" \
  --set image.pullPolicy=Never \
  --set controlPlane.auth.developmentBypass=true \
  --set controlPlane.service.type=ClusterIP \
  --set dataPlane.replicaCount=1 \
  --set dataPlane.service.type=ClusterIP \
  --set dataPlane.podDisruptionBudget.enabled=false \
  --set dataPlane.resources.requests.cpu=10m \
  --set dataPlane.resources.requests.memory=32Mi \
  --set dataPlane.resources.limits.memory=128Mi \
  --set controlPlane.resources.requests.cpu=10m >/dev/null

kubectl --context "$context" --namespace "$namespace" wait --for=condition=ready pod \
  -l app.kubernetes.io/component=controlplane --timeout=180s >/dev/null

cp_service="$(kubectl --context "$context" --namespace "$namespace" get svc -l app.kubernetes.io/component=controlplane -o jsonpath='{.items[0].metadata.name}')"
dp_service="$(kubectl --context "$context" --namespace "$namespace" get svc -l app.kubernetes.io/component=dataplane -o jsonpath='{.items[0].metadata.name}')"
dp_deployment="$(kubectl --context "$context" --namespace "$namespace" get deploy -l app.kubernetes.io/component=dataplane -o jsonpath='{.items[0].metadata.name}')"

kubectl --context "$context" --namespace "$namespace" port-forward "svc/$cp_service" "$admin_port:8081" >/tmp/gatewright-perf-admin-pf.log 2>&1 &
pids+=($!)
for _ in $(seq 1 30); do curl -sf -o /dev/null "http://localhost:$admin_port/readyz" 2>/dev/null && break; sleep 1; done

log "Publishing the orders API (before the data plane is ready: readiness needs a first snapshot, D5)"
curl -sf -X POST "http://localhost:$admin_port/admin/v1/apis" -H 'content-type: application/json' \
  -d "{\"name\":\"orders\",\"basePath\":\"/orders\",\"backend\":{\"service\":\"$namespace/backend-orders:80\"}}" >/dev/null
curl -sf -X POST "http://localhost:$admin_port/admin/v1/environments/dev/publications" -H 'content-type: application/json' -d '{}' >/dev/null

# The data-plane Service only gets an endpoint once its pod passes readiness, and readiness only
# turns true after the snapshot above is distributed - port-forward straight to the pod to bootstrap.
dp_pod="$(kubectl --context "$context" --namespace "$namespace" get pods -l app.kubernetes.io/component=dataplane -o jsonpath='{.items[0].metadata.name}')"
kubectl --context "$context" --namespace "$namespace" port-forward "pod/$dp_pod" "$dp_port:8080" >/tmp/gatewright-perf-dp-bootstrap-pf.log 2>&1 &
bootstrap_pf_pid=$!
for _ in $(seq 1 60); do
  code="$(curl -s -o /dev/null -w '%{http_code}' "http://localhost:$dp_port/readyz" 2>/dev/null || echo 000)"
  [[ "$code" == "200" ]] && break
  sleep 1
done
kill "$bootstrap_pf_pid" 2>/dev/null || true
[[ "$code" == "200" ]] || { echo "Data plane never became ready (got $code)" >&2; exit 1; }
kubectl --context "$context" --namespace "$namespace" wait --for=condition=ready pod \
  -l app.kubernetes.io/component=dataplane --timeout=60s >/dev/null

kubectl --context "$context" --namespace "$namespace" port-forward "svc/$dp_service" "$dp_port:8080" >/tmp/gatewright-perf-dp-pf.log 2>&1 &
pids+=($!)
for _ in $(seq 1 30); do curl -sf -o /dev/null "http://localhost:$dp_port/healthz" 2>/dev/null && break; sleep 1; done
code="$(curl -s -o /dev/null -w '%{http_code}' "http://localhost:$dp_port/orders/1")"
[[ "$code" == "200" ]] || { echo "Baseline /orders/1 never returned 200 through the Service (got $code)" >&2; exit 1; }

# Continuous traffic while we scale/upgrade the data plane, generated *from inside the cluster*
# against the Service's DNS name: a `kubectl port-forward svc/...` pins its tunnel to whichever
# single pod backed the Service when the tunnel was opened (observed directly: it keeps working
# through scale-out and scale-in because the original pod happens to survive both, by Kubernetes'
# own scale-down ordering, but breaks solid for the rest of the test the moment that pod is ever
# replaced - exactly what a rolling update does to it) and so cannot tell a real dropped request
# from its own tunnel going stale. A pod hitting the Service by name goes through the real
# kube-proxy load balancing every other in-cluster client uses, immune to that. Every non-2xx
# response (a connection error folds into "000") is one line in the error log.
error_log="$(mktemp)"
kubectl --context "$context" --namespace "$namespace" apply -f - <<YAML >/dev/null
apiVersion: v1
kind: Pod
metadata:
  name: traffic-gen
spec:
  restartPolicy: Never
  containers:
    - name: traffic-gen
      image: docker.io/curlimages/curl:8.11.1
      command:
        - sh
        - -c
        - |
          while true; do
            code="\$(curl -s -o /dev/null -w '%{http_code}' --max-time 2 "http://$dp_service:8080/orders/1" || echo 000)"
            case "\$code" in
              2*) ;;
              *) echo "\$(date +%s.%N) \$code" ;;
            esac
            sleep 0.05
          done
YAML
kubectl --context "$context" --namespace "$namespace" wait --for=condition=ready pod/traffic-gen --timeout=60s >/dev/null
kubectl --context "$context" --namespace "$namespace" logs -f pod/traffic-gen >"$error_log" 2>/dev/null &
traffic_pid=$!
pids+=("$traffic_pid")

log "Scale-out: dataPlane $dp_deployment 1 -> $dp_replicas replicas, under traffic"
kubectl --context "$context" --namespace "$namespace" scale deployment "$dp_deployment" --replicas="$dp_replicas" >/dev/null
for _ in $(seq 1 120); do
  ready="$(kubectl --context "$context" --namespace "$namespace" get deployment "$dp_deployment" -o jsonpath='{.status.readyReplicas}')"
  [[ "${ready:-0}" == "$dp_replicas" ]] && break
  sleep 1
done
[[ "${ready:-0}" == "$dp_replicas" ]] || { echo "Scale-out never reached $dp_replicas ready replicas (stuck at ${ready:-0}; likely insufficient cluster CPU/memory for this many pods)" >&2; exit 1; }
errors_after_scale_out="$(wc -l <"$error_log")"
log "Scale-out done: $errors_after_scale_out error(s) observed so far"

log "Measuring config propagation to $dp_replicas pods"
pod_names=()
while IFS= read -r line; do
  [[ -n "$line" ]] && pod_names+=("$line")
done < <(kubectl --context "$context" --namespace "$namespace" get pods -l app.kubernetes.io/component=dataplane -o jsonpath='{range .items[*]}{.metadata.name}{"\n"}{end}')
pf_pids=()
pod_ports=()
port=19001
for pod in "${pod_names[@]}"; do
  kubectl --context "$context" --namespace "$namespace" port-forward "pod/$pod" "$port:8080" >/tmp/gatewright-perf-pod-pf-"$port".log 2>&1 &
  pf_pids+=($!)
  pod_ports+=("$port")
  port=$((port + 1))
done
pids+=("${pf_pids[@]}")

config_version() {
  curl -s --max-time 1 -H "Authorization: Bearer $diagnostics_token" "http://localhost:$1/gw/config" 2>/dev/null \
    | grep -o '"version":[0-9]*' | cut -d: -f2 || true
}

before_version=""
for _ in $(seq 1 60); do
  before_version="$(config_version "${pod_ports[0]}")"
  [[ -n "$before_version" ]] && break
  sleep 0.5
done
[[ -n "$before_version" ]] || { echo "Could not read the baseline config version through the pod port-forwards" >&2; exit 1; }

curl -sf -X PUT "http://localhost:$admin_port/admin/v1/apis/orders" -H 'content-type: application/json' \
  -d "{\"name\":\"orders\",\"basePath\":\"/orders\",\"backend\":{\"service\":\"$namespace/backend-orders:80\"},\"policies\":{\"outbound\":[{\"type\":\"headers\",\"config\":{\"response\":{\"set\":{\"x-perf-marker\":\"1\"}}}}]}}" >/dev/null
curl -sf -X POST "http://localhost:$admin_port/admin/v1/environments/dev/publications" -H 'content-type: application/json' -d '{}' >/dev/null
publish_time="$(date +%s.%N)"

converged_at=""
for _ in $(seq 1 100); do
  all_done=1
  for p in "${pod_ports[@]}"; do
    v="$(config_version "$p")"
    if [[ -z "$v" || "$v" == "$before_version" ]]; then
      all_done=0
      break
    fi
  done
  if [[ "$all_done" == 1 ]]; then
    converged_at="$(date +%s.%N)"
    break
  fi
  sleep 0.1
done

for pid in "${pf_pids[@]}"; do kill "$pid" 2>/dev/null || true; done

[[ -n "$converged_at" ]] || { echo "Config change did not reach all $dp_replicas pods within the poll window" >&2; exit 1; }
elapsed="$(echo "$converged_at - $publish_time" | bc)"
log "Config propagation to $dp_replicas pods: ${elapsed}s (target: < ${converge_timeout}s)"
if (($(echo "$elapsed > $converge_timeout" | bc))); then
  echo "Config propagation took ${elapsed}s, over the ${converge_timeout}s target for $dp_replicas pods" >&2
  exit 1
fi

log "Scale-in: dataPlane $dp_deployment $dp_replicas -> 1 replica, under traffic"
kubectl --context "$context" --namespace "$namespace" scale deployment "$dp_deployment" --replicas=1 >/dev/null
for _ in $(seq 1 120); do
  ready="$(kubectl --context "$context" --namespace "$namespace" get deployment "$dp_deployment" -o jsonpath='{.status.readyReplicas}')"
  [[ "${ready:-0}" == "1" ]] && break
  sleep 1
done
errors_after_scale_in="$(wc -l <"$error_log")"
log "Scale-in done: $((errors_after_scale_in - errors_after_scale_out)) error(s) during scale-in"

log "Rolling update: dataPlane image tag bump at 5 replicas, under traffic"
kubectl --context "$context" --namespace "$namespace" scale deployment "$dp_deployment" --replicas=5 >/dev/null
kubectl --context "$context" --namespace "$namespace" rollout status deployment "$dp_deployment" --timeout=120s >/dev/null
helm --kube-context "$context" upgrade "$release" deploy/helm/gatewright \
  --namespace "$namespace" --reuse-values \
  --set dataPlane.replicaCount=5 \
  --set gateway.extraEnv.GATEWRIGHT_PERF_ROLL="$image_tag" >/dev/null
kubectl --context "$context" --namespace "$namespace" rollout status deployment "$dp_deployment" --timeout=180s >/dev/null
errors_after_rollout="$(wc -l <"$error_log")"
log "Rolling update done: $((errors_after_rollout - errors_after_scale_in)) error(s) during the rolling update"

log "Chaos (14.5): abrupt loss of one data-plane pod (no preStop, no SIGTERM), under traffic"
victim="$(kubectl --context "$context" --namespace "$namespace" get pods -l app.kubernetes.io/component=dataplane -o jsonpath='{.items[0].metadata.name}')"
kubectl --context "$context" --namespace "$namespace" delete pod "$victim" --grace-period=0 --force >/dev/null 2>&1
for _ in $(seq 1 120); do
  ready="$(kubectl --context "$context" --namespace "$namespace" get deployment "$dp_deployment" -o jsonpath='{.status.readyReplicas}')"
  [[ "${ready:-0}" == "5" ]] && break
  sleep 1
done
[[ "${ready:-0}" == "5" ]] || { echo "Deployment never recovered to 5 ready replicas after losing a pod (got ${ready:-0})" >&2; exit 1; }
errors_after_kill=$(($(wc -l <"$error_log")))
# Requests in flight to the killed pod, or routed to it before kube-proxy drops it, are lost by
# definition - no graceful path exists. The target is bounded loss plus full recovery, not zero.
kill_errors=$((errors_after_kill - errors_after_rollout))
log "Abrupt pod loss: $kill_errors error(s) (bounded loss accepted, full recovery to 5 ready replicas confirmed)"

kill "$traffic_pid" 2>/dev/null || true

total_errors=$(($(wc -l <"$error_log")))  # wc -l pads its output on BSD/macOS; normalize to a bare integer.
echo
echo "=== Phase 14.2 scale-test summary ==="
echo "Config propagation at $dp_replicas pods: ${elapsed}s (target < ${converge_timeout}s)"
echo "Errors during scale-out:     $errors_after_scale_out"
echo "Errors during scale-in:      $((errors_after_scale_in - errors_after_scale_out))"
echo "Errors during rolling update: $((errors_after_rollout - errors_after_scale_in))"
echo "Errors during abrupt pod loss (bounded loss accepted): $kill_errors"
echo "Total request errors observed (graceful phases): $((total_errors - kill_errors))"
if [[ "${DEBUG_DUMP:-0}" == "1" ]]; then
  cp "$error_log" /tmp/gatewright-perf-errors.log
  kubectl --context "$context" --namespace "$namespace" get events --sort-by=.lastTimestamp > /tmp/gatewright-perf-events.log
  kubectl --context "$context" --namespace "$namespace" get pods -o wide > /tmp/gatewright-perf-pods.log
fi
rm -f "$error_log"

if [[ "$((total_errors - kill_errors))" != "0" ]]; then
  echo "FAILED: expected zero errors during scale-out/scale-in/rolling update" >&2
  exit 1
fi

echo "OK"

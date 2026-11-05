#!/usr/bin/env bash
# Copyright (c) Gatewright contributors.
# SPDX-License-Identifier: MIT
#
# End-to-end test of the Helm chart on a real Kubernetes cluster: builds the images, installs the chart
# (gateway.mode=all), deploys the backend-orders sample as an annotated Service (Kubernetes discovery), then
# exercises the key scenarios through the Admin API and the proxy. Requires kind, kubectl, helm and Docker.
#
#   kind create cluster --name gatewright && ./scripts/k8s-e2e.sh
#
# KEEP_NAMESPACE=1 skips the final cleanup, to inspect a failed run.
set -euo pipefail

cd "$(dirname "$0")/.."

cluster="${GATEWRIGHT_E2E_CLUSTER:-gatewright}"
context="kind-$cluster"
namespace="gatewright-e2e"
release="gatewright-e2e"
image_tag="e2e-$(date +%s)"

log() { echo "==> $*"; }

for tool in kind kubectl helm dotnet docker; do
  command -v "$tool" >/dev/null || { echo "Missing required tool: $tool" >&2; exit 1; }
done

if ! kind get clusters | grep -qx "$cluster"; then
  log "Creating kind cluster '$cluster'"
  kind create cluster --name "$cluster" --wait 180s
fi

cleanup() {
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

log "Building gatewright-host:$image_tag (dotnet publish -t:PublishContainer)"
dotnet publish src/Gatewright.Host -c Release -t:PublishContainer \
  -p:ContainerImageTag="$image_tag" -p:MinVerSkip=true >/dev/null

log "Building the backend-orders sample image"
docker build -t "gatewright-sample-orders:$image_tag" -f Dockerfile \
  --build-arg PROJECT=samples/backend-orders --build-arg ASSEMBLY=backend-orders . >/dev/null

log "Loading images into '$cluster'"
kind load docker-image "gatewright-host:$image_tag" --name "$cluster"
kind load docker-image "gatewright-sample-orders:$image_tag" --name "$cluster"

log "Creating namespace '$namespace'"
kubectl --context "$context" create namespace "$namespace" --dry-run=client -o yaml | kubectl --context "$context" apply -f - >/dev/null

log "Deploying the backend-orders sample (annotated for Kubernetes discovery)"
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

log "Installing the chart ($release, gateway.mode=all)"
helm --kube-context "$context" install "$release" deploy/helm/gatewright \
  --namespace "$namespace" \
  --set image.repository=gatewright-host \
  --set image.tag="$image_tag" \
  --set image.pullPolicy=Never \
  --set controlPlane.auth.developmentBypass=true \
  --set all.service.type=ClusterIP >/dev/null

log "Waiting for the gateway pod to be ready"
kubectl --context "$context" --namespace "$namespace" wait --for=condition=ready pod \
  -l app.kubernetes.io/component=all --timeout=180s >/dev/null

# Helm's fullname helper collapses "<release>-gatewright" to just "<release>" when the release name already
# contains "gatewright" - look the Service up by label instead of assuming its name.
service="$(kubectl --context "$context" --namespace "$namespace" get svc -l app.kubernetes.io/component=all -o jsonpath='{.items[0].metadata.name}')"

port_forward() {
  kubectl --context "$context" --namespace "$namespace" port-forward "svc/$service" "$1:8080" >/tmp/gatewright-e2e-pf.log 2>&1 &
  echo $!
}

pf_pid="$(port_forward 18080)"
trap 'kill "$pf_pid" 2>/dev/null || true; cleanup' EXIT
for _ in $(seq 1 20); do
  curl -sf -o /dev/null http://localhost:18080/healthz && break
  sleep 0.5
done

log "Scenario: /healthz is reachable, /readyz is 503 before any publication"
curl -sf -o /dev/null http://localhost:18080/healthz
code="$(curl -s -o /dev/null -w '%{http_code}' http://localhost:18080/readyz)"
[[ "$code" == "503" ]] || { echo "Expected 503 before publication, got $code" >&2; exit 1; }

log "Scenario: create an API backed by the discovered Service, publish it"
curl -sf -X POST http://localhost:18080/admin/v1/apis -H 'content-type: application/json' \
  -d "{\"name\":\"orders\",\"basePath\":\"/orders\",\"backend\":{\"service\":\"$namespace/backend-orders:80\"}}" >/dev/null
curl -sf -X POST http://localhost:18080/admin/v1/environments/dev/publications -H 'content-type: application/json' -d '{}' >/dev/null

log "Scenario: the proxy reaches the backend through Kubernetes-discovered endpoints"
for _ in $(seq 1 30); do
  code="$(curl -s -o /dev/null -w '%{http_code}' http://localhost:18080/orders/1)"
  [[ "$code" == "200" ]] && break
  sleep 1
done
[[ "$code" == "200" ]] || { echo "Expected 200 from /orders/1, got $code (discovery or publication did not converge in time)" >&2; exit 1; }
body="$(curl -sf http://localhost:18080/orders/1)"
echo "$body" | grep -q '"product":"Widget"' || { echo "Unexpected backend response: $body" >&2; exit 1; }

log "Scenario: /readyz is now ready"
curl -sf -o /dev/null http://localhost:18080/readyz

echo "E2E OK"

{{/*
Chart name, overridable.
*/}}
{{- define "gatewright.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{/*
Fully qualified release name.
*/}}
{{- define "gatewright.fullname" -}}
{{- if .Values.fullnameOverride -}}
{{- .Values.fullnameOverride | trunc 63 | trimSuffix "-" -}}
{{- else -}}
{{- $name := default .Chart.Name .Values.nameOverride -}}
{{- if contains $name .Release.Name -}}
{{- .Release.Name | trunc 63 | trimSuffix "-" -}}
{{- else -}}
{{- printf "%s-%s" .Release.Name $name | trunc 63 | trimSuffix "-" -}}
{{- end -}}
{{- end -}}
{{- end -}}

{{/*
Common labels.
*/}}
{{- define "gatewright.labels" -}}
helm.sh/chart: {{ printf "%s-%s" (include "gatewright.name" .) .Chart.Version | replace "+" "_" | trunc 63 | trimSuffix "-" }}
{{ include "gatewright.selectorLabels" . }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
{{- end -}}

{{/*
Selector labels, parameterized by component (controlplane, dataplane, all, portal).
*/}}
{{- define "gatewright.selectorLabels" -}}
app.kubernetes.io/name: {{ include "gatewright.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- if .component }}
app.kubernetes.io/component: {{ .component }}
{{- end }}
{{- end -}}

{{/*
Service account name.
*/}}
{{- define "gatewright.serviceAccountName" -}}
{{- if .Values.serviceAccount.create -}}
{{- default (include "gatewright.fullname" .) .Values.serviceAccount.name -}}
{{- else -}}
{{- default "default" .Values.serviceAccount.name -}}
{{- end -}}
{{- end -}}

{{/*
The distribution token: given value, else the one already stored in the cluster, else a fresh random one.
Stable across `helm upgrade` because it is read back from the existing Secret.
*/}}
{{- define "gatewright.distributionToken" -}}
{{- $secretName := printf "%s-distribution" (include "gatewright.fullname" .) -}}
{{- $existing := lookup "v1" "Secret" .Release.Namespace $secretName -}}
{{- if .Values.distribution.token -}}
{{- .Values.distribution.token -}}
{{- else if $existing -}}
{{- index $existing.data "token" | b64dec -}}
{{- else -}}
{{- randAlphaNum 32 -}}
{{- end -}}
{{- end -}}

{{/*
The effective Redis connection string: the external one if set, else the in-cluster dev Redis when enabled, else empty.
*/}}
{{- define "gatewright.redisConnectionString" -}}
{{- if .Values.redis.connectionString -}}
{{- .Values.redis.connectionString -}}
{{- else if .Values.redis.dev.enabled -}}
{{- printf "%s-redis-dev:6379" (include "gatewright.fullname" .) -}}
{{- end -}}
{{- end -}}

{{/*
The effective PostgreSQL connection string: the external one if set, else the in-cluster dev PostgreSQL when
enabled, else empty (SQLite). The dev password is a plain value (postgres.dev.password), not generated: it only
ever protects a throwaway, non-persistent-by-default dev database, and a plain value keeps it identical on both
sides (this connection string and the dev Postgres container's own POSTGRES_PASSWORD) without relying on
`lookup` timing across separately rendered templates.
*/}}
{{- define "gatewright.postgresConnectionString" -}}
{{- if .Values.postgres.connectionString -}}
{{- .Values.postgres.connectionString -}}
{{- else if .Values.postgres.dev.enabled -}}
{{- printf "Host=%s-postgres-dev;Database=%s;Username=%s;Password=%s" (include "gatewright.fullname" .) .Values.postgres.dev.database .Values.postgres.dev.username .Values.postgres.dev.password -}}
{{- end -}}
{{- end -}}

{{/*
An initContainer waiting for the bundled dev PostgreSQL to accept connections, so the control plane's first
migration attempt does not race a Postgres container that is still running initdb. A no-op (nothing rendered)
when postgres.dev is not in use: an externally managed PostgreSQL is assumed already reachable.
*/}}
{{- define "gatewright.waitForDevPostgresInitContainer" -}}
{{- if and .Values.postgres.dev.enabled (not .Values.postgres.connectionString) }}
- name: wait-for-postgres
  image: {{ .Values.postgres.dev.image }}
  securityContext:
    {{- toYaml .Values.securityContext | nindent 4 }}
    runAsUser: 70
    runAsGroup: 70
  command:
    - sh
    - -c
    - until pg_isready -h {{ include "gatewright.fullname" . }}-postgres-dev -p 5432 -U {{ .Values.postgres.dev.username }}; do echo waiting for postgres; sleep 1; done
{{- end }}
{{- end -}}

{{/*
Common environment variables for every Gatewright process.
*/}}
{{- define "gatewright.commonEnv" -}}
- name: Gateway__Environment
  value: {{ .Values.gateway.environment | quote }}
- name: Gateway__Shutdown__DrainDelaySeconds
  value: {{ .Values.gateway.shutdown.drainDelaySeconds | quote }}
{{- if .Values.gateway.diagnosticsToken }}
- name: Gateway__DiagnosticsToken
  valueFrom:
    secretKeyRef:
      name: {{ include "gatewright.fullname" . }}-secrets
      key: diagnosticsToken
{{- end }}
{{- if or .Values.redis.connectionString .Values.redis.dev.enabled }}
- name: Redis__ConnectionString
  valueFrom:
    secretKeyRef:
      name: {{ include "gatewright.fullname" . }}-secrets
      key: redisConnectionString
{{- end }}
{{- range $key, $value := .Values.gateway.extraEnv }}
- name: {{ $key }}
  value: {{ $value | quote }}
{{- end }}
{{- end -}}

{{/*
Distribution token environment variable, server side (control plane): Distribution:Tokens:0 (a list; several allow rotation).
*/}}
{{- define "gatewright.distributionServerEnv" -}}
- name: Distribution__Tokens__0
  valueFrom:
    secretKeyRef:
      name: {{ include "gatewright.fullname" . }}-distribution
      key: token
{{- end -}}

{{/*
Distribution token and endpoint environment variables, client side (data plane), pointing at the control plane's gRPC Service.
*/}}
{{- define "gatewright.distributionClientEnv" -}}
- name: Distribution__Token
  valueFrom:
    secretKeyRef:
      name: {{ include "gatewright.fullname" . }}-distribution
      key: token
- name: Distribution__Endpoint
  value: {{ printf "http://%s-controlplane:%d" (include "gatewright.fullname" .) (.Values.controlPlane.service.grpcPort | int) | quote }}
{{- end -}}

{{/*
Control-plane-specific environment variables (database, Kubernetes discovery, auth).
*/}}
{{- define "gatewright.controlPlaneEnv" -}}
- name: ControlPlane__ConnectionString
  valueFrom:
    secretKeyRef:
      name: {{ include "gatewright.fullname" . }}-secrets
      key: controlPlaneConnectionString
- name: ControlPlane__SeedSample
  value: {{ .Values.controlPlane.seedSample | quote }}
- name: Kubernetes__Enabled
  value: {{ .Values.kubernetes.enabled | quote }}
{{- if or .Values.postgres.connectionString .Values.postgres.dev.enabled }}
- name: ControlPlane__Provider
  value: "Postgres"
{{- end }}
{{- if .Values.controlPlane.auth.authority }}
- name: ControlPlane__Auth__Authority
  value: {{ .Values.controlPlane.auth.authority | quote }}
- name: ControlPlane__Auth__Audience
  value: {{ .Values.controlPlane.auth.audience | quote }}
{{- end }}
{{- if .Values.controlPlane.auth.developmentBypass }}
- name: ControlPlane__Auth__DevelopmentBypass
  value: "true"
- name: ASPNETCORE_ENVIRONMENT
  value: "Development"
{{- end }}
{{- end -}}

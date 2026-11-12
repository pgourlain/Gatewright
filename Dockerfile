# Gatewright image. One Dockerfile builds every deployable project of the repository.
#
#   docker build -t gatewright/host:dev .                                              # standard gateway (all modes)
#   docker build -t gatewright/portal:dev --build-arg PROJECT=src/Gatewright.Portal --build-arg ASSEMBLY=Gatewright.Portal .
#   docker build -t acme/gateway:dev --build-arg PROJECT=samples/custom-policy --build-arg ASSEMBLY=Acme.Gateway .
#
# Works the same with `podman build` and `nerdctl build` (Colima).

FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
ARG PROJECT=src/Gatewright.Host
WORKDIR /src
COPY . .
RUN dotnet publish "$PROJECT" -c Release -o /app -p:UseAppHost=false -p:MinVerSkip=true

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
ARG ASSEMBLY=Gatewright.Host
LABEL org.opencontainers.image.licenses="MIT" \
      org.opencontainers.image.source="https://github.com/pgourlain/gatewright"
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_gcServer=0 \
    ASSEMBLY=${ASSEMBLY}
WORKDIR /app
COPY --from=build /app .
# Writable location for the control plane database (mount a volume here).
USER root
RUN mkdir -p /data && chown "$APP_UID" /data
VOLUME /data
# The base image ships an unprivileged "app" user.
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["sh", "-c", "exec dotnet \"/app/${ASSEMBLY}.dll\" \"$@\"", "--"]

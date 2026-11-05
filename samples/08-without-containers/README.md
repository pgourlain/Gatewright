# 08 - Without containers

No Docker, no Kubernetes: .NET 10 SDK only.

## A. Single process

```bash
dotnet run --project samples/backend-orders --urls http://localhost:5101 &
dotnet run --project src/Gatewright.Host -- --Gateway:Mode=all --Gateway:ConfigFile=$PWD/samples/dev-config.json --urls http://localhost:8080
curl http://localhost:8080/orders
```

## B. Control plane and data plane as separate processes

```bash
# control plane: Admin API on 8081, gRPC on 8082 (cleartext HTTP/2)
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Gatewright.Host -- \
  --Gateway:Mode=ControlPlane --ControlPlane:Auth:DevelopmentBypass=true --Distribution:Tokens:0=dev \
  --Kestrel:Endpoints:Admin:Url=http://localhost:8081 --Kestrel:Endpoints:Admin:Protocols=Http1 \
  --Kestrel:Endpoints:Grpc:Url=http://localhost:8082 --Kestrel:Endpoints:Grpc:Protocols=Http2

# data plane on 8080
dotnet run --project src/Gatewright.Host -- --Gateway:Mode=DataPlane --Gateway:Environment=dev \
  --Distribution:Endpoint=http://localhost:8082 --Distribution:Token=dev --urls http://localhost:8080

# publish with the CLI
dotnet run --project src/Gatewright.Cli -- login --server http://localhost:8081 --token dev
dotnet run --project src/Gatewright.Cli -- config import samples/dev-config.json
dotnet run --project src/Gatewright.Cli -- publish --env dev
```

## C. Code-first backend

`samples/backend-orders` is a plain minimal API. With the `Gatewright.Exposure` package a .NET backend declares its policies in code and
its OpenAPI document carries them (`x-gateway`); importing that document creates the API, with no JSON written by hand
(see [docs/user/x-gateway.md](../../docs/user/x-gateway.md)).

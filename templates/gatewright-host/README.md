# Acme.Gateway

A Gatewright distribution: the standard gateway plus your own policies.

```bash
dotnet test                     # tests run the whole gateway in memory
dotnet run -- --Gateway:Mode=all --Gateway:ConfigFile=$PWD/config.json --urls http://localhost:8080
dotnet publish /t:PublishContainer   # container image (or: docker build .)
```

- `SamplePolicy.cs`: a custom policy (`acme.requireHeader`). Edit it or add yours, then register it in `Program.cs`.
- `tests/`: tests written with `GatewayTestHost` (see the Gatewright docs, "Writing a custom policy").
- Deploy with the Gatewright Helm chart by setting `image.repository` to your image. Use the same image for the control plane and the data planes.

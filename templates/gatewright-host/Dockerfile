# Build:  docker build -t acme/gateway .
# Or without Docker:  dotnet publish /t:PublishContainer
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Acme.Gateway.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish Acme.Gateway.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Acme.Gateway.dll"]

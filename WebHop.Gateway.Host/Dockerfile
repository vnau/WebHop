# Build from the repository root: docker build -f WebHop.Gateway.Host/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY WebHop.Core/WebHop.Core.csproj WebHop.Core/
COPY WebHop.Gateway/WebHop.Gateway.csproj WebHop.Gateway/
COPY WebHop.Gateway.Host/WebHop.Gateway.Host.csproj WebHop.Gateway.Host/
RUN dotnet restore WebHop.Gateway.Host/WebHop.Gateway.Host.csproj
COPY WebHop.Core/ WebHop.Core/
COPY WebHop.Gateway/ WebHop.Gateway/
COPY WebHop.Gateway.Host/ WebHop.Gateway.Host/
RUN dotnet publish WebHop.Gateway.Host/WebHop.Gateway.Host.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
# Listens on ASPNETCORE_HTTP_PORTS (8080 by default in .NET images)
EXPOSE 8080
ENTRYPOINT ["dotnet", "WebHop.Gateway.Host.dll"]

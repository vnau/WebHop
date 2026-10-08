# Build from the repository root: docker build -f WebHop.Example/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY WebHop.Core/WebHop.Core.csproj WebHop.Core/
COPY WebHop.Origin/WebHop.Origin.csproj WebHop.Origin/
COPY WebHop.Example/WebHop.Example.csproj WebHop.Example/
RUN dotnet restore WebHop.Example/WebHop.Example.csproj
COPY WebHop.Core/ WebHop.Core/
COPY WebHop.Origin/ WebHop.Origin/
COPY WebHop.Example/ WebHop.Example/
RUN dotnet publish WebHop.Example/WebHop.Example.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
# No ports: the app only connects out to the gateway given in ASPNETCORE_URLS
ENTRYPOINT ["dotnet", "WebHop.Example.dll"]

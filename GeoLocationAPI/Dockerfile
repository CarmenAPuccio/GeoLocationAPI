# syntax=docker/dockerfile:1
# The build context is the repository root, because Directory.Build.props, Directory.Packages.props
# and global.json live there. Build with:
#   finch build -f GeoLocationAPI/Dockerfile -t geolocationapi .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so this layer is cached until a package or build setting changes.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY GeoLocationAPI/GeoLocationAPI.csproj GeoLocationAPI/
RUN dotnet restore GeoLocationAPI/GeoLocationAPI.csproj

COPY GeoLocationAPI/ GeoLocationAPI/
RUN dotnet publish GeoLocationAPI/GeoLocationAPI.csproj \
    -c Release -o /app/publish --no-restore -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=5254
EXPOSE 5254

COPY --from=build /app/publish .

# The image ships a non-root "app" user.
USER $APP_UID

ENTRYPOINT ["dotnet", "GeoLocationAPI.dll"]

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD wget --quiet --tries=1 --spider http://localhost:5254/hc || exit 1

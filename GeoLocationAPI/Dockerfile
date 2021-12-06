FROM mcr.microsoft.com/dotnet/aspnet:6.0-alpine AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["GeoLocationAPI.csproj", "./"]
RUN dotnet restore "GeoLocationAPI.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "GeoLocationAPI.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "GeoLocationAPI.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "GeoLocationAPI.dll"]
HEALTHCHECK --interval=30s --timeout=30s --start-period=5s --retries=3 CMD wget --quiet --tries=1 --spider http://localhost:5000/hc || exit 1

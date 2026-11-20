# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first (cached layer as long as the .csproj files do not change)
COPY BgInventario.slnx ./
COPY src/BgInventario.Core/BgInventario.Core.csproj src/BgInventario.Core/
COPY src/BgInventario.Infrastructure/BgInventario.Infrastructure.csproj src/BgInventario.Infrastructure/
COPY src/BgInventario.Api/BgInventario.Api.csproj src/BgInventario.Api/
RUN dotnet restore src/BgInventario.Api/BgInventario.Api.csproj

COPY src/ src/
RUN dotnet publish src/BgInventario.Api/BgInventario.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
# Non-root user shipped with the official .NET images
USER $APP_UID
ENTRYPOINT ["dotnet", "BgInventario.Api.dll"]

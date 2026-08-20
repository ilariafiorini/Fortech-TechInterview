# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["src/FlightsService/FlightsService.csproj", "src/FlightsService/"]
COPY ["src/TechInterview.ServiceDefaults/TechInterview.ServiceDefaults.csproj", "src/TechInterview.ServiceDefaults/"]
RUN dotnet restore "src/FlightsService/FlightsService.csproj"

COPY src/FlightsService/ src/FlightsService/
COPY src/TechInterview.ServiceDefaults/ src/TechInterview.ServiceDefaults/

WORKDIR /src/src/FlightsService
RUN dotnet publish -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
ENV ASPNETCORE_HTTP_PORTS=8080
# Porta HTTP/1.1 dedicata all'healthcheck Docker (vedi Program.cs): l'endpoint gRPC
# principale (8080) resta HTTP/2 puro, incompatibile con curl.
ENV FLIGHTS_HEALTH_PORT=8090
EXPOSE 8080
COPY --from=build /app/publish .
HEALTHCHECK --interval=10s --timeout=3s --retries=5 --start-period=15s CMD curl -f http://localhost:8090/health || exit 1
ENTRYPOINT ["dotnet", "FlightsService.dll"]

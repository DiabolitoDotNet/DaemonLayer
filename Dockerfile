# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Copy csproj files and restore dependencies
COPY src/InfernalHierarchy.Core/*.csproj ./src/InfernalHierarchy.Core/
COPY src/InfernalHierarchy.Agents/*.csproj ./src/InfernalHierarchy.Agents/
COPY src/InfernalHierarchy.Tools/*.csproj ./src/InfernalHierarchy.Tools/
COPY src/InfernalHierarchy.Memory/*.csproj ./src/InfernalHierarchy.Memory/
COPY src/InfernalHierarchy.Messaging/*.csproj ./src/InfernalHierarchy.Messaging/
COPY src/InfernalHierarchy.Personas/*.csproj ./src/InfernalHierarchy.Personas/
COPY src/InfernalHierarchy.Telegram/*.csproj ./src/InfernalHierarchy.Telegram/
COPY src/InfernalHierarchy.Host/*.csproj ./src/InfernalHierarchy.Host/

RUN dotnet restore ./src/InfernalHierarchy.Host/InfernalHierarchy.Host.csproj

# Copy everything else and build
COPY src/ ./src/
COPY souls/ ./souls/

WORKDIR /source/src/InfernalHierarchy.Host
RUN dotnet publish -c Release -o /app --no-restore

# Stage 2: Runtime
# Host is an ASP.NET Core app (health/metrics endpoints), so we need Microsoft.AspNetCore.App
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# Copy build output
COPY --from=build /app ./
COPY --from=build /source/souls /souls

# Create data directory for LiteDB
RUN mkdir -p /app/data && \
    mkdir -p /app/logs

# Set environment
ENV ASPNETCORE_ENVIRONMENT=Production

# Run the application
ENTRYPOINT ["dotnet", "InfernalHierarchy.Host.dll"]

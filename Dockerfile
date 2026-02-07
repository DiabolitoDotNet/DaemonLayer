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
COPY templates/ ./templates/

WORKDIR /source/src/InfernalHierarchy.Host
RUN dotnet publish -c Release -o /app --no-restore

# Stage 1b: Build whisper.cpp (STT)
FROM debian:bookworm-slim AS whisper-build
WORKDIR /whisper

RUN apt-get update && \
        apt-get install -y --no-install-recommends \
            ca-certificates \
            git \
            build-essential \
            cmake \
        && rm -rf /var/lib/apt/lists/*

# Build whisper.cpp from source
RUN git clone --depth 1 https://github.com/ggerganov/whisper.cpp .
RUN cmake -S . -B build -DCMAKE_BUILD_TYPE=Release
RUN cmake --build build --config Release -j

RUN mkdir -p /out && cp /whisper/build/bin/main /out/whisper

# Stage 2: Runtime
# Host is an ASP.NET Core app (health/metrics endpoints), so we need Microsoft.AspNetCore.App
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# Install ffmpeg (used for decoding uploads into WAV for whisper.cpp)
RUN apt-get update && \
    apt-get install -y --no-install-recommends ffmpeg && \
    rm -rf /var/lib/apt/lists/*

# Copy build output
COPY --from=build /app ./
COPY --from=build /source/souls /souls
COPY --from=build /source/templates ./templates

# Copy whisper.cpp binary
# (whisper.cpp builds a 'main' binary; we standardize it to /usr/local/bin/whisper)
RUN mkdir -p /usr/local/bin
COPY --from=whisper-build /out/whisper /usr/local/bin/whisper

# Create data directory for LiteDB
RUN mkdir -p /app/data && \
    mkdir -p /app/logs

# Set environment
ENV ASPNETCORE_ENVIRONMENT=Production

# Run the application
ENTRYPOINT ["dotnet", "InfernalHierarchy.Host.dll"]

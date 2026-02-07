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

# Stage 2: Runtime
# Host is an ASP.NET Core app (health/metrics endpoints), so we need Microsoft.AspNetCore.App
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# Install ffmpeg (used for decoding uploads into WAV) + Python tooling for faster-whisper/kokoro
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
        ffmpeg \
        python3 \
        python3-pip \
        python3-venv \
        espeak-ng \
        libsndfile1 \
    && rm -rf /var/lib/apt/lists/*

# Voice helpers (Python)
COPY scripts/voice /app/voice

# Install Python deps into a dedicated venv (Ubuntu marks system Python as externally-managed)
# Note: Torch CPU wheels are hosted on the official PyTorch index.
RUN python3 -m venv /opt/voice-venv && \
    /opt/voice-venv/bin/pip install --no-cache-dir --upgrade pip && \
    /opt/voice-venv/bin/pip install --no-cache-dir --index-url https://download.pytorch.org/whl/cpu torch && \
    /opt/voice-venv/bin/pip install --no-cache-dir -r /app/voice/requirements.txt

# Copy build output
COPY --from=build /app ./
COPY --from=build /source/souls /souls
COPY --from=build /source/templates ./templates

# Create data directory for LiteDB
RUN mkdir -p /app/data && \
    mkdir -p /app/logs

# Set environment
ENV ASPNETCORE_ENVIRONMENT=Production

# Run the application
ENTRYPOINT ["dotnet", "InfernalHierarchy.Host.dll"]

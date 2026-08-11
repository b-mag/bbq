# =============================================================================
# Dockerfile for Carcosa Matchmaking Service
# Multi-stage build: SDK for compilation, runtime-deps for the AOT binary
# =============================================================================

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project file and restore dependencies
COPY Carcosa.Matchmaking.csproj .
RUN dotnet restore

# Copy source and publish as AOT
COPY . .
RUN dotnet publish -c Release -r linux-x64 -o /app

# Runtime stage: minimal image with just the native binary
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

EXPOSE 5100
ENTRYPOINT ["./Carcosa.Matchmaking"]

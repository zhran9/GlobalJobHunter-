# ============================================================
# STAGE 1 — BUILD
# Use the full .NET 8 SDK to compile and publish the app.
# We use the standard SDK here (not Playwright) because we
# only need .NET tools to build — no browser needed yet.
# ============================================================
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

# Set the working directory inside the container
WORKDIR /src

# Copy ONLY the .csproj first and restore NuGet packages.
# Docker caches this layer — if .csproj hasn't changed, it
# skips this expensive step on future builds. Smart caching!
COPY ["GlobalJobHunter.Service.csproj", "."]
RUN dotnet restore "GlobalJobHunter.Service.csproj"

# Now copy all remaining source files
COPY . .

# Publish a Release build to /app/publish
# --no-restore: packages already restored above
# -o: output directory
RUN dotnet publish "GlobalJobHunter.Service.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore

# ============================================================
# STAGE 2 — RUNTIME
# Use the official Microsoft Playwright+.NET image.
# This image already has Chromium + all system dependencies
# pre-installed. We copy only the published output from Stage 1.
# Result: a lean, production-ready container.
# ============================================================
FROM mcr.microsoft.com/playwright/dotnet:v1.44.0-jammy AS runtime

# Set working directory for the app
WORKDIR /app

# Create the data directory where SQLite (jobs.db) will live.
# This is the path we will mount a persistent volume to.
RUN mkdir -p /app/data

# Copy the published app from Stage 1
COPY --from=build /app/publish .

# Tell Playwright where Chromium is (pre-installed in this image)
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright

# Set environment to Production (reads appsettings.Production.json if present)
ENV DOTNET_ENVIRONMENT=Production

# Your app reads from appsettings.json — secrets like API keys
# should be overridden via environment variables at runtime,
# NOT baked into the image. See docker-compose or Render dashboard.
# Example: AI__ApiKey, Telegram__BotToken, Telegram__ChatId

# The entry point: run the compiled DLL
ENTRYPOINT ["dotnet", "GlobalJobHunter.Service.dll"]

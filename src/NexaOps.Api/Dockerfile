# NexaOps API container image.
#
# Multi-stage: the SDK image compiles, and only the published output is copied into a runtime
# image. The final image therefore contains no compiler, no source, no NuGet cache and no
# build-time secret - which is both smaller and a materially smaller attack surface.

# ---------------------------------------------------------------------------
# Build
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

ARG BUILD_CONFIGURATION=Release
ARG VERSION=1.0.0

# Copy only what restore needs first. This layer is cached until a project file or the central
# package versions change, so an ordinary source edit does not re-download every package.
COPY ["Directory.Build.props", "./"]
COPY ["Directory.Packages.props", "./"]
COPY ["global.json", "./"]
COPY ["src/NexaOps.Domain/NexaOps.Domain.csproj", "src/NexaOps.Domain/"]
COPY ["src/NexaOps.Application/NexaOps.Application.csproj", "src/NexaOps.Application/"]
COPY ["src/NexaOps.Infrastructure/NexaOps.Infrastructure.csproj", "src/NexaOps.Infrastructure/"]
COPY ["src/NexaOps.Api/NexaOps.Api.csproj", "src/NexaOps.Api/"]

RUN dotnet restore "src/NexaOps.Api/NexaOps.Api.csproj"

COPY ["src/", "src/"]

RUN dotnet publish "src/NexaOps.Api/NexaOps.Api.csproj" \
    --configuration "$BUILD_CONFIGURATION" \
    --no-restore \
    --output /app/publish \
    -p:Version="$VERSION" \
    -p:UseAppHost=false

# ---------------------------------------------------------------------------
# Runtime
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS final

# ICU is required: the product formats dates and currency for en-IN, and the invariant
# globalization fallback would silently render them as en-US.
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_gcServer=1

WORKDIR /app
COPY --from=build /app/publish .

# A non-root account. The base image provides `app` (uid 64198); running as root would let a
# container escape start with full privileges inside the container filesystem.
USER $APP_UID

EXPOSE 8080

# Container Apps has its own liveness and readiness probes, so this HEALTHCHECK exists for
# `docker run` and Compose, where nothing else would notice a wedged process.
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD ["/bin/sh", "-c", "curl --fail --silent http://localhost:8080/health/live || exit 1"]

ENTRYPOINT ["dotnet", "NexaOps.Api.dll"]

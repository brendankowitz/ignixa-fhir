# Multi-stage Dockerfile for Ignixa FHIR Server
# Build arguments for versioning (passed from CI/CD)
ARG VERSION=0.0.0-dev
ARG ASSEMBLY_VERSION=0.0.0
ARG INFORMATIONAL_VERSION=0.0.0-dev+local
ARG BUILD_DATE
ARG VCS_REF

# Stage 1: Build
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:9.0-azurelinux3.0 AS build
ARG VERSION
ARG ASSEMBLY_VERSION
ARG INFORMATIONAL_VERSION

WORKDIR /src

# Copy root-level configuration files for centralized package management and code style
COPY Directory.Build.props ./
COPY Directory.Packages.props ./
COPY GitVersion.yml ./
COPY .editorconfig ./

# Copy all source project files for layer caching
# Hosting layer
COPY src/Application/Ignixa.Web/Ignixa.Web.csproj src/Application/Ignixa.Web/
# Application layer
COPY src/Application/Ignixa.Api/Ignixa.Api.csproj src/Application/Ignixa.Api/
COPY src/Application/Ignixa.Application/Ignixa.Application.csproj src/Application/Ignixa.Application/
COPY src/Application/Ignixa.Application.BackgroundOperations/Ignixa.Application.BackgroundOperations.csproj src/Application/Ignixa.Application.BackgroundOperations/
COPY src/Application/Ignixa.Application.Operations/Ignixa.Application.Operations.csproj src/Application/Ignixa.Application.Operations/
COPY src/Application/Ignixa.Domain/Ignixa.Domain.csproj src/Application/Ignixa.Domain/
# Core layer
COPY src/Core/Ignixa.Abstractions/Ignixa.Abstractions.csproj src/Core/Ignixa.Abstractions/
COPY src/Core/Ignixa.FhirMappingLanguage/Ignixa.FhirMappingLanguage.csproj src/Core/Ignixa.FhirMappingLanguage/
COPY src/Core/Ignixa.FhirPath/Ignixa.FhirPath.csproj src/Core/Ignixa.FhirPath/
COPY src/Core/Ignixa.PackageManagement/Ignixa.PackageManagement.csproj src/Core/Ignixa.PackageManagement/
COPY src/Core/Ignixa.Search/Ignixa.Search.csproj src/Core/Ignixa.Search/
COPY src/Core/Ignixa.Serialization/Ignixa.Serialization.csproj src/Core/Ignixa.Serialization/
COPY src/Core/Ignixa.Specification/Ignixa.Specification.csproj src/Core/Ignixa.Specification/
COPY src/Core/Ignixa.SqlOnFhir/Ignixa.SqlOnFhir.csproj src/Core/Ignixa.SqlOnFhir/
COPY src/Core/Ignixa.Validation/Ignixa.Validation.csproj src/Core/Ignixa.Validation/
COPY src/Core/Extensions/Ignixa.Extensions.FirelySdk6/Ignixa.Extensions.FirelySdk6.csproj src/Core/Extensions/Ignixa.Extensions.FirelySdk6/
# DataLayer
COPY src/DataLayer/Ignixa.DataLayer.BlobStorage/Ignixa.DataLayer.BlobStorage.csproj src/DataLayer/Ignixa.DataLayer.BlobStorage/
COPY src/DataLayer/Ignixa.DataLayer.FileSystem/Ignixa.DataLayer.FileSystem.csproj src/DataLayer/Ignixa.DataLayer.FileSystem/
COPY src/DataLayer/Ignixa.DataLayer.InMemoryIndex/Ignixa.DataLayer.InMemoryIndex.csproj src/DataLayer/Ignixa.DataLayer.InMemoryIndex/
COPY src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Ignixa.DataLayer.SqlEntityFramework.csproj src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/

# Restore dependencies for Web project only (excludes test/bench projects)
# DisableGitVersion=true because .git folder is not available in Docker build context
WORKDIR /src/src/Application/Ignixa.Web
RUN dotnet restore Ignixa.Web.csproj /p:DisableGitVersion=true

# Copy remaining source files
WORKDIR /src
COPY src/ src/

# Build and publish with version information
# DisableGitVersion=true because .git folder is not available in Docker build context
WORKDIR /src/src/Application/Ignixa.Web
RUN dotnet publish Ignixa.Web.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false \
    /p:DisableGitVersion=true \
    /p:Version=${VERSION} \
    /p:AssemblyVersion=${ASSEMBLY_VERSION} \
    /p:FileVersion=${ASSEMBLY_VERSION} \
    /p:InformationalVersion=${INFORMATIONAL_VERSION}

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0-azurelinux3.0 AS runtime
ARG VERSION
ARG BUILD_DATE
ARG VCS_REF

# OCI Labels (https://github.com/opencontainers/image-spec/blob/main/annotations.md)
LABEL org.opencontainers.image.title="Ignixa FHIR Server" \
      org.opencontainers.image.description="A blazing-fast multi-FHIR, multi-tenant, multi-database, data streaming reference implementation FHIR Server built in dotnet" \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.created="${BUILD_DATE}" \
      org.opencontainers.image.revision="${VCS_REF}" \
      org.opencontainers.image.vendor="Ignixa Contributors" \
      org.opencontainers.image.source="https://github.com/brendankowitz/ignixa-fhir" \
      org.opencontainers.image.licenses="MIT"

# tdnf clean all - cleans all the repos used to obtain packages and reduces the size of our image.
RUN tdnf clean all && tdnf repolist --refresh && tdnf update -y && tdnf clean all

# See https://github.com/dotnet/SqlClient/issues/220
RUN tdnf install icu -y && \
  tdnf clean all

WORKDIR /app

# Copy published output from build stage
COPY --from=build /app/publish .

# Create data directories before switching to non-root user
RUN mkdir -p /app/fhir-data/tenants && \
    chown -R nonroot:nonroot /app/fhir-data && \
    mkdir -p /app/fhir-exports && \
    chown -R nonroot:nonroot /app/fhir-exports
# Health check
#HEALTHCHECK --interval=30s --timeout=3s --start-period=30s --retries=3 \
#    CMD curl -f http://localhost:8080/health || exit 1

# Set environment variables
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true

USER nonroot
EXPOSE 8080

# Entry point
ENTRYPOINT ["dotnet", "Ignixa.Web.dll"]

# Multi-stage Dockerfile for Ignixa FHIR Server
# Stage 1: Build
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:9.0-azurelinux3.0 AS build
WORKDIR /src

# Copy root-level configuration files for centralized package management and code style
COPY Directory.Build.props ./
COPY Directory.Packages.props ./
COPY .editorconfig ./

# Copy all source project files for layer caching
COPY src/Ignixa.Abstractions/Ignixa.Abstractions.csproj src/Ignixa.Abstractions/
COPY src/Ignixa.Api/Ignixa.Api.csproj src/Ignixa.Api/
COPY src/Ignixa.Application/Ignixa.Application.csproj src/Ignixa.Application/
COPY src/Ignixa.Application.BackgroundOperations/Ignixa.Application.BackgroundOperations.csproj src/Ignixa.Application.BackgroundOperations/
COPY src/Ignixa.Application.Operations/Ignixa.Application.Operations.csproj src/Ignixa.Application.Operations/
COPY src/Ignixa.DataLayer.BlobStorage/Ignixa.DataLayer.BlobStorage.csproj src/Ignixa.DataLayer.BlobStorage/
COPY src/Ignixa.DataLayer.FileSystem/Ignixa.DataLayer.FileSystem.csproj src/Ignixa.DataLayer.FileSystem/
COPY src/Ignixa.DataLayer.InMemoryIndex/Ignixa.DataLayer.InMemoryIndex.csproj src/Ignixa.DataLayer.InMemoryIndex/
COPY src/Ignixa.DataLayer.SqlEntityFramework/Ignixa.DataLayer.SqlEntityFramework.csproj src/Ignixa.DataLayer.SqlEntityFramework/
COPY src/Ignixa.Domain/Ignixa.Domain.csproj src/Ignixa.Domain/
COPY src/Ignixa.FhirMappingLanguage/Ignixa.FhirMappingLanguage.csproj src/Ignixa.FhirMappingLanguage/
COPY src/Ignixa.FhirPath/Ignixa.FhirPath.csproj src/Ignixa.FhirPath/
COPY src/Ignixa.PackageManagement/Ignixa.PackageManagement.csproj src/Ignixa.PackageManagement/
COPY src/Ignixa.Search/Ignixa.Search.csproj src/Ignixa.Search/
COPY src/Ignixa.Serialization/Ignixa.Serialization.csproj src/Ignixa.Serialization/
COPY src/Ignixa.Specification/Ignixa.Specification.csproj src/Ignixa.Specification/
COPY src/Ignixa.SqlOnFhir/Ignixa.SqlOnFhir.csproj src/Ignixa.SqlOnFhir/
COPY src/Ignixa.Validation/Ignixa.Validation.csproj src/Ignixa.Validation/

# Restore dependencies for API project only (excludes test/bench projects)
WORKDIR /src/src/Ignixa.Api
RUN dotnet restore Ignixa.Api.csproj

# Copy remaining source files
WORKDIR /src
COPY src/ src/

# Build and publish
WORKDIR /src/src/Ignixa.Api
RUN dotnet publish Ignixa.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0-azurelinux3.0 AS runtime

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
ENTRYPOINT ["dotnet", "Ignixa.Api.dll"]

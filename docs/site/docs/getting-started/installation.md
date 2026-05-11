---
sidebar_position: 1
title: Installation
description: Choose your path - run the server or use the SDK
---

# Getting Started

Choose your path based on what you want to do.

## Run the FHIR Server

Deploy a fully-featured FHIR R4/R5 server.

### Docker (Recommended)

The fastest way to run locally with SQL Server included:

```bash
git clone https://github.com/brendankowitz/ignixa-fhir.git
cd ignixa-fhir
echo "SQL_SA_PASSWORD=<your-password>" > .env
docker compose up -d
```

Access at `http://localhost:8080/metadata`

See [Docker Deployment](/docs/server/deployment/docker) for configuration options.

### Azure

One-click deployment to Azure App Service:

[![Deploy to Azure](https://aka.ms/deploytoazurebutton)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2Fbrendankowitz%2Fignixa-fhir%2Fmain%2Fdeploy%2Fazure%2Fazuredeploy.json)

See [Azure Deployment](/docs/server/deployment/azure) for CLI options.

### From Source

```bash
git clone https://github.com/brendankowitz/ignixa-fhir.git
cd ignixa-fhir
dotnet build All.sln
cd src/Application/Ignixa.Web
dotnet run
```

Requires SQL Server configured in `appsettings.Development.json`.

## Use the Core SDK

Build custom FHIR applications with standalone NuGet packages. No server required.

```bash
dotnet add package Ignixa.Serialization   # JSON parsing
dotnet add package Ignixa.FhirPath        # FHIRPath evaluation
dotnet add package Ignixa.Validation      # Resource validation
dotnet add package Ignixa.FhirFakes       # Test data generation
```

See [Core SDK Overview](/docs/core-sdk/overview) for all packages.

## CLI Tools

Standalone command-line tools:

```bash
dotnet tool install --global Ignixa.FhirFakes.Cli     # Generate test data
dotnet tool install --global Ignixa.Validation.Cli   # Validate resources
dotnet tool install --global Ignixa.SqlOnFhir.Cli    # Transform to Parquet/CSV/NDJSON
```

## Next Steps

| Goal | Next Step |
|------|-----------|
| Try the FHIR API | [Quick Start](/docs/getting-started/quick-start) |
| Configure the server | [Server Configuration](/docs/server/configuration) |
| Build with the SDK | [Core SDK Overview](/docs/core-sdk/overview) |

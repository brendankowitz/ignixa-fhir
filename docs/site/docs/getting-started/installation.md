---
sidebar_position: 1
title: Installation
description: Install Ignixa FHIR Server and Core SDK packages
---

# Installation

## Deploy the FHIR Server

### Quickest: Deploy to Azure (Recommended)

Deploy a production-ready environment with a single click:

[![Deploy to Azure](https://aka.ms/deploytoazurebutton)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2Fbrendankowitz%2Fignixa-fhir%2Fmain%2Fdeploy%2Fazure%2Fazuredeploy.json)

This provisions App Service, SQL Server, Storage, and Managed Identity automatically.

See [Azure Deployment](/docs/server/deployment/azure) for CLI options and configuration.

### Local Development: Docker Compose

```bash
git clone https://github.com/brendankowitz/ignixa-fhir.git
cd ignixa-fhir

# Create .env with SQL_SA_PASSWORD=YourStrong!Passw0rd
docker compose up -d
```

Access at `http://localhost:8080/metadata`.

See [Docker Deployment](/docs/server/deployment/docker) for details.

### From Source

```bash
git clone https://github.com/brendankowitz/ignixa-fhir.git
cd ignixa-fhir
dotnet build All.sln

cd src/Application/Ignixa.Web
dotnet run
```

Requires SQL Server. Configure connection in `appsettings.Development.json`:

```json
{
  "Tenants": {
    "Configurations": [{
      "TenantId": 1,
      "Storage": {
        "ConnectionString": "Server=(local);Database=FHIR_R4;Integrated Security=true;TrustServerCertificate=true"
      }
    }]
  }
}
```

## Install Core SDK Packages

Use the SDK packages independently to build custom FHIR applications:

```bash
# Foundation packages
dotnet add package Ignixa.Abstractions
dotnet add package Ignixa.Specification
dotnet add package Ignixa.Serialization

# Feature packages
dotnet add package Ignixa.FhirPath
dotnet add package Ignixa.Validation
dotnet add package Ignixa.Search

# Testing & Development
dotnet add package Ignixa.FhirFakes
```

See the [Core SDK Overview](/docs/core-sdk/overview) for detailed package descriptions.

## CLI Tools

```bash
# Synthetic FHIR data generator
dotnet tool install --global Ignixa.FhirFakes.Cli

# FHIR resource validator
dotnet tool install --global Ignixa.Validation.Cli

# SQL on FHIR transformer
dotnet tool install --global Ignixa.SqlOnFhir.Cli
```

## Next Steps

- [Quick Start Guide](/docs/getting-started/quick-start) - Make your first FHIR requests
- [Configuration](/docs/getting-started/configuration) - Configure storage and features

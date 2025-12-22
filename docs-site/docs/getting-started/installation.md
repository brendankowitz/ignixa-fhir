---
sidebar_position: 1
title: Installation
description: Install Ignixa FHIR Server and Core SDK packages
---

# Installation

Ignixa offers two ways to get started:

1. **Ignixa FHIR Server** - A complete, production-ready FHIR server
2. **Core SDK Packages** - Modular NuGet packages for building custom FHIR applications

## Prerequisites

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) or later
- Docker (optional, for SQL Server)

## Option 1: Run the FHIR Server

### Using Docker (Recommended)

Pull and run the official Docker image:

```bash
docker pull ghcr.io/brendankowitz/ignixa-fhir:release

docker run -p 8080:8080 ghcr.io/brendankowitz/ignixa-fhir:release
```

Access the FHIR metadata endpoint at `http://localhost:8080/metadata`.

### Using Docker Compose (with SQL Server)

For a production-like environment with SQL Server:

```bash
# Clone the repository
git clone https://github.com/brendankowitz/ignixa-fhir.git
cd ignixa-fhir

# Create environment file
cp .env.example .env
# Edit .env and set SQL_SA_PASSWORD

# Start the stack
docker compose up -d
```

### From Source

```bash
# Clone the repository
git clone https://github.com/brendankowitz/ignixa-fhir.git
cd ignixa-fhir

# Build the solution
dotnet build All.sln

# Run the API
cd src/Application/Ignixa.Api
dotnet run
```

The server starts at `https://localhost:5001/metadata` by default.

## Option 2: Install Core SDK Packages

Install individual packages using the .NET CLI:

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

Ignixa also provides command-line tools:

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

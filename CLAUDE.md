# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a C# .NET 9.0 codebase for FHIR Server contributions and experiments. The project is organized as a Visual Studio solution (`All.sln`) with multiple class library projects focused on extending and enhancing FHIR server functionality.

## Architecture

The codebase follows a modular architecture with distinct separation of concerns:

- **Base Libraries** (organized under "Base" solution folder):
  - `Microsoft.Health.Fhir.Extensions` - Core FHIR extensions and utilities
  - `Microsoft.Health.Fhir.Extensions.Tests` - Unit tests for extensions
  - `Microsoft.Health.Fhir.Search.Extensions` - Search-related FHIR extensions
  - `Microsoft.Health.Fhir.Specification.Extensions` - FHIR specification extensions
  - `Microsoft.Health.Fhir.SourceNodeSerialization` - Custom serialization for FHIR source nodes
  - `Microsoft.Health.Fhir.SourceNodeSerialization.UnitTests` - Tests for serialization

- **Server Libraries** (organized under "Server" solution folder):
  - `Microsoft.Health.Fhir.Core` - Core FHIR server functionality with embedded resources

The projects use shared dependencies and configuration through `Directory.Build.props`, which defines common package versions for the Healthcare Shared libraries (v7.1.5), Hl7.Fhir (v4.3.0), and targets .NET 9.0.

## Build Status

**✅ Successfully Compiling Projects (4/7):**
- `Microsoft.Health.Fhir.Extensions` - Core FHIR extensions and utilities
- `Microsoft.Health.Fhir.SourceNodeSerialization` - Custom serialization for FHIR source nodes
- `Microsoft.Health.Fhir.Search.Extensions` - Search-related FHIR extensions
- `Microsoft.Health.Fhir.Specification.Extensions` - FHIR specification extensions

**⚠️ Projects with Issues (3/7):**
- `Microsoft.Health.Fhir.Core` - Missing many FHIR-specific types from full server codebase
- `Microsoft.Health.Fhir.Extensions.Tests` - Missing extension methods and types, requires API updates
- `Microsoft.Health.Fhir.SourceNodeSerialization.UnitTests` - Missing test utilities from full server codebase

## Common Commands

### Build
```bash
# Build all projects (3 will fail due to missing dependencies)
dotnet build All.sln

# Build individual working projects
dotnet build src/Microsoft.Health.Fhir.Extensions/Microsoft.Health.Fhir.Extensions.csproj
dotnet build src/Microsoft.Health.Fhir.SourceNodeSerialization/Microsoft.Health.Fhir.SourceNodeSerialization.csproj
dotnet build src/Microsoft.Health.Fhir.Search.Extensions/Microsoft.Health.Fhir.Search.Extensions.csproj
dotnet build src/Microsoft.Health.Fhir.Specification.Extensions/Microsoft.Health.Fhir.Specification.Extensions.csproj
```

### Test
```bash
# Run all tests (some projects may fail to build)
dotnet test All.sln

# Test individual projects that compile successfully
dotnet test src/Microsoft.Health.Fhir.Extensions.Tests/Microsoft.Health.Fhir.Extensions.Tests.csproj
```

## Code Standards

- **StyleCop**: Configured via `stylecop.json` with Microsoft Corporation copyright headers
- **Code Analysis**: Latest analysis level enabled with code style enforcement in build
- **Warnings as Errors**: Enabled with specific suppressions for SA (StyleCop) and CA (Code Analysis) rules
- **Indentation**: 4 spaces, no tabs
- **Using Directives**: System usings first, placed outside namespace

## Key Dependencies

- **Hl7.Fhir libraries**: Core FHIR functionality (v3.8.1)
- **Microsoft.Health libraries**: Healthcare-specific shared utilities (v4.0.28)
- **xUnit**: Testing framework
- **Autofac**: Dependency injection container
- **MediatR**: Mediator pattern implementation
- **FluentValidation**: Validation library

## FHIR Resource Data

The `Microsoft.Health.Fhir.Core` project contains embedded JSON resources for different FHIR versions:
- R4, R5, and STU3 capabilities and search parameters
- Operation definitions for export, reindex, and other FHIR operations
- Security role schemas
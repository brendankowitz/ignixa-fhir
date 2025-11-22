# Gemini Code Assistant Context

This document provides context for the Gemini Code Assistant to understand the Ignixa FHIR Server project.

## Project Overview

Ignixa is a high-performance, multi-tenant FHIR server built with .NET 9. It is designed with a clean architecture, separating concerns into distinct layers: Domain, Application, DataLayer, and API. The project follows the CQRS pattern using Medino for messaging.

### Key Technologies
- **Framework:** .NET 9
- **Architecture:** Clean Architecture, CQRS
- **FHIR Versions:** R4, R4B, R5, STU3
- **Database Support:** File System (default), SQL Server, Azure Blob Storage (planned)
- **API:** ASP.NET Core Minimal API
- **Async:** DurableTask for background operations
- **DI Container:** Autofac
- **Testing:** xUnit, NSubstitute, FluentAssertions

### Project Structure

The solution is organized into several key directories:

- `src/`: Contains the main source code for the application, divided by architectural layer.
  - `Ignixa.Api/`: The main entry point of the application (ASP.NET Core Minimal API).
  - `Ignixa.Application/`: Contains the business logic and CQRS handlers.
  - `Ignixa.Domain/`: Contains the core domain models and abstractions.
  - `Ignixa.DataLayer.*/`: Contains the data access logic for different storage backends.
- `test/`: Contains the unit and integration tests for the project.
- `codegen/`: Contains scripts and tools for code generation.
- `docs/`: Contains architecture decision records (ADRs) and other documentation.
- `All.sln`: The main solution file for the project.

## Building and Running

### Building the Project

To build the entire solution, run the following command from the root directory:

```bash
dotnet build All.sln
```

### Running the Server

To run the FHIR server, navigate to the API project and use `dotnet run`:

```bash
cd src/Ignixa.Api
dotnet run
```

The server will be available at `https://localhost:5001`.

### Running Tests

To run the test suite, execute the following command from the root directory:

```bash
dotnet test All.sln
```

## Development Conventions

### Coding Style

The project enforces a consistent coding style through:
- **StyleCop:** Rules are defined in `stylecop.json`.
- **EditorConfig:** Configuration is in `.editorconfig`.
- **Nullable Reference Types:** Enabled across the project.

### Code Generation

The project uses code generation for creating structure definition providers from official FHIR packages. To run the generation scripts, use:

```bash
cd codegen
./generate.ps1  # For PowerShell
./generate.sh   # For Bash
```

### Dependencies

Project dependencies are centrally managed in `Directory.Packages.props`.

### Documentation

Architectural decisions are documented in the `docs/adr` directory. Further development guidance for AI assistants can be found in `CLAUDE.md`.

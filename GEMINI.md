# Gemini Development Guide for the Ignixa FHIR Server

This document provides a comprehensive guide for understanding, building, and developing the Ignixa FHIR Server project.

## 1. Project Overview

Ignixa is a high-performance, multi-tenant FHIR (Fast Healthcare Interoperability Resources) server built on .NET 9. It is designed from the ground up using modern .NET patterns and a strict **Clean Architecture** to provide a scalable and extensible platform for healthcare data interoperability.

### Key Characteristics:

*   **Technologies:** .NET 9, C#, ASP.NET Core (Minimal APIs), Entity Framework Core, MediatR (via the `Medino` library for CQRS), Autofac (for dependency injection), and DurableTask (for background processing).
*   **Architecture:**
    *   **Clean Architecture:** A strict separation of concerns into four main layers: `Domain`, `Application`, `DataLayer`, and `Api`.
    *   **CQRS (Command Query Responsibility Segregation):** Business logic is encapsulated in `IRequest` records (Queries/Commands) and corresponding `IRequestHandler` classes.
    *   **Multi-Tenancy:** Supports data partitioning per tenant, with options for different storage backends like SQL Server or the local file system.
*   **Features:**
    *   Multi-version FHIR support (R4, R4B, R5, STU3).
    *   Complete RESTful API with streaming responses for performance.
    *   Advanced features like FHIRPath Patch operations, transaction bundles, and a multi-tier validation system.
    *   Asynchronous background jobs for bulk operations (`$export`, `$import`).

## 2. Building and Running

The project uses standard `dotnet` CLI commands for building, testing, and running.

### Build

To build the entire solution and ensure all dependencies are resolved:

```bash
dotnet build All.sln
```

The build should produce 0 warnings and 0 errors, as code analysis rules are enforced.

### Run the API Server

The main API project is `Ignixa.Api`. To run it locally:

```bash
cd src/Ignixa.Api
dotnet run
```

The server will start on `https://localhost:5001` and `http://localhost:5000`. You can test if it's running by accessing the capability statement:

```bash
curl https://localhost:5001/metadata
```

### Run Tests

The solution has extensive test coverage using xUnit. To run all tests:

```bash
dotnet test All.sln
```

All tests must pass before any changes are considered complete.

## 3. Development Conventions

The project adheres to a strict set of conventions to maintain code quality and architectural integrity.

### Architecture: The Four Layers

1.  **`Ignixa.Api` (Presentation Layer):**
    *   Contains ASP.NET Core Minimal API endpoints.
    *   Responsible for handling HTTP requests, forwarding them to the Application layer via MediatR, and formatting responses.
    *   **NO business logic** should be here.

2.  **`Ignixa.Application` (Application Layer):**
    *   The core of the business logic.
    *   Organized into features using the CQRS pattern. Each feature consists of a `Query` or `Command` (defined as a C# `record`) and a `Handler` class.
    *   Depends on the `Domain` layer but knows nothing about the `DataLayer` or `Api` layers.

3.  **`Ignixa.Domain` (Domain Layer):**
    *   Contains domain models, interfaces for repositories (e.g., `IFhirRepository`), and core abstractions.
    *   Has no external dependencies. It is the heart of the application.

4.  **`Ignixa.DataLayer.*` (Infrastructure Layer):**
    *   Contains concrete implementations of the interfaces defined in the `Domain` layer (e.g., `SqlFhirRepository`).
    *   Handles all interactions with databases, file systems, or other external services.

### Key Development Patterns

*   **One Type Per File:** Each class, record, or interface must be in its own file (e.g., `PatientQuery.cs` and `PatientQueryHandler.cs`).
*   **CQRS with MediatR:** New features are added by creating a new Query/Command record and a corresponding Handler in the `Ignixa.Application` project.
*   **Minimal APIs:** Endpoints are defined functionally in `*Endpoints.cs` files within the `Ignixa.Api` project. Do not use MVC Controllers.
*   **Testing:**
    *   Tests follow the **AAA (Arrange, Act, Assert)** pattern.
    *   Test method names use a BDD-style convention: `GivenContext_WhenAction_ThenResult`.
    *   New features or bug fixes **must** be accompanied by tests.

### Code Generation

The `Ignixa.Specification` project contains FHIR structure definitions that are generated from official HL7 packages. To re-generate this code, use the scripts in the `codegen` directory:

```bash
# For Windows (PowerShell)
cd codegen
./generate.ps1

# For Linux/macOS (Bash)
cd codegen
./generate.sh
```

### Important Documentation

*   **`README.md`:** The primary source for project overview, features, and setup instructions.
*   **`CLAUDE.md`:** A detailed development guide created specifically for AI assistants, containing architectural rules, common patterns, and checklists. **This is a crucial resource for understanding the "how-to" of development in this repo.**
*   **`docs/adr/`:** A directory of Architecture Decision Records (ADRs) that explain key design choices and the project's history.

### Git Workflow

**CRITICAL**: Never commit without user approval.
1. Make changes & test
2. Show diff + status
3. Ask: "Should I commit: [message]?"
4. Wait for confirmation
5. Execute git commit

---
slug: introducing-ignixa
title: Introducing Ignixa - A Modern FHIR Ecosystem for .NET
authors: [ignixa]
tags: [announcement, fhir, architecture]
---

**Ignixa** is a modular, high-performance FHIR ecosystem built on **.NET**. It serves as both a **Reference Server** and a suite of **Standalone Tools** for the modern health IT developer.

This post explains what we're building and why.

<!-- truncate -->

:::warning Project Status
**Advanced Research / Reference Implementation.** This is a personal project exploring "next-gen" architecture. It supports and tests advanced parts of the FHIR specification but is **not** a supported enterprise product.
:::

## The Missing Toolchain

You don't need to run the full server to use Ignixa's power. The core capabilities are available as standalone **CLI Tools** and **NuGet Packages**:

| Tool / Library | Status | What it does |
|:---|:---|:---|
| **Ignixa.SqlOnFhir** | Beta | Native .NET implementation of **SQL-on-FHIR v2**. Projects FHIR to tables/views. |
| **Ignixa.FhirFakes** | Stable | Generates massive datasets of realistic, synthetic patient data for load testing. |
| **Ignixa.FhirMappingLanguage** | Beta | A C# engine for the **FHIR Mapping Language**. Transpile and execute maps natively. |
| **Ignixa.Validation** | Stable | High-speed, three-tier validation (Structure -> Profile -> Terminology). |

## Why Another FHIR Server?

Building FHIR applications in .NET often means:

- **Heavy dependencies** on large SDK libraries with significant memory footprints
- **Version lock-in** where supporting multiple FHIR versions requires separate codebases
- **Monolithic architectures** that make it hard to use just the pieces you need

Ignixa takes a different approach.

## Core Design Principles

### ISourceNode-First Architecture

Rather than generating POCOs for every FHIR resource type, Ignixa works with `ISourceNode` - a lightweight abstraction over FHIR data structures:

- **Version agnostic**: The same code works across R4, R4B, R5, R6, and STU3
- **Low memory footprint**: No large object graphs
- **Zero-copy serialization**: Stream data without intermediate allocations

### Modular SDK Packages

The Core SDK is split into focused packages. Use what you need, leave the rest:

| Package | Purpose |
|---------|---------|
| `Ignixa.Abstractions` | Core interfaces (`ISourceNode`, `IElement`) |
| `Ignixa.Serialization` | High-performance JSON parsing and writing |
| `Ignixa.FhirPath` | Compiled expression engine with caching |
| `Ignixa.Validation` | Three-tier validation engine |
| `Ignixa.Search` | Parameter indexing and extraction |
| `Ignixa.FhirFakes` | Synthetic data generation |

### F5 Developer Experience

A core principle: **press F5 and it works**. No Docker compose files to memorize. No external services required for basic development. The server runs with filesystem storage by default, with SQL Server and blob storage as opt-in production features.

## The Reference Server

An implementation designed for **maximum throughput** and **architectural purity**:

- **Performance**: Built on Minimal APIs and zero-copy serialization for low-allocation processing
- **Architecture**: Strict Clean Architecture using CQRS (Medino) to separate domain logic from infrastructure
- **Advanced Features**: Native support for `$import`, `$export`, and SQL-on-FHIR view definitions

### Current Capabilities

**Server Features:**
- Full CRUD operations for all FHIR resource types
- Search with standard parameters
- Bundle processing (batch and transaction)
- Validation at configurable levels
- Multi-tenant routing with data isolation

**Infrastructure:**
- Docker images published to GHCR
- NuGet packages on NuGet.org
- CI/CD with automated releases

## What's Next

The roadmap focuses on:

1. **Operations** - Implementing `$validate`, `$expand`, and other FHIR operations
2. **Subscriptions** - FHIR R5 subscription framework
3. **Bulk Data** - Export and import at scale
4. **SMART on FHIR** - OAuth2 authorization scopes

## Get Involved

The project is open source under the MIT license.

- **GitHub**: [brendankowitz/ignixa-fhir](https://github.com/brendankowitz/ignixa-fhir)
- **Documentation**: [brendankowitz.github.io/ignixa-fhir](https://brendankowitz.github.io/ignixa-fhir/)
- **NuGet**: Search for `Ignixa.*` packages
- **Docker**: `ghcr.io/brendankowitz/ignixa-fhir:release`

Feedback, issues, and contributions are welcome.

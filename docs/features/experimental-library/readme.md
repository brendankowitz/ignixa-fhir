# Feature: Experimental Library

Self-contained library for isolating experimental features with configuration-driven toggles.

## Status

**Current**: Proposed

## Investigations

| Investigation | Status | Created | Description |
|--------------|--------|---------|-------------|
| [library-proposal](investigations/library-proposal.md) | Viable | 2025-12-18 | Ignixa.Application.Experimental library architecture and migration plan |

## Overview

This feature creates a dedicated `Ignixa.Application.Experimental` library to house experimental features in an isolated, self-contained manner with configuration-driven enable/disable controls.

### Key Components

- **Experimental Options** configuration with master switch and per-feature toggles
- **Self-contained Features** including endpoints, handlers, models, and services
- **Registration Infrastructure** for IServiceCollection and Autofac integration
- **Endpoint Registration** with conditional mapping based on configuration

### Features to Include

| Feature | Current Location | Status |
|---------|-----------------|--------|
| MCP Server | `Ignixa.Application/Features/Mcp/` | Migrated |
| $transform | `Ignixa.Application.Operations/Features/Transform/` | Migrated |
| Terminology | `Ignixa.Application.Operations/Features/Terminology/` | Migrated |

### Default Behavior

Experimental mode is **enabled by default** in the Docker image to provide full functionality out of the box. Production deployments can disable with:

```json
{
  "Experimental": {
    "Enabled": false
  }
}
```

## Alignment

- [x] Follows layer rules (API -> App -> Domain -> Data)
- [x] F5 Developer Experience (works with minimal setup)
- [x] Consistent with existing patterns
- [x] Configuration-driven feature flags

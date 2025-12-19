# Feature: MCP Integration

Model Context Protocol integration for AI-powered FHIR access.

## Investigations

| Investigation | Status | Created | Description |
|--------------|--------|---------|-------------|
| [mcp-overview](investigations/mcp-overview.md) | Proposed | 2025-11-08 | MCP server integration architecture and implementation plan |
| [tool-design](investigations/tool-design.md) | Proposed | 2025-11-08 | Design guidelines for LLM-optimized MCP tools |

## Overview

This feature enables AI agents and LLM applications to interact with FHIR data through the Model Context Protocol (MCP).

### Key Components

- **MCP Server** at `/mcp/sse` endpoint using SSE transport
- **FHIR Operations Tools** for search, read, and data access
- **Admin Operations Tools** for package management and job monitoring
- **Terminology Tools** for ValueSet expansion and code validation
- **LLM Optimization** with result limits and field selection

### Implementation Phases

1. **Foundation** (2-3 weeks): MCP server setup, basic FHIR tools
2. **Admin Operations** (2-3 weeks): Package and job management tools
3. **Advanced Features** (2 weeks): Terminology, validation, prompts

Total estimated effort: 6-8 weeks

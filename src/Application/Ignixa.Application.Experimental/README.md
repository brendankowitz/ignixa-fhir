# Ignixa.Application.Experimental

Experimental features for Ignixa FHIR Server. This library provides a self-contained module for features that are not yet considered stable for production use.

## Features

- **MCP Server** - Model Context Protocol integration for AI-assisted FHIR operations
- **$transform Operation** - FHIR Mapping Language transformation
- **Terminology Operations** - $expand, $translate, $subsumes

## Configuration

Experimental features are controlled via configuration:

```json
{
  "Experimental": {
    "Enabled": true,
    "Features": {
      "Mcp": { "Enabled": true },
      "Transform": { "Enabled": true },
      "Terminology": { "Enabled": true }
    }
  }
}
```

## Default Behavior

Experimental mode is **enabled by default** in the Docker image to provide full functionality out of the box.

## Disabling Experimental Features

To disable all experimental features in production:

```json
{
  "Experimental": {
    "Enabled": false
  }
}
```

## Feature Graduation

Features that prove stable and widely adopted will be promoted to the core `Ignixa.Application` or `Ignixa.Application.Operations` libraries.

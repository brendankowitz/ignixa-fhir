# Ignixa.DataLayer.FileSystem

This library provides a file system-based storage implementation for the Ignixa FHIR Server.

## Description

It implements the data access interfaces defined in `Ignixa.Domain` using the local file system. This is primarily useful for:
- Development and testing
- Small-scale deployments
- "Box" environments where a full database server is not available

**Note:** This is an internal component of the Ignixa FHIR Server and is not intended to be used directly by external applications.

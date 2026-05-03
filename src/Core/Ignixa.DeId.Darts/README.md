# Ignixa.DeId.Darts

DARTS (Data Anonymous and Re-identification Technical Standards) FHIR IG support for `Ignixa.DeId`.

## Features

- **Library Configuration Loader**: Load `DeIdOptions` from FHIR `Library` resources with base64-encoded JSON attachments
- **DARTS Policy Support**: Built-in constants for Safe Harbor and Expert Determination policies

## Installation

```bash
dotnet add package Ignixa.DeId.Darts
```

## Usage

```csharp
using Ignixa.DeId.Darts.Configuration;

var loader = new LibraryConfigurationLoader();
var options = loader.LoadFromLibrary(libraryResource);
```

## License

MIT License. See LICENSE file in the repository root.

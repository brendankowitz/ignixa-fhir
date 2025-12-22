---
sidebar_position: 2
title: Quick Start
description: Make your first FHIR requests
---

# Quick Start

Create and query FHIR resources using the REST API.

## Prerequisites

Start the server following the [Installation Guide](/docs/getting-started/installation), then verify it's running:

```bash
curl http://localhost:8080/metadata
```

## Create a Patient

```bash
curl -X POST http://localhost:8080/Patient \
  -H "Content-Type: application/fhir+json" \
  -d '{
    "resourceType": "Patient",
    "name": [{"family": "Smith", "given": ["John"]}],
    "gender": "male",
    "birthDate": "1990-05-15"
  }'
```

Note the `id` in the response for subsequent requests.

## Read

```bash
curl http://localhost:8080/Patient/{id}
```

## Search

```bash
curl "http://localhost:8080/Patient?family=Smith"
```

## Update

```bash
curl -X PUT http://localhost:8080/Patient/{id} \
  -H "Content-Type: application/fhir+json" \
  -d '{
    "resourceType": "Patient",
    "id": "{id}",
    "name": [{"family": "Smith", "given": ["John"]}],
    "gender": "male",
    "birthDate": "1990-05-15",
    "telecom": [{"system": "phone", "value": "+1-555-0123"}]
  }'
```

## Delete

```bash
curl -X DELETE http://localhost:8080/Patient/{id}
```

## Next Steps

- [Server Configuration](/docs/server/configuration) - Configure storage and features
- [Search Parameters](/docs/server/fhir/search-parameters) - Advanced search options
- [Supported Resources](/docs/server/fhir/supported-resources) - Full resource list
- [Core SDK](/docs/core-sdk/overview) - Build custom FHIR applications

---
sidebar_position: 2
title: Quick Start
description: Create and query FHIR resources in under 10 minutes
---

# Quick Start

This guide walks you through making your first FHIR requests with Ignixa. By the end, you'll have created a Patient resource and performed searches.

## Start the Server

If you haven't already, start Ignixa using Docker:

```bash
docker run -p 8080:8080 ghcr.io/brendankowitz/ignixa-fhir:release
```

## Verify the Server

Check the server is running by fetching the CapabilityStatement:

```bash
curl http://localhost:8080/metadata
```

You should see a JSON response describing the server's capabilities.

## Create a Patient

Create your first Patient resource:

```bash
curl -X POST http://localhost:8080/Patient \
  -H "Content-Type: application/fhir+json" \
  -d '{
    "resourceType": "Patient",
    "name": [{
      "use": "official",
      "family": "Smith",
      "given": ["John", "William"]
    }],
    "gender": "male",
    "birthDate": "1990-05-15"
  }'
```

The response includes the created resource with a server-assigned `id`:

```json
{
  "resourceType": "Patient",
  "id": "abc123",
  "meta": {
    "versionId": "1",
    "lastUpdated": "2024-01-15T10:30:00Z"
  },
  "name": [{
    "use": "official",
    "family": "Smith",
    "given": ["John", "William"]
  }],
  "gender": "male",
  "birthDate": "1990-05-15"
}
```

## Read a Patient

Retrieve the patient using the assigned ID:

```bash
curl http://localhost:8080/Patient/abc123
```

## Search for Patients

Search by family name:

```bash
curl "http://localhost:8080/Patient?family=Smith"
```

Search with multiple parameters:

```bash
curl "http://localhost:8080/Patient?gender=male&birthdate=gt1980-01-01"
```

## Create an Observation

Create an Observation linked to your Patient:

```bash
curl -X POST http://localhost:8080/Observation \
  -H "Content-Type: application/fhir+json" \
  -d '{
    "resourceType": "Observation",
    "status": "final",
    "code": {
      "coding": [{
        "system": "http://loinc.org",
        "code": "29463-7",
        "display": "Body Weight"
      }]
    },
    "subject": {
      "reference": "Patient/abc123"
    },
    "valueQuantity": {
      "value": 75,
      "unit": "kg",
      "system": "http://unitsofmeasure.org",
      "code": "kg"
    }
  }'
```

## Search with Includes

Fetch Observations and include the referenced Patient:

```bash
curl "http://localhost:8080/Observation?subject=Patient/abc123&_include=Observation:subject"
```

## Update a Patient

Update using PUT (full resource replacement):

```bash
curl -X PUT http://localhost:8080/Patient/abc123 \
  -H "Content-Type: application/fhir+json" \
  -d '{
    "resourceType": "Patient",
    "id": "abc123",
    "name": [{
      "use": "official",
      "family": "Smith",
      "given": ["John", "William"]
    }],
    "gender": "male",
    "birthDate": "1990-05-15",
    "telecom": [{
      "system": "phone",
      "value": "+1-555-0123",
      "use": "mobile"
    }]
  }'
```

## View History

View all versions of a resource:

```bash
curl http://localhost:8080/Patient/abc123/_history
```

## Delete a Patient

Delete the resource:

```bash
curl -X DELETE http://localhost:8080/Patient/abc123
```

## Using the .NET SDK

If you're building a .NET application, use the Core SDK packages:

```csharp
using Ignixa.Serialization;
using Ignixa.Abstractions;

// Parse FHIR JSON
var json = """
{
  "resourceType": "Patient",
  "name": [{ "family": "Smith", "given": ["John"] }]
}
""";

var sourceNode = JsonSourceNavigator.Parse(json);
var patient = sourceNode.ToSourceNode();

// Navigate the resource
var familyName = patient["name"][0]["family"].Text;
Console.WriteLine($"Family name: {familyName}");
```

## Next Steps

- [Configuration](/docs/getting-started/configuration) - Configure storage backends
- [FHIR Compliance](/docs/server/fhir/capability-statement) - Understand supported features
- [Core SDK](/docs/core-sdk/overview) - Build custom FHIR applications

/*
 * Low-tech FHIRPath performance sanity check
 * Compares Ignixa vs Firely for complex navigation over 10K iterations
 */

using System.Diagnostics;
using System.Text.Json;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.FhirPath;
using Ignixa.Abstractions;
using Ignixa.Extensions.FirelySdk;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;

const int iterations = 10_000;
const string complexExpression = "Patient.name.where(use='official').given.first()";

const string patientJson = """
{
  "resourceType": "Patient",
  "id": "example-123",
  "meta": {
    "versionId": "1",
    "lastUpdated": "2025-01-15T10:30:00Z"
  },
  "text": {
    "status": "generated",
    "div": "<div xmlns=\"http://www.w3.org/1999/xhtml\">John Doe</div>"
  },
  "identifier": [
    {
      "system": "http://hospital.example.org/patients",
      "value": "12345"
    }
  ],
  "active": true,
  "name": [
    {
      "use": "official",
      "family": "Doe",
      "given": ["John", "Michael"]
    }
  ],
  "gender": "male",
  "birthDate": "1985-07-15"
}
""";

Console.WriteLine("FHIRPath Performance Sanity Check");
Console.WriteLine("==================================");
Console.WriteLine($"Expression: {complexExpression}");
Console.WriteLine($"Iterations: {iterations:N0}");
Console.WriteLine();

// Setup Ignixa
var ignixaPatient = ResourceJsonNode.Parse(patientJson);
var schemaProvider = new R4CoreSchemaProvider();
var ignixaTyped = ignixaPatient.ToElement(schemaProvider);

// Setup Firely
var firelySource = Hl7.Fhir.Serialization.FhirJsonNode.Parse(patientJson);
var firelyTyped = firelySource.ToTypedElement(ModelInfo.ModelInspector);

// Warmup both engines (caches AST, delegates, etc.)
Console.WriteLine("Warming up caches...");
var ignixaWarmup = ignixaTyped.Select(complexExpression).ToArray();
var firelyWarmup = firelyTyped.Select(complexExpression).ToArray();
Console.WriteLine($"Ignixa warmup returned {ignixaWarmup.Length} elements");
Console.WriteLine($"Firely warmup returned {firelyWarmup.Length} elements");
Console.WriteLine();

// Verify both return same result
var ignixaResults = ignixaTyped.Select(complexExpression).ToArray();
var firelyResults = firelyTyped.Select(complexExpression).ToArray();

if (ignixaResults.Length == 0 || firelyResults.Length == 0)
{
    Console.WriteLine($"⚠ ERROR: No results returned! Ignixa: {ignixaResults.Length}, Firely: {firelyResults.Length}");
    Environment.Exit(1);
}

var ignixaResult = ignixaResults[0].Value?.ToString();
var firelyResult = firelyResults[0].Value?.ToString();
Console.WriteLine($"✓ Both engines return: '{ignixaResult}'");
if (ignixaResult != firelyResult)
{
    Console.WriteLine($"⚠ WARNING: Results differ! Ignixa: '{ignixaResult}', Firely: '{firelyResult}'");
}
Console.WriteLine();

// Benchmark Ignixa
Console.WriteLine("Running Ignixa...");
var ignixaStopwatch = Stopwatch.StartNew();
for (int i = 0; i < iterations; i++)
{
    _ = ignixaTyped.Select(complexExpression).ToArray();
}
ignixaStopwatch.Stop();

// Benchmark Firely
Console.WriteLine("Running Firely...");
var firelyStopwatch = Stopwatch.StartNew();
for (int i = 0; i < iterations; i++)
{
    _ = firelyTyped.Select(complexExpression).ToArray();
}
firelyStopwatch.Stop();

// Results
var ignixaMs = ignixaStopwatch.Elapsed.TotalMilliseconds;
var firelyMs = firelyStopwatch.Elapsed.TotalMilliseconds;
var speedup = firelyMs / ignixaMs;

Console.WriteLine();
Console.WriteLine("Results");
Console.WriteLine("-------");
Console.WriteLine($"Ignixa:  {ignixaMs,8:N2} ms  ({ignixaMs / iterations,8:N4} ms/iteration)");
Console.WriteLine($"Firely:  {firelyMs,8:N2} ms  ({firelyMs / iterations,8:N4} ms/iteration)");
Console.WriteLine();
Console.WriteLine($"Speedup: {speedup:N2}x faster with Ignixa");
Console.WriteLine();

if (speedup < 5.0)
{
    Console.WriteLine($"⚠ WARNING: Expected >5x speedup, got {speedup:N2}x");
    Environment.Exit(1);
}
else
{
    Console.WriteLine($"✓ Performance check PASSED (>5x speedup achieved)");
    Environment.Exit(0);
}

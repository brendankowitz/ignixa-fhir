/*
 * Low-tech FHIRPath performance sanity check.
 *
 * A smoke test, not a source of published figures. It runs three lanes for one expression:
 *
 *   Ignixa (native element)   - Select() on SchemaAwareElement
 *   Firely (POCO-backed)      - Select() on a PocoNode, the like-for-like comparison and the gate
 *   Firely (source-backed)    - Select() on a source-backed ITypedElement, which re-deserializes the
 *                               whole resource per call; reported as an API cost, never as engine speed
 *
 * It under-reports relative to BenchmarkDotNet - roughly 3.5x here against ~10x measured properly for
 * the same expression - because a Stopwatch loop in a shared process cannot match BDN's isolation,
 * steady-state detection and statistical validation. Quote bench/Ignixa.Benchmarks, not this. The value
 * of this tool is that it fails loudly and in seconds when the compiled path regresses badly.
 *
 * Two mistakes this file previously made, both worth not repeating: it bound `ToElement` to the Firely
 * bridge overload and timed the interop adapter while calling it Ignixa, and it published a ~200x
 * "speedup" that was really Firely's per-call POCO conversion.
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

// Two lane sizes. The evaluation lanes are microsecond-scale, so a 10,000-iteration loop finished in
// well under a second and reported times several times worse than BenchmarkDotNet measures for the same
// work: the loop ended while tiered JIT was still promoting it. They now warm to steady state first and
// then run long enough to measure it. The source-backed Firely lane is millisecond-scale and needs far
// fewer iterations to be stable - it is also the lane whose cost is dominated by allocation, not JIT.
const int iterations = 100_000;
const int warmupIterations = 20_000;
const int slowIterations = 1_000;
const int slowWarmupIterations = 50;
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
// Bound explicitly to the native element model. `ignixaPatient.ToElement(schemaProvider)` is ambiguous
// in this file: `using Ignixa.Extensions.FirelySdk` brings an ISourceNode overload into scope that
// routes through the Firely bridge, and overload resolution had been selecting it - so this tool was
// timing the interop adapter and calling it Ignixa.
var ignixaTyped = SchemaAwareElementExtensions.ToElement(ignixaPatient.ToSourceNavigator(), schemaProvider);
Console.WriteLine($"Ignixa element model: {ignixaTyped.GetType().Name}");

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
for (int i = 0; i < warmupIterations; i++)
{
    _ = ignixaTyped.Select(complexExpression).ToArray();
}

var ignixaStopwatch = Stopwatch.StartNew();
for (int i = 0; i < iterations; i++)
{
    _ = ignixaTyped.Select(complexExpression).ToArray();
}
ignixaStopwatch.Stop();

// Benchmark Firely, source-backed. This is the lane that made the original comparison misleading:
// Firely's ITypedElement.Select(string) calls ToPocoNode, which re-deserializes the entire resource
// into POCOs on every call when the input is not already a PocoNode. It measures a real cost that a
// caller using this API actually pays, but it is model bridging, not evaluation.
Console.WriteLine("Running Firely (source-backed ITypedElement)...");
for (int i = 0; i < slowWarmupIterations; i++)
{
    _ = firelyTyped.Select(complexExpression).ToArray();
}

var firelyStopwatch = Stopwatch.StartNew();
for (int i = 0; i < slowIterations; i++)
{
    _ = firelyTyped.Select(complexExpression).ToArray();
}
firelyStopwatch.Stop();

// Benchmark Firely, POCO-backed: the like-for-like lane. Both sides now call the same shape of public
// API - Select(expression) on an element - and both pay a cache lookup plus evaluation per call. The
// only thing removed is the per-call model conversion, which the lane above measures separately.
// Comparing Ignixa's Select() against Firely's raw pre-compiled delegate would be the same
// apples-to-oranges error as the source-backed lane, only pointed the other way.
Console.WriteLine("Running Firely (POCO-backed, cached compile)...");
var firelyPocoNode = firelyTyped.ToPocoNode(rootName: firelyTyped.Location);
_ = firelyPocoNode.Select(complexExpression).ToArray();

for (int i = 0; i < warmupIterations; i++)
{
    _ = firelyPocoNode.Select(complexExpression).ToArray();
}

var firelyPocoStopwatch = Stopwatch.StartNew();
for (int i = 0; i < iterations; i++)
{
    _ = firelyPocoNode.Select(complexExpression).ToArray();
}
firelyPocoStopwatch.Stop();

// Results. The lanes run different iteration counts, so every ratio is computed from per-iteration
// times rather than from the stopwatch totals.
var ignixaNs = ignixaStopwatch.Elapsed.TotalMilliseconds * 1_000_000.0 / iterations;
var firelySourceNs = firelyStopwatch.Elapsed.TotalMilliseconds * 1_000_000.0 / slowIterations;
var firelyPocoNs = firelyPocoStopwatch.Elapsed.TotalMilliseconds * 1_000_000.0 / iterations;

var speedup = firelySourceNs / ignixaNs;
var likeForLike = firelyPocoNs / ignixaNs;

Console.WriteLine();
Console.WriteLine("Results (per iteration)");
Console.WriteLine("-----------------------");
Console.WriteLine($"Ignixa (native element):  {ignixaNs,12:N1} ns");
Console.WriteLine($"Firely (POCO-backed):     {firelyPocoNs,12:N1} ns");
Console.WriteLine($"Firely (source-backed):   {firelySourceNs,12:N1} ns");
Console.WriteLine();
Console.WriteLine($"Like-for-like speedup:  {likeForLike:N2}x   <- the number to quote");
Console.WriteLine($"API-cost ratio:         {speedup:N2}x   (includes Firely's per-call POCO conversion)");
Console.WriteLine();

// The gate is on the like-for-like lane. Gating on the source-backed ratio would have passed on the
// strength of Firely re-deserializing the resource, which says nothing about this engine.
const double MinimumLikeForLikeSpeedup = 3.0;

if (likeForLike < MinimumLikeForLikeSpeedup)
{
    Console.WriteLine($"⚠ WARNING: Expected >{MinimumLikeForLikeSpeedup:N0}x like-for-like speedup, got {likeForLike:N2}x");
    Environment.Exit(1);
}

Console.WriteLine($"✓ Performance check PASSED ({likeForLike:N2}x like-for-like)");
Environment.Exit(0);

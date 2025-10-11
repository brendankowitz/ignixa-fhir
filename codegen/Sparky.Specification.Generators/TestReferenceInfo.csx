#r "nuget: Hl7.Fhir.R4, 5.10.2"
using Hl7.Fhir.Model;

// Simple test to see what's in a TypeRefComponent
var typeRef = new ElementDefinition.TypeRefComponent();
typeRef.Code = "Reference";

Console.WriteLine($"Properties available on TypeRefComponent:");
Console.WriteLine($"  Code: {typeRef.Code}");
Console.WriteLine($"  Profile: {typeRef.Profile?.Count ?? 0} items");
Console.WriteLine($"  TargetProfile: {typeRef.TargetProfile?.Count ?? 0} items");
Console.WriteLine($"  Aggregation: {typeRef.Aggregation?.Count ?? 0} items");
Console.WriteLine($"  Versioning: {typeRef.Versioning}");

Console.WriteLine("\nTargetProfile property type: " + typeRef.TargetProfile?.GetType().Name);

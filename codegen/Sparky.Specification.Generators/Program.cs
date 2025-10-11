// <copyright file="Program.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Microsoft.Health.Fhir.CodeGen.Configuration;
using Microsoft.Health.Fhir.CodeGen.Loader;
using Microsoft.Health.Fhir.CodeGen.Models;
using Sparky.Specification.Generators;

Console.WriteLine("Sparky FHIR Structure Definition Provider Generator");
Console.WriteLine("====================================================");

// Parse command line arguments
string fhirVersion = args.Length > 0 ? args[0] : "R4";
string outputDir = args.Length > 1 ? args[1] : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Sparky.Specification", "Generated");

// Map FHIR version to package name
string packageId = fhirVersion.ToUpperInvariant() switch
{
    "R4" => "hl7.fhir.r4.core#4.0.1",
    "R4B" => "hl7.fhir.r4b.core#4.3.0",
    "R5" => "hl7.fhir.r5.core#5.0.0",
    "STU3" => "hl7.fhir.r3.core#3.0.2",
    _ => throw new ArgumentException($"Unsupported FHIR version: {fhirVersion}")
};

Console.WriteLine($"FHIR Version: {fhirVersion}");
Console.WriteLine($"Package: {packageId}");
Console.WriteLine($"Output Directory: {outputDir}");
Console.WriteLine();

// Create package loader configuration
var config = new ConfigRoot
{
    UseOfficialRegistries = true,
    AutoLoadExpansions = true
};

Console.WriteLine("Loading FHIR package...");
// Pass null for LoaderOptions to avoid SDK version conflicts
var loader = new PackageLoader(config, null);
DefinitionCollection? definitions = await loader.LoadPackages([packageId]);

if (definitions == null)
{
    Console.WriteLine("✗ Failed to load package");
    return 1;
}

Console.WriteLine($"Loaded {definitions.ResourcesByName.Count} resources");
Console.WriteLine($"Loaded {definitions.ComplexTypesByName.Count} complex types");
Console.WriteLine($"Loaded {definitions.PrimitiveTypesByName.Count} primitive types");
Console.WriteLine();

// Generate the provider
Console.WriteLine("Generating provider code...");
var language = new CSharpStructureProviderLanguage();
var providerConfig = new CSharpStructureProviderConfig
{
    OutputDirectory = Path.GetFullPath(outputDir),
    Namespace = "Sparky.Specification.Generated"
};

language.Export(providerConfig, definitions);

Console.WriteLine();
Console.WriteLine("✓ Generation complete!");
return 0;

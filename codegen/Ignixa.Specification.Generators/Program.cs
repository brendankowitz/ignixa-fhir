// <copyright file="Program.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Microsoft.Health.Fhir.CodeGen.Configuration;
using Microsoft.Health.Fhir.CodeGen.Loader;
using Microsoft.Health.Fhir.CodeGen.Models;
using Ignixa.Specification.Generators;

// Parse command line arguments
string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "structure";
mode = mode switch
{
    "search" => "search",
    "compartment" => "compartment",
    "codesystem" => "codesystem",
    "valueset" => "valueset",
    "invariant" => "invariant",
    _ => "structure"
};

string fhirVersion = (mode != "structure" && args.Length > 1) ? args[1] : (args.Length > 0 && mode == "structure") ? args[0] : "R4";

string defaultOutputDir = mode switch
{
    "search" or "compartment" or "codesystem" => Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Ignixa.Search", "Generated"),
    "valueset" => Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Ignixa.Specification", "ValueSets", "Normative"),
    "invariant" => Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Ignixa.Specification", "Generated"),
    _ => Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Ignixa.Specification", "Generated")  // structure mode: use Ignixa
};

string outputDir = args.Length > 2 ? args[2] :
                   args.Length > 1 && mode == "structure" ? args[1] :
                   defaultOutputDir;

string title = mode switch
{
    "search" => "Ignixa FHIR Search Parameter Generator",
    "compartment" => "Ignixa FHIR Compartment Definition Generator",
    "codesystem" => "Ignixa FHIR Code System Mappings Generator",
    "valueset" => "Ignixa FHIR ValueSet Enum Generator",
    "invariant" => "Ignixa FHIR Invariant Provider Generator",
    _ => "Ignixa FHIR Structure Definition Provider Generator"
};

Console.WriteLine(title);
Console.WriteLine("====================================================");

// Map FHIR version to package name
string packageId = fhirVersion.ToUpperInvariant() switch
{
    "R4" => "hl7.fhir.r4.core#4.0.1",
    "R4B" => "hl7.fhir.r4b.core#4.3.0",
    "R5" => "hl7.fhir.r5.core#5.0.0",
    "R6" => "hl7.fhir.r6.core#6.0.0-ballot2",
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

// Generate the output
switch (mode)
{
    case "search":
        Console.WriteLine("Generating search parameter code...");
        var searchLanguage = new CSharpSearchParameterLanguage();
        var searchConfig = new CSharpSearchParameterConfig
        {
            OutputDirectory = Path.GetFullPath(outputDir),
            Namespace = "Ignixa.Search.Generated"
        };
        searchLanguage.Export(searchConfig, definitions);
        break;

    case "compartment":
        Console.WriteLine("Generating compartment definition code...");
        var compartmentLanguage = new CSharpCompartmentLanguage();
        var compartmentConfig = new CSharpCompartmentConfig
        {
            OutputDirectory = Path.GetFullPath(outputDir),
            Namespace = "Ignixa.Search.Generated"
        };
        compartmentLanguage.Export(compartmentConfig, definitions);
        break;

    case "codesystem":
        Console.WriteLine("Generating code system mappings code...");
        var codeSystemLanguage = new CSharpCodeSystemResolverLanguage();
        var codeSystemConfig = new CSharpCodeSystemResolverConfig
        {
            OutputDirectory = Path.GetFullPath(outputDir),
            Namespace = "Ignixa.Search.Generated"
        };
        codeSystemLanguage.Export(codeSystemConfig, definitions);
        break;

    case "valueset":
        Console.WriteLine("Generating ValueSet enum code...");
        var valueSetLanguage = new CSharpValueSetLanguage();
        var valueSetConfig = new CSharpValueSetConfig
        {
            OutputDirectory = Path.GetFullPath(outputDir),
            Namespace = "Ignixa.Specification.ValueSets.Normative"
        };
        valueSetLanguage.Export(valueSetConfig, definitions);
        break;

    case "invariant":
        Console.WriteLine("Generating invariant provider code...");
        var invariantLanguage = new CSharpInvariantLanguage();
        var invariantConfig = new CSharpInvariantConfig
        {
            OutputDirectory = Path.GetFullPath(outputDir),
            Namespace = "Ignixa.Specification.Generated"
        };
        invariantLanguage.Export(invariantConfig, definitions);
        break;

    default: // structure
        Console.WriteLine("Generating provider code...");
        var structureLanguage = new CSharpStructureProviderLanguage();
        var providerConfig = new CSharpStructureProviderConfig
        {
            OutputDirectory = Path.GetFullPath(outputDir),
            Namespace = "Ignixa.Specification.Generated"
        };
        structureLanguage.Export(providerConfig, definitions);
        break;
}

Console.WriteLine();
Console.WriteLine("✓ Generation complete!");
return 0;

# Ignixa.FhirPath.Generators

Roslyn source generator for FHIRPath function registration. Generates
`SymbolTable.RegisterStandardFunctions()` from methods annotated with
`[FhirPathFunction]`, eliminating hand-maintained registration code.

## Usage

Reference as an analyzer alongside `Ignixa.FhirPath`:

```xml
<PackageReference Include="Ignixa.FhirPath" />
<PackageReference Include="Ignixa.FhirPath.Generators" PrivateAssets="all" />
```

Annotate static methods with `[FhirPathFunction("name")]` and the generator emits the
registration plumbing at compile time.

Part of the [Ignixa FHIR](https://github.com/brendankowitz/ignixa-fhir) Core SDK.

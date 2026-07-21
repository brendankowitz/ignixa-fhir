# Ignixa.TestScript.Suites

Canonical FHIR `TestScript` conformance suites for the [Ignixa](https://github.com/brendankowitz/ignixa-fhir)
TestScript engine, shipped as NuGet content.

Adding a `PackageReference` copies the suites into your build output under `testscripts/`,
preserving category subfolders:

```
testscripts/
  Bundles/ CRUD/ Foundation/ Microsoft/ Operations/
  Regression/ Search/ Subscriptions/ Validation/
  source-revision.txt
```

Resolve them at runtime with `Path.Combine(AppContext.BaseDirectory, "testscripts")`.

`source-revision.txt` holds the exact `ignixa-fhir` commit the suites were packed from, so
a report can link to a permalink rather than a moving `main` ref.

## Running them

```bash
dotnet tool install -g Ignixa.ConformanceMatrix.Cli
ignixa-matrix run --server https://your-fhir-server --tests ./testscripts \
  --impl my-server --out ./reports/my-server.json
```

## Extensions

Several suites use the four Ignixa TestScript extensions described in
[ADR 2607](https://github.com/brendankowitz/ignixa-fhir/blob/main/docs/adr/adr-2607-testscript-extensions.md).
Three are ignore-safe on a plain engine; suites using `fhirfakes` require an engine that
understands it. Suites and engine are versioned together for this reason.

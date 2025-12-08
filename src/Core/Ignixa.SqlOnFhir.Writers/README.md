# Ignixa.SqlOnFhir.Writers

Writers for SQL on FHIR ViewDefinitions, providing output in various formats.

## Features

- **Parquet Writer**: Convert FHIR resources using ViewDefinitions into Parquet format
- **CSV Writer**: Convert FHIR resources using ViewDefinitions into CSV format
- **Schema Extraction**: Extract schema from ViewDefinitions for preview and validation

## Usage

### Parquet Writer

```csharp
var writer = new ParquetFileWriter(
    outputPath,
    schema,
    logger,
    columnTypeMap);

// Write rows
foreach (var row in rows)
{
    await writer.WriteRowAsync(row, cancellationToken);
}

await writer.FlushAsync(cancellationToken);
```

### CSV Writer

```csharp
var writer = new CsvFileWriter(outputPath, logger);

// Write rows
foreach (var row in rows)
{
    await writer.WriteRowAsync(row, cancellationToken);
}

await writer.FlushAsync(cancellationToken);
```

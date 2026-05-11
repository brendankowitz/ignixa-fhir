# Ignixa.SqlOnFhir.Writers

Writers for SQL on FHIR ViewDefinitions, providing output in various formats.

## Features

- **Parquet Writer**: Convert FHIR resources using ViewDefinitions into Parquet format
- **CSV Writer**: Convert FHIR resources using ViewDefinitions into CSV format
- **NDJSON Writer**: Convert rows into newline-delimited JSON for streaming pipelines
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

### NDJSON Writer

```csharp
var writer = new NdjsonFileWriter(outputPath, logger);

foreach (var row in rows)
{
    await writer.WriteRowAsync(row, cancellationToken);
}

await writer.FlushAsync(cancellationToken);

Console.WriteLine($"Rows: {writer.RowsWritten}, bytes: {writer.BytesWritten}");
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

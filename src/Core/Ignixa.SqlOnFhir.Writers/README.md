# Ignixa.SqlOnFhir.Writers

Writers for SQL on FHIR ViewDefinitions, providing output in various formats.

## Features

- **Parquet Writer**: Convert FHIR resources using ViewDefinitions into Parquet format
- **CSV Writer**: Convert FHIR resources using ViewDefinitions into CSV format
- **NDJSON Writer**: Convert rows into newline-delimited JSON for streaming pipelines
- **Schema Extraction**: Extract schema from ViewDefinitions for preview and validation

## Behaviour notes

- **No byte order mark.** `CsvFileWriter` and `NdjsonFileWriter` write UTF-8 without a BOM. Earlier versions started both files with `EF BB BF`, which put a stray character in front of the first NDJSON line (making it invalid JSON for a strict reader) and in front of the first CSV header name. Excel opens a BOM-less UTF-8 CSV in the system code page unless the data is imported as UTF-8 (Data > From Text/CSV); if you need the BOM for Excel, write `0xEF 0xBB 0xBF` to the stream before the writer's first row, using the stream constructor below.
- **Stream constructors.** Each writer also takes a `Stream` (`new CsvFileWriter(stream, logger)`, `new NdjsonFileWriter(stream, logger)`, `new ParquetFileWriter(stream, schema, logger, ...)`) in place of a path. The caller opens the stream (for example with `FileMode.CreateNew`, so the file is known to be new) and the writer owns it from then on: it writes, flushes and disposes it, and never reopens the file by path. The Parquet stream must be seekable.

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

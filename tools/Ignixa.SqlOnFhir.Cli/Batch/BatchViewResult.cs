namespace Ignixa.SqlOnFhir.Cli.Batch;

internal record BatchViewResult(
    string ViewDefinitionName,
    BatchViewStatus Status,
    long RowsWritten = 0,
    long BytesWritten = 0,
    double DurationSeconds = 0,
    string? OutputPath = null,
    string? SkipReason = null,
    string? ErrorMessage = null);

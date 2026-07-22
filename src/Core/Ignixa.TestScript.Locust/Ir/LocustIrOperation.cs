namespace Ignixa.TestScript.Locust.Ir;

/// <summary>
/// A compiled TestScript operation action describing a single HTTP interaction against the FHIR server.
/// </summary>
public sealed record LocustIrOperation : LocustIrAction
{
    public required string Type { get; init; }

    public required string Method { get; init; }

    public string? Resource { get; init; }

    public string? Url { get; init; }

    public string? Params { get; init; }

    public string? Accept { get; init; }

    public string? ContentType { get; init; }

    public string? SourceId { get; init; }

    public string? ResponseId { get; init; }

    public string? RequestId { get; init; }

    public bool EncodeRequestUrl { get; init; } = true;

    public IReadOnlyList<LocustIrHeader> Headers { get; init; } = [];

    public LocustIrWaitFor? WaitFor { get; init; }
}

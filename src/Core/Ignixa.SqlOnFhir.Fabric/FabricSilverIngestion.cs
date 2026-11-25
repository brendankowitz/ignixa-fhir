// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.SqlOnFhir.Fabric;

/// <summary>
/// The lineage a caller supplies for one projected resource: which source system it came from, which file carried it,
/// and when it was ingested. Immutable once constructed.
/// </summary>
public sealed class FabricSilverIngestion
{
    /// <param name="sourceNamespace">
    /// The source system, usually its FHIR base URL. Silver ids are hashed from it after removing its scheme and one
    /// trailing slash, so two systems differing only in those parts share ids. Written verbatim to
    /// <c>msftSourceSystem</c> and to the system of every identifier the projection adds.
    /// </param>
    /// <param name="filePath">Written verbatim to <c>msftFilePath</c> (the form Silver expects there is unverified).</param>
    /// <param name="ingestedAt">
    /// The ingestion instant, in UTC (offset zero). Silver stores microseconds, so finer precision is truncated.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The namespace is blank or has nothing left after its scheme is removed, the file path is blank, the namespace
    /// or file path is not valid Unicode (a lone surrogate would be stored replaced), or the instant is not in UTC.
    /// </exception>
    public FabricSilverIngestion(string sourceNamespace, string filePath, DateTimeOffset ingestedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceNamespace);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        RequireWellFormed(sourceNamespace, nameof(sourceNamespace));
        RequireWellFormed(filePath, nameof(filePath));
        if (ingestedAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The ingestion instant must be expressed in UTC.", nameof(ingestedAt));
        }

        StrippedNamespace = SilverIdHash.Strip(sourceNamespace)
            ?? throw new ArgumentException("The source namespace has no content after its scheme.", nameof(sourceNamespace));
        SourceNamespace = sourceNamespace;
        FilePath = filePath;
        IngestedAt = SilverTemporal.TruncateToMicroseconds(ingestedAt);
    }

    public string SourceNamespace { get; }

    public string FilePath { get; }

    public DateTimeOffset IngestedAt { get; }

    /// <summary>The namespace as it takes part in Silver ids and base comparisons.</summary>
    internal string StrippedNamespace { get; }

    private static void RequireWellFormed(string value, string parameterName)
    {
        if (!SilverSourceText.IsWellFormed(value))
        {
            throw new ArgumentException("The value is not valid Unicode.", parameterName);
        }
    }
}

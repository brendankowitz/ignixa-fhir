// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Indexing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// The capture drops the indexer's "element 'canonical' is not supported" warning only for a parameter whose canonicals
/// were indexed, recognising it from the event's structured state rather than its text. Guards of the structured filter,
/// mutation-checked (at 78d8b45 the filter matched the message text in <c>FolderIndexBuilder</c>, behind no API these can
/// call); the indexer's real event is pinned by <see cref="SharedIndexingTests"/>' <c>_profile</c> rows.
/// </summary>
public class ExtractionFailureCaptureTests
{
    private const string Category = "Indexer";

    private const string Profile = "http://hl7.org/fhir/SearchParameter/Resource-profile";

    private const string Message = "The FHIR element '{ElementType}' is not supported for search parameter '{SearchParameterUrl}'.";

    private static readonly CatalogEntry Entry = new("Patient", "p1", "patients.ndjson", 0, 10, 1, false);

    private static readonly Action<ILogger, string, string, Exception?> NotSupported =
        LoggerMessage.Define<string, string>(LogLevel.Warning, new EventId(1, "FhirElementTypeNotSupported"), Message);

    private static readonly Action<ILogger, string, string, Exception?> SameTextOtherEvent =
        LoggerMessage.Define<string, string>(LogLevel.Warning, new EventId(2, "SomethingElse"), Message);

    [Fact]
    public void GivenTheCanonicalWarningForAnIndexedParameter_WhenCaptured_ThenItIsDropped()
    {
        Capture(logger => NotSupported(logger, "canonical", Profile, null), [Profile]).ShouldBeEmpty();
    }

    [Fact]
    public void GivenTheCanonicalWarningForAParameterNotIndexed_WhenCaptured_ThenItIsKept()
    {
        Capture(logger => NotSupported(logger, "canonical", Profile, null), ["http://example.org/other"]).ShouldHaveSingleItem();
    }

    [Fact]
    public void GivenTheWarningForAnotherElementType_WhenCaptured_ThenItIsKept()
    {
        Capture(logger => NotSupported(logger, "Narrative", Profile, null), [Profile]).ShouldHaveSingleItem();
    }

    [Fact]
    public void GivenTheSameTextFromAnotherEvent_WhenCaptured_ThenItIsKept()
    {
        Capture(logger => SameTextOtherEvent(logger, "canonical", Profile, null), [Profile])
            .ShouldHaveSingleItem().Code.ShouldBe(ExtractionFailureCapture.DiagnosticCode);
    }

    private static List<CatalogDiagnostic> Capture(Action<ILogger> log, HashSet<string> indexed)
    {
        using ILoggerFactory factory = ExtractionFailureCapture.Wrap(NullLoggerFactory.Instance, Category);
        ILogger logger = factory.CreateLogger(Category);

        return ExtractionFailureCapture.Capture(Entry, () => { log(logger); return 0; }, _ => indexed).Diagnostics;
    }
}

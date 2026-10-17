// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.RegularExpressions;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Session;

namespace Ignixa.CliOnFhir.Clock;

/// <summary>
/// The session's <see cref="AsOf"/>: the instant its answers are anchored to. Resolved once, on first use, in this order: the
/// session's <c>--as-of</c> option (a FHIR instant or date, or a form V14 accepted: see Parse); the
/// <c>transactionTime</c> of a Bulk Data export manifest at the folder's root (<see cref="BulkExportManifests"/>); the
/// <c>timestamp</c> of the folder's container Bundles, the latest when they differ (<see cref="BundleTimestamps"/>); otherwise
/// the session's clock (<see cref="FolderSession.UtcNow"/>). Detection only reads, and reads little: the manifest candidates at
/// the root, and of each container Bundle the bytes before its first catalogued entry and after its last. A call may name its own
/// instant (<see cref="ForCall"/>); a verb with a default of its own reads only what the caller stated (<see cref="Stated"/>).
/// Every <c>--as-of</c> and <c>asOf</c> argument is read here, by one grammar. Safe to use concurrently.
/// </summary>
public sealed partial class AsOfClock
{
    /// <summary>The error code of an <c>--as-of</c> (or <c>asOf</c> argument) that is not a date or a date and time.</summary>
    public const string InvalidCode = "invalid-as-of";

    private const string NotADateOrInstant = "not a FHIR date or dateTime: give a date, such as 2024-05-01, 2024-05 or 2024 (the first instant of that day, "
        + "month or year in UTC), or a date and time, such as 2024-05-01T08:30:00-05:00 (without a zone it is read as UTC)";

    private readonly FolderSession _session;
    private readonly string? _option;
    private readonly Lazy<AsOfResolution> _resolution;

    /// <param name="session">The folder, read for its data-pull time, and whose clock is the last resort.</param>
    /// <param name="sessionOption">The session's <c>--as-of</c> value as given; null when there is none.</param>
    public AsOfClock(FolderSession session, string? sessionOption)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;
        _option = sessionOption;

        // Not ExecutionAndPublication: that would keep an exception (a folder that could not be read) for the rest of the session.
        _resolution = new Lazy<AsOfResolution>(Detect, LazyThreadSafetyMode.PublicationOnly);
    }

    /// <summary>The session's instant, worked out on the first call and the same on every later one.</summary>
    /// <exception cref="ToolRequestException"><see cref="InvalidCode"/>: the session's <c>--as-of</c> is not a FHIR instant or date.</exception>
    public AsOfResolution Resolve() => _resolution.Value;

    /// <summary>The instant of one call: its own <paramref name="argument"/> when it gives one, otherwise the session's.</summary>
    /// <exception cref="ToolRequestException"><see cref="InvalidCode"/>: the argument, or the session's option, is not a FHIR instant or date.</exception>
    public AsOfResolution ForCall(string? argument) =>
        Argument(argument) is { } given ? new AsOfResolution(given, []) : Resolve();

    /// <summary>
    /// What the caller stated: the call's own <paramref name="argument"/>, else the session's <c>--as-of</c>; null when neither gives
    /// one, so that a verb with a default of its own applies it (<c>summary</c>: the patient's latest dated resource;
    /// <c>patient_cohort</c>: the clock). Nothing of the folder is read.
    /// </summary>
    /// <exception cref="ToolRequestException"><see cref="InvalidCode"/>: the argument, or the session's option, is not a FHIR instant or date.</exception>
    public AsOf? Stated(string? argument) =>
        Argument(argument) ?? (_option is null ? null : Parse(_option, "--as-of", AsOfSources.SessionOption));

    /// <summary>A call's own <c>asOf</c> argument, source <see cref="AsOfSources.Argument"/>; null when it gives none.</summary>
    /// <exception cref="ToolRequestException"><see cref="InvalidCode"/>: the argument is not a FHIR instant or date.</exception>
    public static AsOf? Argument(string? argument) => argument is null ? null : Parse(argument, "asOf", AsOfSources.Argument);

    private AsOfResolution Detect()
    {
        if (_option is not null)
        {
            return new AsOfResolution(Parse(_option, "--as-of", AsOfSources.SessionOption), []);
        }

        FolderCatalog catalog = _session.Catalog;
        (DateTimeOffset? manifest, List<IssueItem> issues) = BulkExportManifests.Detect(catalog.RootPath);
        if (manifest is { } transactionTime)
        {
            return new AsOfResolution(new AsOf(transactionTime, AsOfSources.BulkExportManifest), issues);
        }

        (DateTimeOffset? timestamp, IReadOnlyList<IssueItem> bundleIssues) = BundleTimestamps.Detect(catalog);
        issues.AddRange(bundleIssues);
        return timestamp is { } latest
            ? new AsOfResolution(new AsOf(latest, AsOfSources.BundleTimestamp), issues)
            : new AsOfResolution(new AsOf(ToMilliseconds(_session.UtcNow().ToUniversalTime()), AsOfSources.Clock), issues);
    }

    // The clock's own precision (ticks) says nothing the data could; an instant to the millisecond is how FHIR data usually reads.
    private static DateTimeOffset ToMilliseconds(DateTimeOffset now) => new(now.Ticks - (now.Ticks % TimeSpan.TicksPerMillisecond), now.Offset);

    // Surrounding whitespace is dropped, as patient_summary and patient_cohort always dropped it from their asOf. A full date and a
    // FHIR instant are read strictly (an instant keeps its offset and any number of fraction digits); the other forms V14 accepted
    // are read as V14 read them (LenientAsOf).
    private static AsOf Parse(string text, string name, string source)
    {
        string trimmed = text.Trim();
        if (FhirDate.TryParse(trimmed, out DateOnly day))
        {
            return AsOf.OfDate(day, source);
        }

        if (FhirInstant.Problem(trimmed, out DateTimeOffset value) is not { } problem)
        {
            return new AsOf(value, source);
        }

        if (LenientAsOf.Parse(trimmed, source) is { } lenient)
        {
            return lenient;
        }

        string reason = FhirDate.HasShape(trimmed) ? "not a date of the calendar" : FhirInstant.HasShape(trimmed) ? problem : NotADateOrInstant;
        throw ToolRequestException.Error(InvalidCode, $"'{name}' {Shown(text)} is {reason}.");
    }

    // The caller's own text, shown when it is shaped like a date and time, by its digest otherwise.
    private static string Shown(string text) => InstantLike().IsMatch(text) ? $"'{text}'" : $"(digest {FhirIds.Digest(text)})";

    [GeneratedRegex(@"^[0-9TZ:.+\-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex InstantLike();
}

// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Clock;

/// <summary>Where an <see cref="AsOf"/> instant came from, as facts name it.</summary>
public static class AsOfSources
{
    /// <summary>The call's own <c>asOf</c> argument, which overrides the session's value for that call.</summary>
    public const string Argument = "argument";

    /// <summary>The session's <c>--as-of</c> option.</summary>
    public const string SessionOption = "session-option";

    /// <summary>The <c>transactionTime</c> of a Bulk Data export manifest at the folder's root.</summary>
    public const string BulkExportManifest = "bulk-export-manifest";

    /// <summary>The <c>timestamp</c> of the folder's container Bundles (the latest when they differ).</summary>
    public const string BundleTimestamp = "bundle-timestamp";

    /// <summary>
    /// The start of the patient's own latest dated resource: the default of <c>summary</c> / <c>patient_summary</c>, which read one
    /// patient's record whole however long ago it ended or however far its dates are shifted.
    /// </summary>
    public const string PatientLatest = "patient-latest";

    /// <summary>
    /// The session's clock: the folder records no data-pull time (view, draft), the patient has no dated resource (summary), or the
    /// default of <c>patient_cohort</c>, which has always counted as of now.
    /// </summary>
    public const string Clock = "clock";

    /// <summary>A view's <c>--var asOf=...</c>, which replaced the session's instant for that run.</summary>
    public const string Variable = "variable";

    /// <summary>A ViewDefinition's own constant <c>asOf</c>, which the view reads instead of the session's instant.</summary>
    public const string ViewConstant = "view-constant";
}

// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;

namespace Ignixa.CliOnFhir.Commands;

/// <summary>Arguments and options shared by every verb.</summary>
internal static class CommonOptions
{
    public static Argument<string> CreatePathArgument() => new("path") { Description = "Folder of FHIR resources (JSON, NDJSON or Bundles)" };

    public static Option<string> CreateFhirVersionOption()
    {
        var option = new Option<string>("--fhir-version")
        {
            Description = "FHIR version of the folder's resources",
            DefaultValueFactory = _ => FhirSchemas.DefaultVersion,
        };
        option.AcceptOnlyFromAmong([.. FhirSchemas.SupportedVersions]);
        return option;
    }
}

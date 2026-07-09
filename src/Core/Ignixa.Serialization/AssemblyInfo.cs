// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Ignixa.Application")]
[assembly: InternalsVisibleTo("Ignixa.Application.Operations")]
[assembly: InternalsVisibleTo("Ignixa.Api")]
[assembly: InternalsVisibleTo("Ignixa.DeId")]
[assembly: InternalsVisibleTo("Ignixa.FhirFakes")]
[assembly: InternalsVisibleTo("Ignixa.FhirMappingLanguage")]
[assembly: InternalsVisibleTo("Ignixa.TestScript")]
[assembly: InternalsVisibleTo("Ignixa.TestScript.FhirFakes")]

// Generated typed-model packages use the shared JSON backing store.
[assembly: InternalsVisibleTo("Ignixa.Models.R4")]
[assembly: InternalsVisibleTo("Ignixa.Models.R5")]

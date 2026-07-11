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
[assembly: InternalsVisibleTo("Ignixa.Api.E2ETests")]
[assembly: InternalsVisibleTo("Ignixa.Models.Tests")]

// Reserved for planned generated typed-model packages (not yet present in this
// solution) that will share this JSON backing store.
[assembly: InternalsVisibleTo("Ignixa.Models.R4")]
[assembly: InternalsVisibleTo("Ignixa.Models.R5")]

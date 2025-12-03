// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;

// CA1720: Identifier contains type name - Justified: These are FHIR primitive type names from the specification
[assembly: SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "FHIR specification primitive type names", Scope = "member", Target = "~F:Ignixa.Abstractions.FhirPrimitive.Integer")]
[assembly: SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "FHIR specification primitive type names", Scope = "member", Target = "~F:Ignixa.Abstractions.FhirPrimitive.String")]
[assembly: SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "FHIR specification primitive type names", Scope = "member", Target = "~F:Ignixa.Abstractions.FhirPrimitive.Decimal")]
[assembly: SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "FHIR specification type name constants", Scope = "member", Target = "~F:Ignixa.Abstractions.FhirTypeConstants.Integer")]
[assembly: SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "FHIR specification type name constants", Scope = "member", Target = "~F:Ignixa.Abstractions.FhirTypeConstants.String")]
[assembly: SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "FHIR specification type name constants", Scope = "member", Target = "~F:Ignixa.Abstractions.FhirTypeConstants.Decimal")]

// CA1028: Byte-backed enum for performance - Justified: We want byte-sized enum for fast type checks and minimal memory
[assembly: SuppressMessage("Design", "CA1028:Enum Storage should be Int32", Justification = "Byte enum is intentional for performance (2ns type checks, minimal memory)", Scope = "type", Target = "~T:Ignixa.Abstractions.FhirPrimitive")]
[assembly: SuppressMessage("Design", "CA1028:Enum Storage should be Int32", Justification = "Byte enum is intentional for minimal memory footprint", Scope = "type", Target = "~T:Ignixa.Abstractions.FhirVersion")]

// CA1008: FhirVersion enum doesn't need None=0 - Justified: FHIR versions start at Stu3=3, None is not a valid version
[assembly: SuppressMessage("Design", "CA1008:Enums should have zero value", Justification = "FHIR versions start at Stu3 (3), no None/Unknown value needed", Scope = "type", Target = "~T:Ignixa.Abstractions.FhirVersion")]

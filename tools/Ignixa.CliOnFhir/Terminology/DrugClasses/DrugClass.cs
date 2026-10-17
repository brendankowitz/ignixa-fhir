// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology.DrugClasses;

/// <summary>A drug class.</summary>
/// <param name="ClassSource">ATC, VA, EPC or MOA: the value the 'source' argument takes.</param>
/// <param name="ClassId">The class id within the source, for example C07AB.</param>
/// <param name="ClassName">The class name RxClass gives (data).</param>
/// <param name="ClassType">RxClass's class type, for example ATC1-4.</param>
/// <param name="Via">For a drug's classes, the concept the class is related to (often the drug's ingredient); otherwise null.</param>
public sealed record DrugClass(string ClassSource, string ClassId, string? ClassName, string? ClassType, DrugConcept? Via);

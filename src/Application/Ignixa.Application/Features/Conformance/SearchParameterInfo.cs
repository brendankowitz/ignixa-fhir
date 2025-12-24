// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Application.Features.Conformance;

/// <summary>
/// DTO representing a SearchParameter being activated from a package.
/// </summary>
public record SearchParameterInfo(
    string Canonical,
    string Code,
    IReadOnlyList<string> BaseResourceTypes,
    string Expression,
    SearchParamType Type,
    string? DerivedFrom,
    string SourcePackageId,
    IReadOnlyList<CompositeComponent>? Components,
    IReadOnlyList<string>? TargetResourceTypes,
    string? Name,
    string? Description);

/// <summary>
/// Component of a composite SearchParameter.
/// </summary>
public record CompositeComponent(string DefinitionUrl, string Expression);

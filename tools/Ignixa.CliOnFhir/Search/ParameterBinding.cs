// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Ignixa.CliOnFhir.Search;

/// <summary>One search parameter definition a query parameter resolved to, on one resource type, with how the folder populates it.</summary>
/// <param name="Role">
/// <c>filter</c> (the parameter is matched), <c>chain</c> (a reference followed to <paramref name="Targets"/>),
/// <c>reverse-chain</c> (a <c>_has</c> reference from <paramref name="ResourceType"/> back to the searched type), <c>sort</c>,
/// <c>include</c> or <c>revinclude</c>.
/// </param>
/// <param name="ResourceType">The type the definition is evaluated on.</param>
/// <param name="Code">The parameter code.</param>
/// <param name="Url">The SearchParameter canonical url.</param>
/// <param name="Type">The parameter type: <c>token</c>, <c>string</c>, <c>reference</c>, <c>date</c>, ...</param>
/// <param name="Expression">The FHIRPath expression values are extracted with.</param>
/// <param name="Targets">For a chain or include, the types the reference is followed to.</param>
/// <param name="Populated">
/// <paramref name="ResourceType"/> resources in the folder's index with at least one value for the parameter (every resource
/// for <c>_id</c>).
/// </param>
/// <param name="Indexed"><paramref name="ResourceType"/> resources in the folder's index.</param>
/// <param name="TopValues">For a token parameter, its most common values (at most 10, most resources first); absent otherwise.</param>
public sealed record ParameterBinding(
    string Role,
    string ResourceType,
    string Code,
    string Url,
    string Type,
    string Expression,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Targets,
    int Populated,
    int Indexed,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<TopValue>? TopValues);

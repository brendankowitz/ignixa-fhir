// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.FhirMappingLanguage.Expressions;
using Ignixa.FhirMappingLanguage.Mutator;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.CliOnFhir.Mapping;

/// <summary>
/// Runs a map's entry group on one resource, set up as <c>Ignixa.FhirMappingLanguage</c>'s official FML test cases are run
/// (<c>FmlTransformOracleTests</c>): the resource is the source, a new resource of the target type the target, the target is
/// changed through a <see cref="JsonNodeMutator"/>, and that mutator makes what <c>create()</c> asks for, a datatype or a
/// resource (a node parsed from <c>{"resourceType": type}</c> is refused as a datatype such as <c>Reference</c>).
/// <c>MapOracleTests</c> holds the two to the same results. Errors are strict (the first one stops the resource), each run gets a
/// new evaluator (its limits count per resource), and <see cref="MappingEvaluatorOptions.TransformTimeout"/> bounds each one.
/// </summary>
/// <param name="schema">The folder's FHIR version.</param>
/// <param name="map">The map, its imports merged.</param>
/// <param name="entry">Its entry group.</param>
/// <param name="transformTimeout">The longest one resource's transformation may take.</param>
internal sealed class MapTransform(IFhirSchemaProvider schema, MapExpression map, MapEntry entry, TimeSpan transformTimeout)
{
    private readonly FhirPathParser _fhirPathParser = new();
    private readonly FhirPathEvaluator _fhirPathEvaluator = new();

    /// <summary>The options each resource is transformed with: the library's defaults and <see cref="MappingEvaluatorOptions.TransformTimeout"/>.</summary>
    public MappingEvaluatorOptions Options { get; } = new() { TransformTimeout = transformTimeout, ErrorMode = ErrorMode.Strict };

    /// <summary>The target resource built from <paramref name="source"/>.</summary>
    /// <exception cref="MappingExecutionException">The map failed on it, or ran out of time (code <c>TRANSFORM_TIMEOUT</c>).</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public JsonObject Apply(ResourceJsonNode source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        var mutator = new JsonNodeMutator(_fhirPathEvaluator, _fhirPathParser, () => schema);
        ResourceJsonNode target = JsonSourceNodeFactory.Parse(new JsonObject { ["resourceType"] = entry.TargetType });
        var context = new MappingContext
        {
            ErrorMode = ErrorMode.Strict,
            ResourceCreator = type => mutator.CreateElement(type),
        };
        context.SetSource(entry.SourceName, source.ToElement(schema));
        context.SetTarget(entry.TargetName, target.ToElement(schema));
        context.SetTargetResource(entry.TargetName, target);

        new MappingEvaluator(Options, mutator).ExecuteGroup(map, entry.Group, context, cancellationToken);
        return ((IMutableJsonNode)target).MutableNode;
    }
}

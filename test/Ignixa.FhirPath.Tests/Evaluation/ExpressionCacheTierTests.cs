// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;
using Shouldly;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// Expressions an API caller supplies are cached apart from the server's own, so a caller submitting many
/// distinct expressions can neither grow memory without bound nor evict the server's expressions.
/// </summary>
[Collection(NonParallelCollection)]
public class ExpressionCacheTierTests
{
    // Exceeds two generations of the server tier (4096 each), so a flood that reached it would evict the entry.
    private const int FloodCount = 9000;

    // Other tests insert into the shared server tier; running alone keeps the eviction assertions deterministic.
    private const string NonParallelCollection = "SharedExpressionCache";

    private static readonly IFhirSchemaProvider Schema = FhirVersion.R4.GetSchemaProvider();

    private static readonly IElement Patient = ResourceJsonNode
        .Parse("""{"resourceType":"Patient","id":"p1","active":true}""")
        .ToElement(Schema);

    [Fact]
    public void GivenServerExpressionCached_WhenRequestSuppliedExpressionsFloodTheCache_ThenServerExpressionRemainsCached()
    {
        // Arrange
        const string serverExpression = "Patient.id | Patient.active | Patient.tierServerMarker1";
        TypedElementExtensions.Precompile(serverExpression);
        var requestContext = new EvaluationContext { ExpressionIsRequestSupplied = true };

        // Act
        for (var i = 0; i < FloodCount; i++)
        {
            _ = Patient.Select($"Patient.id | Patient.active | {i}", requestContext).ToList();
        }

        // Assert
        TypedElementExtensions.IsServerExpressionCached(serverExpression).ShouldBeTrue();
    }

    [Fact]
    public void GivenRequestSuppliedExpression_WhenEvaluated_ThenItIsNotCachedInTheServerTier()
    {
        // Arrange
        const string expression = "Patient.id | Patient.active | Patient.tierIsolationMarker2";

        // Act
        _ = Patient.Select(expression, new EvaluationContext { ExpressionIsRequestSupplied = true }).ToList();

        // Assert
        TypedElementExtensions.IsRequestSuppliedExpressionCached(expression).ShouldBeTrue();
        TypedElementExtensions.IsServerExpressionCached(expression).ShouldBeFalse();
    }

    [Fact]
    public void GivenMoreRequestSuppliedExpressionsThanTheTierHolds_WhenEvaluated_ThenEarliestAreEvicted()
    {
        // Arrange
        const string first = "Patient.id | Patient.active | Patient.tierEvictionMarker3";
        var requestContext = new EvaluationContext { ExpressionIsRequestSupplied = true };
        _ = Patient.Select(first, requestContext).ToList();

        // Act
        for (var i = 0; i < 2000; i++)
        {
            _ = Patient.Select($"Patient.id | Patient.active | {i}", requestContext).ToList();
        }

        // Assert
        TypedElementExtensions.IsRequestSuppliedExpressionCached(first).ShouldBeFalse();
    }
}

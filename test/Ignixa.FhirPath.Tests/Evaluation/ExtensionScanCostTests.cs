// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;
using Shouldly;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// <c>extension(url)</c> walks every extension of its input to find the matching ones, so the walk is charged even
/// when nothing matches and the function answers empty.
/// </summary>
public class ExtensionScanCostTests
{
    private static readonly IFhirSchemaProvider Schema = FhirVersion.R4.GetSchemaProvider();

    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    [Fact]
    public void GivenManyNonmatchingExtensionsScannedInALoop_WhenEvaluatedUnderALowStepLimit_ThenTheScanIsCharged()
    {
        // Arrange: 50 scans of 2,000 extensions, 100,000 visits, more than 50,000 steps allow.
        var patient = PatientWithExtensions(2_000);
        var expression = "'" + new string('a', 50) + "'.toChars().select(%resource.extension('http://example.org/missing').empty())";
        var context = new EvaluationContext
        {
            Resource = patient,
            RootResource = patient,
            Schema = Schema,
            Limits = FhirPathEvaluationLimits.Default with { MaxSteps = 50_000 },
        };

        // Act & Assert
        Should.Throw<FhirPathResourceLimitExceededException>(
            () => _evaluator.Evaluate(patient, _parser.Parse(expression), context).ToList());
    }

    private static IElement PatientWithExtensions(int count)
    {
        var extensions = string.Join(
            ',',
            Enumerable.Range(0, count).Select(i => string.Create(
                CultureInfo.InvariantCulture,
                $$"""{"url":"http://example.org/e{{i}}","valueString":"v"}""")));
        return ResourceJsonNode.Parse($$"""{"resourceType":"Patient","id":"p","extension":[{{extensions}}]}""").ToElement(Schema);
    }
}

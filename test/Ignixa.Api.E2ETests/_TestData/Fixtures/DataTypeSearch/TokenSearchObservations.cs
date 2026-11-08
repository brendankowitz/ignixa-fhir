// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.FhirFakes.Builders;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Api.E2ETests._TestData.Fixtures.DataTypeSearch;

/// <summary>
/// The Observations <see cref="TokenSearchTestFixture"/> writes, built in one place so that the server write-path test in
/// Ignixa.Api.Tests (which compiles this file too) validates exactly what the E2E fixture sends, without SQL Server.
/// </summary>
/// <remarks>
/// The value systems are absolute URIs: <c>Coding.system</c> must be absolute at Spec depth, the server's default write
/// depth, wherever the Coding appears, a choice variant such as <c>valueCodeableConcept</c> included. The relative
/// <c>system1</c>..<c>system3</c> the fixture once used were accepted only because choice variants went unchecked.
/// </remarks>
internal static class TokenSearchObservations
{
    public const string System1 = "http://example.org/fhir/token-system1";

    public const string System2 = "http://example.org/fhir/token-system2";

    public const string System3 = "http://example.org/fhir/token-system3";

    /// <summary>
    /// The fixture's Observations, tagged with <paramref name="tag"/>. Index mapping:
    /// [0] = code1 in System1 (no display)
    /// [1] = System2|code2
    /// [2] = text="text" (no coding)
    /// [3] = System1|code2 with display "text2"
    /// [4] = System3|code3 with display "text"
    /// [5] = System1|code1 + System3|code2, text="text"
    /// [6] = System2|code1 + System3|code3 with display "text2"
    /// [7] = code3 (no system)
    /// [8] = empty value concept, category http://example.org/test-category|test (for :not over missing values)
    /// [9] = empty value concept, case-sensitive identifiers "VALUE" and "value"
    /// </summary>
    public static ResourceJsonNode[] Build(IFhirSchemaProvider schemaProvider, string tag) =>
    [
        Base(schemaProvider, tag).WithCodedValue("code1", System1).Build(),
        Base(schemaProvider, tag).WithCodedValue("code2", System2).Build(),
        Base(schemaProvider, tag).WithTextOnlyCodedValue("text").Build(),
        Base(schemaProvider, tag).WithCodedValue("code2", System1, "text2").Build(),
        Base(schemaProvider, tag).WithCodedValue("code3", System3, "text").Build(),
        Base(schemaProvider, tag).WithCodedValue("code1", System1).WithCodedValue("code2", System3, null, "text").Build(),
        Base(schemaProvider, tag).WithCodedValue("code1", System2).WithCodedValue("code3", System3, "text2").Build(),
        Base(schemaProvider, tag).WithCodedValue("code3", null).Build(),
        Base(schemaProvider, tag).WithEmptyCodedValue().WithCategory("test", "http://example.org/test-category").Build(),
        Base(schemaProvider, tag).WithEmptyCodedValue().WithIdentifier("VALUE", "test").WithIdentifier("value", "test").Build(),
    ];

    private static ObservationBuilder Base(IFhirSchemaProvider schemaProvider, string tag) =>
        ObservationBuilder.Create(schemaProvider)
            .WithTag(tag)
            .WithCode("code", "http://example.org/test-code")
            .WithStatus("registered");
}

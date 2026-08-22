/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Pins the behaviour changes that came with narrowing FHIRPath type-name matching to ordinal.
 *
 * The change was documented as being about the pre-R5 cast aliases, but the narrowing applies to
 * every type identifier on every version, so three further behaviours moved with it and shipped
 * unpinned: `is(Date)` stopped matching a FHIR date, complex and resource type names stopped
 * matching under alternate casing, and an identifier no model declares - `long` - became an error
 * instead of false. Each is believed correct and each is kept deliberately. Nothing here is version
 * gated: these hold on all five published versions, which is the point.
 */

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;
using Shouldly;
using Xunit;

namespace Ignixa.FhirPath.Tests.Evaluation;

public class TypeIdentifierNarrowingTests
{
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    private const string PatientJson = """
    {
      "resourceType": "Patient",
      "id": "example",
      "birthDate": "1974-12-25",
      "name": [ { "family": "Chalmers", "given": [ "Peter" ] } ]
    }
    """;

    /// <summary>
    /// <c>is</c> refuses the System spelling of a FHIR primitive on every version, including the
    /// pre-R5 versions where <c>as</c> accepts it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This changed with the ordinal narrowing - <c>Patient.birthDate is Date</c> previously answered
    /// true - and it changed on all five versions rather than only from R5. That asymmetry against
    /// <c>as</c> is deliberate. The R4 allowance for crossing the FHIR/System namespace boundary was
    /// written for <c>as</c>, and HL7's own documentation uses the namespace distinction under
    /// <c>is</c> as a worked example (<c>Patient.name.given.is(System.string).not()</c>) in the same
    /// versions. A <c>FHIR.date</c> is not a <c>System.Date</c> in any published version, so there is
    /// no version for which <c>is</c> should say it is.
    /// </para>
    /// <para>
    /// The practical justification for gating <c>as</c> and not <c>is</c> is that shipped artifacts
    /// depend on the lenient <c>as</c> - the STU3 and R4 search parameters that spell casts in
    /// PascalCase - and no shipped artifact depends on a lenient <c>is</c>.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(FhirVersion.Stu3)]
    [InlineData(FhirVersion.R4)]
    [InlineData(FhirVersion.R4B)]
    [InlineData(FhirVersion.R5)]
    [InlineData(FhirVersion.R6)]
    public void GivenAnyPublishedVersion_WhenTypeTestingAFhirDateWithTheSystemSpelling_ThenFalse(
        FhirVersion version)
    {
        // Arrange
        var schema = version.GetSchemaProvider();
        var element = ResourceJsonNode.Parse(PatientJson).ToElement(schema);

        // Act
        var result = Evaluate(element, "birthDate is Date", schema);

        // Assert
        result.ShouldHaveSingleItem().Value.ShouldBe(false);
    }

    /// <summary>
    /// The lowercase FHIR spelling still matches under <c>is</c>, so the assertion above is about the
    /// namespace and not about <c>is</c> having stopped working.
    /// </summary>
    [Theory]
    [InlineData(FhirVersion.Stu3)]
    [InlineData(FhirVersion.R4)]
    [InlineData(FhirVersion.R4B)]
    [InlineData(FhirVersion.R5)]
    [InlineData(FhirVersion.R6)]
    public void GivenAnyPublishedVersion_WhenTypeTestingAFhirDateWithTheFhirSpelling_ThenTrue(
        FhirVersion version)
    {
        // Arrange
        var schema = version.GetSchemaProvider();
        var element = ResourceJsonNode.Parse(PatientJson).ToElement(schema);

        // Act
        var result = Evaluate(element, "birthDate is date", schema);

        // Assert
        result.ShouldHaveSingleItem().Value.ShouldBe(true);
    }

    /// <summary>
    /// The narrowing reaches complex types and resource types too, not only primitives.
    /// </summary>
    /// <remarks>
    /// The documented story for the ordinal narrowing was entirely about primitives, but the matching
    /// rule is shared, so <c>humanname</c>, <c>resource</c> and <c>domainresource</c> stopped matching
    /// their PascalCase declarations at the same time. That is more spec-correct - FHIRPath
    /// identifiers are case-sensitive and the model declares <c>HumanName</c>, <c>Resource</c> and
    /// <c>DomainResource</c> - and it is kept. It is pinned here because reverting the complex-type
    /// half of the narrowing would otherwise break no test at all.
    /// </remarks>
    [Theory]
    [InlineData(FhirVersion.Stu3, "name.as(humanname)")]
    [InlineData(FhirVersion.R4, "name.as(humanname)")]
    [InlineData(FhirVersion.R4B, "name.as(humanname)")]
    [InlineData(FhirVersion.R5, "name.as(humanname)")]
    [InlineData(FhirVersion.R6, "name.as(humanname)")]
    [InlineData(FhirVersion.Stu3, "$this.as(resource)")]
    [InlineData(FhirVersion.R4, "$this.as(resource)")]
    [InlineData(FhirVersion.R4B, "$this.as(resource)")]
    [InlineData(FhirVersion.R5, "$this.as(resource)")]
    [InlineData(FhirVersion.R6, "$this.as(resource)")]
    public void GivenAnyPublishedVersion_WhenCastingAComplexTypeWithLowercaseSpelling_ThenEmpty(
        FhirVersion version,
        string expression)
    {
        // Arrange
        var schema = version.GetSchemaProvider();
        var element = ResourceJsonNode.Parse(PatientJson).ToElement(schema);

        // Act
        var result = Evaluate(element, expression, schema);

        // Assert
        result.ShouldBeEmpty();
    }

    /// <inheritdoc cref="GivenAnyPublishedVersion_WhenCastingAComplexTypeWithLowercaseSpelling_ThenEmpty"/>
    [Theory]
    [InlineData(FhirVersion.Stu3, "$this is resource")]
    [InlineData(FhirVersion.R4, "$this is resource")]
    [InlineData(FhirVersion.R4B, "$this is resource")]
    [InlineData(FhirVersion.R5, "$this is resource")]
    [InlineData(FhirVersion.R6, "$this is resource")]
    [InlineData(FhirVersion.Stu3, "$this is domainresource")]
    [InlineData(FhirVersion.R4, "$this is domainresource")]
    [InlineData(FhirVersion.R4B, "$this is domainresource")]
    [InlineData(FhirVersion.R5, "$this is domainresource")]
    [InlineData(FhirVersion.R6, "$this is domainresource")]
    public void GivenAnyPublishedVersion_WhenTypeTestingAResourceWithLowercaseSpelling_ThenFalse(
        FhirVersion version,
        string expression)
    {
        // Arrange
        var schema = version.GetSchemaProvider();
        var element = ResourceJsonNode.Parse(PatientJson).ToElement(schema);

        // Act
        var result = Evaluate(element, expression, schema);

        // Assert
        result.ShouldHaveSingleItem().Value.ShouldBe(false);
    }

    /// <summary>
    /// The PascalCase declarations still match, so the assertions above pin casing rather than a
    /// broken resource-hierarchy walk.
    /// </summary>
    [Theory]
    [InlineData(FhirVersion.Stu3, "$this is Resource")]
    [InlineData(FhirVersion.R4, "$this is Resource")]
    [InlineData(FhirVersion.R4B, "$this is Resource")]
    [InlineData(FhirVersion.R5, "$this is Resource")]
    [InlineData(FhirVersion.R6, "$this is Resource")]
    [InlineData(FhirVersion.Stu3, "$this is DomainResource")]
    [InlineData(FhirVersion.R4, "$this is DomainResource")]
    [InlineData(FhirVersion.R4B, "$this is DomainResource")]
    [InlineData(FhirVersion.R5, "$this is DomainResource")]
    [InlineData(FhirVersion.R6, "$this is DomainResource")]
    public void GivenAnyPublishedVersion_WhenTypeTestingAResourceWithItsDeclaredSpelling_ThenTrue(
        FhirVersion version,
        string expression)
    {
        // Arrange
        var schema = version.GetSchemaProvider();
        var element = ResourceJsonNode.Parse(PatientJson).ToElement(schema);

        // Act
        var result = Evaluate(element, expression, schema);

        // Assert
        result.ShouldHaveSingleItem().Value.ShouldBe(true);
    }

    /// <summary>
    /// <c>1 is long</c> is an error rather than false, on every version.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>long</c> was previously carried in the System type-name set under a case-insensitive
    /// comparer, so it resolved and answered false. Under ordinal matching only <c>Long</c> is a
    /// System type name, and no FHIR model declares a type called <c>long</c> - R5 and R6 spell the
    /// 64-bit integer <c>integer64</c>, and STU3 through R4B have no such type at all. FHIRPath
    /// requires an identifier that cannot be resolved to raise an error, so throwing is the correct
    /// answer and it is kept.
    /// </para>
    /// <para>
    /// This is the behaviour that makes the leniency pinned in
    /// <see cref="TypeNameCaseSensitivityTests.GivenAnyPublishedVersion_WhenCastingWithArbitraryCasing_ThenItDoesNotMatch"/>
    /// visibly inconsistent: <c>long</c> fails to resolve and throws, while <c>DATETIME</c> resolves
    /// case-insensitively through the schema provider and returns empty. Both are documented; only one
    /// is conformant.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(FhirVersion.Stu3)]
    [InlineData(FhirVersion.R4)]
    [InlineData(FhirVersion.R4B)]
    [InlineData(FhirVersion.R5)]
    [InlineData(FhirVersion.R6)]
    public void GivenAnyPublishedVersion_WhenTypeTestingAgainstAnUndeclaredIdentifier_ThenItThrows(
        FhirVersion version)
    {
        // Arrange
        var schema = version.GetSchemaProvider();
        var element = ResourceJsonNode.Parse(PatientJson).ToElement(schema);

        // Act & Assert
        Should.Throw<FhirPathEvaluationException>(() => Evaluate(element, "1 is long", schema));
    }

    /// <summary>
    /// The System spelling resolves and answers false, so the throw above is about resolution and not
    /// about 64-bit integers being unsupported.
    /// </summary>
    [Theory]
    [InlineData(FhirVersion.Stu3)]
    [InlineData(FhirVersion.R4)]
    [InlineData(FhirVersion.R4B)]
    [InlineData(FhirVersion.R5)]
    [InlineData(FhirVersion.R6)]
    public void GivenAnyPublishedVersion_WhenTypeTestingAgainstTheSystemLongSpelling_ThenFalse(
        FhirVersion version)
    {
        // Arrange
        var schema = version.GetSchemaProvider();
        var element = ResourceJsonNode.Parse(PatientJson).ToElement(schema);

        // Act
        var result = Evaluate(element, "1 is Long", schema);

        // Assert
        result.ShouldHaveSingleItem().Value.ShouldBe(false);
    }

    private IReadOnlyList<IElement> Evaluate(IElement element, string expression, ISchema? schema) =>
        _evaluator.Evaluate(
            element,
            _parser.Parse(expression),
            new EvaluationContext { Resource = element, RootResource = element, Schema = schema })
        .ToList();
}

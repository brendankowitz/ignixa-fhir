// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Checks;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests.Abstractions;

/// <summary>
/// Tests for <see cref="ValidationSchema.Compose"/>: concatenation order, error cases, and
/// the <see cref="ISingletonCheck"/>-driven deduplication contract (first occurrence wins).
/// </summary>
public class ValidationSchemaComposeTests
{
    [Fact]
    public void GivenEmptyList_WhenComposing_ThenThrows()
    {
        var schemas = Array.Empty<ValidationSchema>();

        Should.Throw<ArgumentException>(() => ValidationSchema.Compose(schemas));
    }

    [Fact]
    public void GivenSingleSchema_WhenComposing_ThenReturnsSameInstance()
    {
        var only = MakeSchema("http://example.org/Only", "Patient");

        var composed = ValidationSchema.Compose(new[] { only });

        composed.ShouldBeSameAs(only);
    }

    [Fact]
    public void GivenTwoSchemasEachWithSingletonUniversalChecks_WhenComposing_ThenExactlyOneOfEach()
    {
        var first = MakeSchema(
            "http://example.org/Base",
            "Patient",
            universalChecks: new IValidationCheck[]
            {
                new JsonStructureCheck(),
                new NarrativeCheck(),
                new ResourceTypeValidationCheck(new HashSet<string> { "Patient" }),
            });
        var second = MakeSchema(
            "http://example.org/Profile",
            "Patient",
            universalChecks: new IValidationCheck[]
            {
                new JsonStructureCheck(),
                new NarrativeCheck(),
                new ResourceTypeValidationCheck(new HashSet<string> { "Patient" }),
            });

        var composed = ValidationSchema.Compose(new[] { first, second });

        composed.Checks.OfType<JsonStructureCheck>().Count().ShouldBe(1);
        composed.Checks.OfType<NarrativeCheck>().Count().ShouldBe(1);
        composed.Checks.OfType<ResourceTypeValidationCheck>().Count().ShouldBe(1);
    }

    [Fact]
    public void GivenTwoSchemasEachWithUnknownPropertyCheck_WhenComposing_ThenFirstOneWins()
    {
        var firstAllowed = new List<string> { "name" };
        var secondAllowed = new List<string> { "birthDate" };
        var firstUnknown = new UnknownPropertyCheck(firstAllowed);
        var secondUnknown = new UnknownPropertyCheck(secondAllowed);
        var first = MakeSchema("http://example.org/Base", "Patient", specChecks: new IValidationCheck[] { firstUnknown });
        var second = MakeSchema("http://example.org/Profile", "Patient", specChecks: new IValidationCheck[] { secondUnknown });

        var composed = ValidationSchema.Compose(new[] { first, second });

        var unknownChecks = composed.Checks.OfType<UnknownPropertyCheck>().ToList();
        unknownChecks.Count.ShouldBe(1);
        unknownChecks[0].ShouldBeSameAs(firstUnknown);
    }

    [Fact]
    public void GivenTwoSchemasWithDuplicatedNonMarkerCheck_WhenComposing_ThenBothRetained()
    {
        var firstCheck = new AlwaysValidCheck();
        var secondCheck = new AlwaysValidCheck();
        var first = MakeSchema("http://example.org/Base", "Patient", specChecks: new IValidationCheck[] { firstCheck });
        var second = MakeSchema("http://example.org/Profile", "Patient", specChecks: new IValidationCheck[] { secondCheck });

        var composed = ValidationSchema.Compose(new[] { first, second });

        composed.Checks.OfType<AlwaysValidCheck>().Count().ShouldBe(2);
    }

    [Fact]
    public void GivenCustomSingletonCheckInTwoSchemas_WhenComposing_ThenDeduplicatedByMarker()
    {
        var firstSingleton = new CustomSingletonCheck();
        var secondSingleton = new CustomSingletonCheck();
        var first = MakeSchema("http://example.org/Base", "Patient", specChecks: new IValidationCheck[] { firstSingleton });
        var second = MakeSchema("http://example.org/Profile", "Patient", specChecks: new IValidationCheck[] { secondSingleton });

        var composed = ValidationSchema.Compose(new[] { first, second });

        var singletons = composed.Checks.OfType<CustomSingletonCheck>().ToList();
        singletons.Count.ShouldBe(1);
        singletons[0].ShouldBeSameAs(firstSingleton);
    }

    private static ValidationSchema MakeSchema(
        string canonicalUrl,
        string resourceType,
        IReadOnlyList<IValidationCheck>? universalChecks = null,
        IReadOnlyList<IValidationCheck>? specChecks = null,
        IReadOnlyList<IValidationCheck>? profileChecks = null)
        => new(
            canonicalUrl,
            resourceType,
            universalChecks ?? Array.Empty<IValidationCheck>(),
            specChecks ?? Array.Empty<IValidationCheck>(),
            profileChecks ?? Array.Empty<IValidationCheck>());

    private sealed class AlwaysValidCheck : IValidationCheck
    {
        public ValidationResult Validate(IElement element, ValidationSettings settings, ValidationState state)
            => ValidationResult.Success();
    }

    private sealed class CustomSingletonCheck : IValidationCheck, ISingletonCheck
    {
        public ValidationResult Validate(IElement element, ValidationSettings settings, ValidationState state)
            => ValidationResult.Success();
    }
}

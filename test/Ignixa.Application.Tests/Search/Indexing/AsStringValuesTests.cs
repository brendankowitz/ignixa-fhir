// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Abstractions;
using Ignixa.FhirFakes.Builders;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Search.Indexing.Converters;
using Ignixa.Specification.Generated;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Indexing;

/// <summary>
/// Contract tests for <see cref="ElementExtensions.AsStringValues"/>.
/// </summary>
/// <remarks>
/// The projection used to be <c>x.Value as string</c>, which yields <see langword="null"/> for the
/// <see cref="FhirTemporal"/> that <see cref="IElement.Value"/> returns for date, dateTime, instant and
/// time primitives - so any temporal an expression selected was silently dropped from the index. These
/// tests pin the two halves of the contract: a value with a lexical form survives, a value without one
/// does not.
/// </remarks>
public class AsStringValuesTests
{
    private readonly R4CoreSchemaProvider _schemaProvider = new();

    [Fact]
    public void GivenADateTypedElement_WhenProjectingToStringValues_ThenTheTemporalIsNotDropped()
    {
        // Arrange
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithBirthDate(1974, 12, 25)
            .Build();

        var birthDate = patient.ToElement(_schemaProvider).Select("birthDate").ToList();

        // Guard: this test is only meaningful while IElement.Value is a typed temporal here.
        birthDate.ShouldHaveSingleItem();
        birthDate[0].Value.ShouldBeOfType<FhirTemporal>();

        // Act
        var values = birthDate.AsStringValues().ToList();

        // Assert
        values.ShouldBe(["1974-12-25"]);
    }

    [Fact]
    public void GivenAPartialDate_WhenProjectingToStringValues_ThenThePrecisionIsPreservedVerbatim()
    {
        // Arrange
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithBirthDate(1974, 12)
            .Build();

        var birthDate = patient.ToElement(_schemaProvider).Select("birthDate").ToList();

        // Act
        var values = birthDate.AsStringValues().ToList();

        // Assert
        values.ShouldBe(["1974-12"]);
    }

    [Fact]
    public void GivenStringTypedElements_WhenProjectingToStringValues_ThenValuesAreUnchanged()
    {
        // Arrange
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithGivenName("Ada")
            .WithFamilyName("Lovelace")
            .Build();

        var given = patient.ToElement(_schemaProvider).Select("name.given").ToList();

        // Act
        var values = given.AsStringValues().ToList();

        // Assert
        values.ShouldBe(["Ada"]);
    }

    [Fact]
    public void GivenAValueWithNoLexicalForm_WhenProjectingToStringValues_ThenItIsStillDropped()
    {
        // Arrange
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithActive(true)
            .Build();

        var active = patient.ToElement(_schemaProvider).Select("active").ToList();

        // Guard: a boolean is exactly the case that must keep yielding nothing.
        active.ShouldHaveSingleItem();
        active[0].Value.ShouldBeOfType<bool>();

        // Act
        var values = active.AsStringValues().ToList();

        // Assert
        values.ShouldBeEmpty();
    }

    [Fact]
    public void GivenNullElements_WhenProjectingToStringValues_ThenReturnsEmpty()
    {
        // Act
        var values = ((IEnumerable<IElement>)null!).AsStringValues().ToList();

        // Assert
        values.ShouldBeEmpty();
    }
}

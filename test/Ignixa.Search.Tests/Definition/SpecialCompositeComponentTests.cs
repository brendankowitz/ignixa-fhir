// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Tests.Indexing;
using Ignixa.Specification.Extensions;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Tests.Definition;

/// <summary>
/// Regressions: a composite component declared <c>Special</c> (R5/R6 <c>Composition-section-code-text</c>, whose second
/// component selects <c>section.text</c>, a Narrative) made the binder throw <see cref="KeyNotFoundException"/>
/// ("key 'Special'"), which callers reported as unparseable input; a value with a <c>|</c> was instead bound as a token
/// that no index entry can match. Both now fail as not supported, naming the parameter and the component.
/// </summary>
public class SpecialCompositeComponentTests
{
    [Theory]
    [InlineData(FhirVersion.R5, "section-code-text=http://loinc.org|10154-3$chief")]
    [InlineData(FhirVersion.R5, "section-code-text=10154-3$chief")]
    [InlineData(FhirVersion.R5, "section-code-text=10154-3$a|b")]
    [InlineData(FhirVersion.R6, "section-code-text=10154-3$chief")]
    public void GivenACompositeWithASpecialComponent_WhenParsed_ThenItIsRefusedAsNotSupportedNamingTheComponent(FhirVersion version, string query)
    {
        SearchOperationNotSupportedException refused = Should.Throw<SearchOperationNotSupportedException>(
            () => VersionedCorpus.For(version).Match("Composition", query));

        string diagnostics = refused.Issues.ShouldHaveSingleItem().Diagnostics.ShouldNotBeNull();
        diagnostics.ShouldContain("'section-code-text'");
        diagnostics.ShouldContain("component 1");
        diagnostics.ShouldContain("Special");
    }

    [Fact]
    public void GivenATypeWithNoValueGrammar_WhenAnAtomicValueIsParsed_ThenItIsRefusedAsNotSupported()
    {
        IFhirSchemaProvider schema = FhirVersion.R5.GetSchemaProvider();
        var parser = new SearchAtomicValueParser(new ReferenceSearchValueParser(schema, NullFhirBaseUriProvider.Instance), schema);

        Should.Throw<SearchOperationNotSupportedException>(() => parser.Parse(SearchParamType.Special, "x"))
            .Issues.ShouldHaveSingleItem().Diagnostics.ShouldNotBeNull().ShouldContain("Special");
    }
}

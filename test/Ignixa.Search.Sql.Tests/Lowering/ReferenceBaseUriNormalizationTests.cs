using Ignixa.Abstractions;
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Lowering.Leaf;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Lowering;

/// <summary>
/// The write path stores <c>ReferenceSearchValue.BaseUri?.ToString()</c> and the query path binds the same
/// expression, so the two only reconcile if <see cref="Uri"/>'s normalization is stable across the round
/// trip and if the parser's server-base comparison sees through it. Both halves are load-bearing and
/// neither is visible from either side alone: a divergence produces no error, just a search that silently
/// finds nothing.
/// </summary>
public class ReferenceBaseUriNormalizationTests
{
    private static readonly Uri ServerBase = new("http://example.org/fhir/");

    private static ReferenceSearchValueParser Parser(Uri? serverBase = null)
        => new(new FakeSchemaProvider(), new FakeBaseUriProvider(serverBase ?? ServerBase));

    private static SearchParameterInfo SubjectParameter()
        => new("subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));

    private static Predicate Lower(ReferenceSearchValue value)
    {
        var parameter = SubjectParameter();
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, modifier: null, value);
        var context = new LeafContext(new SymbolTable(
            new Dictionary<string, short> { [parameter.Url!.ToString()] = 77 },
            new Dictionary<string, short> { ["Observation"] = 104, ["Patient"] = 103 }));
        return ReferenceLoweringRule.Lower(predicate, value, context, 104).Predicate!;
    }

    /// <summary>The row the write path would produce for a value, keyed the way the query path names the columns.</summary>
    private static Dictionary<string, object> StoredRow(ReferenceSearchValue value)
    {
        var row = new Dictionary<string, object>
        {
            ["ReferenceResourceTypeId"] = (short)103,
            ["ReferenceResourceId"] = value.ResourceId,
        };

        if (value.BaseUri?.ToString() is { } baseUri)
        {
            row["BaseUri"] = baseUri;
        }

        return row;
    }

    public static TheoryData<string, string, string> NormalizingForms() => new()
    {
        { "default http port is stripped", "http://example.org:80/fhir/", "http://example.org/fhir/" },
        { "default https port is stripped", "https://example.org:443/fhir/", "https://example.org/fhir/" },
        { "host case is lowered", "http://EXAMPLE.ORG/fhir/", "http://example.org/fhir/" },
        { "authority-only gains a trailing slash", "http://example.org", "http://example.org/" },
        { "a non-default port is preserved", "http://example.org:8080/fhir/", "http://example.org:8080/fhir/" },
    };

    [Theory]
    [MemberData(nameof(NormalizingForms))]
    public void GivenAUriThatNormalizes_WhenRoundTrippedThroughToString_ThenItIsStableAndIdempotent(
        string scenario, string input, string expected)
    {
        // Act
        var once = new Uri(input).ToString();
        var twice = new Uri(once).ToString();

        // Assert — the second pass must not move it again, or a re-indexed row would stop matching
        once.ShouldBe(expected, scenario);
        twice.ShouldBe(expected, scenario);
    }

    [Theory]
    [InlineData("http://example.org:80/fhir/Patient/123")]
    [InlineData("http://EXAMPLE.ORG/fhir/Patient/123")]
    [InlineData("http://example.org/fhir/Patient/123")]
    public void GivenAnAbsoluteSelfReferenceInANormalizingForm_WhenParsed_ThenItCollapsesToInternalWithNoBaseUri(string reference)
    {
        // Act
        var value = Parser().Parse(reference);

        // Assert — the comparison against the server base has to see through Uri normalization, or an
        // absolute self-reference is misfiled as External and never matches a relatively-stored row
        value.Kind.ShouldBe(ReferenceKind.Internal);
        value.BaseUri.ShouldBeNull();
        value.ResourceType.ShouldBe("Patient");
        value.ResourceId.ShouldBe("123");
    }

    [Fact]
    public void GivenARelativelyStoredReference_WhenSearchedForByAbsoluteSelfUrl_ThenThePredicateMatchesTheStoredRow()
    {
        // Arrange — the index path parsed the relative form; the query path parsed the absolute form,
        // spelled with a redundant default port. This is the reconciliation the spec requires.
        var parser = Parser();
        var stored = StoredRow(parser.Parse("Patient/123"));
        var queried = parser.Parse("http://example.org:80/fhir/Patient/123");

        // Act
        var predicate = Lower(queried);

        // Assert
        queried.Kind.ShouldBe(ReferenceKind.Internal);
        PredicateRowEvaluator.Matches(predicate, stored).ShouldBeTrue();
    }

    [Fact]
    public void GivenAnAbsolutelyStoredExternalReference_WhenSearchedForByADifferentlySpelledEquivalent_ThenThePredicateMatchesTheStoredRow()
    {
        // Arrange — the write path stored BaseUri.ToString() from one spelling; the query path binds
        // BaseUri.ToString() from another. Only normalization makes the two strings equal.
        var parser = Parser();
        var stored = StoredRow(parser.Parse("http://other.org/fhir/Patient/123"));
        var queried = parser.Parse("http://OTHER.org:80/fhir/Patient/123");

        // Act
        var predicate = Lower(queried);

        // Assert
        queried.Kind.ShouldBe(ReferenceKind.External);
        queried.BaseUri!.ToString().ShouldBe("http://other.org/fhir/");
        stored["BaseUri"].ShouldBe("http://other.org/fhir/");
        PredicateRowEvaluator.Matches(predicate, stored).ShouldBeTrue();
    }

    [Fact]
    public void GivenAnExternalReference_WhenLowered_ThenBindsTheNormalizedBaseUriNotTheRawInput()
    {
        // Arrange
        var value = Parser().Parse("http://OTHER.org:80/fhir/Patient/123");

        // Act
        var predicate = Lower(value);

        // Assert — (BaseUri AND Type) AND Id, with the normalized base as the bound value
        var outer = predicate.ShouldBeOfType<Predicate.And>();
        var inner = outer.Left.ShouldBeOfType<Predicate.And>();
        var baseUriEqual = inner.Left.ShouldBeOfType<Predicate.Equal>();
        baseUriEqual.Column.Column.ShouldBe("BaseUri");
        baseUriEqual.Value.Value.ShouldBe("http://other.org/fhir/");
    }

    [Fact]
    public void GivenAnInternalReference_WhenLowered_ThenDemandsANullBaseUri()
    {
        // Arrange — only a value the parser positively identified as Internal may constrain the base
        var value = Parser().Parse("http://example.org/fhir/Patient/123");

        // Act
        var predicate = Lower(value);

        // Assert
        var outer = predicate.ShouldBeOfType<Predicate.And>();
        var inner = outer.Left.ShouldBeOfType<Predicate.And>();
        inner.Left.ShouldBeOfType<Predicate.IsNull>().Column.Column.ShouldBe("BaseUri");
    }

    [Fact]
    public void GivenARelativeReference_WhenLowered_ThenLeavesTheBaseUriUnconstrainedSoItMatchesEitherStoredForm()
    {
        // Arrange — the "or vice versa" direction: a relative search value must find a row stored with a
        // base as well as one stored without.
        var parser = Parser();
        var value = parser.Parse("Patient/123");
        var storedWithoutBase = StoredRow(parser.Parse("Patient/123"));
        var storedWithBase = StoredRow(parser.Parse("http://other.org/fhir/Patient/123"));

        // Act
        var predicate = Lower(value);

        // Assert
        value.Kind.ShouldBe(ReferenceKind.InternalOrExternal);
        PredicateRowEvaluator.Matches(predicate, storedWithoutBase).ShouldBeTrue();
        PredicateRowEvaluator.Matches(predicate, storedWithBase).ShouldBeTrue();
    }

    [Fact]
    public void GivenAServerBaseConfiguredWithoutATrailingSlash_WhenParsingASelfReference_ThenItStillCollapsesToInternal()
    {
        // Arrange — the base the parser derives from a reference always ends in '/', because it is the text
        // preceding the resource type, whereas a configured base is whatever an operator typed. Uri equality
        // compares AbsolutePath exactly, so comparing with '==' meant "http://example.org/fhir" never
        // matched "http://example.org/fhir/" and the collapse silently stopped happening. Recognition now
        // goes through FhirServiceBaseUri, which treats a service base as the directory it is.
        var value = Parser(new Uri("http://example.org/fhir")).Parse("http://example.org/fhir/Patient/123");

        // Assert
        value.Kind.ShouldBe(ReferenceKind.Internal);
        value.BaseUri.ShouldBeNull();
    }

    [Fact]
    public void GivenNoBaseUriProvider_WhenParsingAnAbsoluteReference_ThenItStaysExternalWithItsNormalizedBase()
    {
        // Arrange — opting out of a server base explicitly: there is nothing to compare against
        var parser = new ReferenceSearchValueParser(new FakeSchemaProvider(), NullFhirBaseUriProvider.Instance);

        // Act
        var value = parser.Parse("http://example.org:80/fhir/Patient/123");

        // Assert
        value.Kind.ShouldBe(ReferenceKind.External);
        value.BaseUri!.ToString().ShouldBe("http://example.org/fhir/");
    }

    private sealed class FakeBaseUriProvider(Uri? baseUri) : IFhirBaseUriProvider
    {
        public Uri? GetBaseUri() => baseUri;
    }

    private sealed class FakeSchemaProvider : IFhirSchemaProvider
    {
        public FhirVersion Version => FhirVersion.R4;

        public IReadOnlySet<string> ResourceTypeNames { get; } = new HashSet<string> { "Patient", "Observation" };

        public string FullVersion => "4.0.1";

        public IReferenceMetadataProvider ReferenceMetadataProvider => throw new NotSupportedException("These tests exercise only ResourceTypeNames.");

        public IValueSetProvider ValueSetProvider => throw new NotSupportedException("These tests exercise only ResourceTypeNames.");

        public IType? GetTypeDefinition(string typeName) => throw new NotSupportedException("These tests exercise only ResourceTypeNames.");

        public bool IsKnownType(string typeName) => ResourceTypeNames.Contains(typeName);
    }
}

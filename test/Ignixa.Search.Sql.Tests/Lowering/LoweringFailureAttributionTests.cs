using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Lowering.Composite;
using Ignixa.Search.Sql.Lowering.Leaf;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Lowering;

/// <summary>
/// Both dispatchers attach the failing parameter (and its span) to an in-flight lowering failure, which is
/// the only thing that lets a trace name the parameter responsible. The keys are internal to the compiler,
/// so these assert the literal <see cref="System.Exception.Data"/> keys rather than reaching through
/// <c>SearchCompiler</c>: a <see cref="KeyNotFoundException"/> means Resolve's tree-walk missed a symbol,
/// and a walk gap cannot be synthesised through the public compile path, which builds its own symbol table.
/// </summary>
public class LoweringFailureAttributionTests
{
    private const string ParameterDataKey = "Ignixa.SearchParameter";
    private const string SpanDataKey = "Ignixa.SourceSpan";

    private static LeafContext EmptyContext()
        => new(new SymbolTable(new Dictionary<string, short>(), new Dictionary<string, short>()));

    private static SearchParameterInfo TokenParameter()
        => new("active", "active", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Patient-active"));

    [Fact]
    public void GivenASymbolTableMiss_WhenLoweringALeaf_ThenTheKeyNotFoundExceptionIsAttributedToTheParameter()
    {
        // Arrange — an empty symbol table makes SearchParamId throw; before this was attributed, the
        // trace's failure recorder took its unattributed early return and could not name the parameter.
        var parameter = TokenParameter();
        var span = new SourceSpan(SourceOrigin.Value, 0, 4);
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "true", text: null))
        {
            Span = span,
        };

        // Act
        var exception = Should.Throw<KeyNotFoundException>(() =>
            LeafLoweringDispatcher.Lower(predicate, EmptyContext(), 103));

        // Assert
        exception.Data[ParameterDataKey].ShouldBe(parameter);
        exception.Data[SpanDataKey].ShouldBe(span);
    }

    [Fact]
    public void GivenAnUnimplementedShape_WhenLoweringALeaf_ThenTheNotSupportedExceptionIsStillAttributed()
    {
        // Arrange — the pre-existing attribution must survive the widened catch
        var parameter = TokenParameter();
        var span = new SourceSpan(SourceOrigin.Value, 0, 4);
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: null, text: "display only"))
        {
            Span = span,
        };
        var context = new LeafContext(new SymbolTable(
            new Dictionary<string, short> { [parameter.Url!.ToString()] = 44 },
            new Dictionary<string, short> { ["Patient"] = 103 }));

        // Act
        var exception = Should.Throw<NotSupportedException>(() =>
            LeafLoweringDispatcher.Lower(predicate, context, 103));

        // Assert
        exception.Data[ParameterDataKey].ShouldBe(parameter);
        exception.Data[SpanDataKey].ShouldBe(span);
    }

    [Fact]
    public void GivenASymbolTableMiss_WhenLoweringAComposite_ThenTheKeyNotFoundExceptionIsAttributedToTheCompositeParameter()
    {
        // Arrange
        var composite = new SearchParameterInfo(
            "code-value-concept", "code-value-concept", SearchParamType.Composite,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-code-value-concept"));
        var span = new SourceSpan(SourceOrigin.Value, 0, 6);
        var components = new[]
        {
            ComponentAt(0, "code", "8480-6", span),
            ComponentAt(1, "value-concept", "high", null),
        };

        // Act
        var exception = Should.Throw<KeyNotFoundException>(() =>
            CompositeLoweringDispatcher.Lower(composite, components, EmptyContext(), 104));

        // Assert — the composite parameter, not one of its components, is what a trace reports
        exception.Data[ParameterDataKey].ShouldBe(composite);
        exception.Data[SpanDataKey].ShouldBe(span);
    }

    private static CompositeComponentExpression ComponentAt(int position, string code, string tokenCode, SourceSpan? span)
    {
        var parameter = new SearchParameterInfo(code, code, SearchParamType.Token, new Uri($"http://hl7.org/fhir/SearchParameter/Observation-{code}"));
        return new CompositeComponentExpression(
            parameter,
            position,
            new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, tokenCode, text: null)))
        {
            Span = span,
        };
    }
}

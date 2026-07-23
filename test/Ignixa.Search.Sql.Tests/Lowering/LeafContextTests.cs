using Ignixa.Search.Models;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Lowering;

public class LeafContextTests
{
    [Fact]
    public void GivenAResolvedParameter_WhenSearchParamIdRequested_ThenReturnsTheSymbolTablesValue()
    {
        // Arrange
        var parameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [parameter.Url.ToString()] = 202 },
            new Dictionary<string, short>());
        var context = new LeafContext(symbols);

        // Act & Assert
        context.SearchParamId(parameter).ShouldBe((short)202);
    }

    [Fact]
    public void GivenAValue_WhenParameterized_ThenReturnsASqlParameterRefWrappingIt()
    {
        // Arrange
        var symbols = new SymbolTable(new Dictionary<string, short>(), new Dictionary<string, short>());
        var context = new LeafContext(symbols);

        // Act
        var parameterRef = context.Parameter("Smith");

        // Assert
        parameterRef.Value.ShouldBe("Smith");
    }

    [Fact]
    public void GivenAnApproximationReferenceTime_WhenLeafContextConstructed_ThenTheTimeIsExposed()
    {
        // Arrange
        var fixedTime = new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);
        var symbols = new SymbolTable(new Dictionary<string, short>(), new Dictionary<string, short>());

        // Act
        var context = new LeafContext(symbols, fixedTime);

        // Assert
        context.ApproximationReferenceTime.ShouldBe(fixedTime);
    }

    [Fact]
    public void GivenNoApproximationReferenceTime_WhenLeafContextConstructed_ThenItIsNull()
    {
        // Arrange
        var symbols = new SymbolTable(new Dictionary<string, short>(), new Dictionary<string, short>());

        // Act
        var context = new LeafContext(symbols);

        // Assert
        context.ApproximationReferenceTime.ShouldBeNull();
    }
}

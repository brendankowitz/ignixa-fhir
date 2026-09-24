using System.Reflection;
using Ignixa.DataLayer.SqlServer.RowGenerators;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.Tests.RowGenerators;

public class RowGeneratorsCompilationTests
{
    [Fact]
    public void GivenTheCopiedRowGeneratorsAssembly_WhenListingISearchParameterRowGeneratorImplementers_ThenAllFourteenArePresent()
    {
        var expectedTypeNames = new[]
        {
            "DateTimeSearchParameterRowGenerator", "NumberSearchParameterRowGenerator", "QuantityCodeRowGenerator",
            "QuantitySearchParameterRowGenerator", "ReferenceSearchParameterRowGenerator", "RefTokenCompositeRowGenerator",
            "StringSearchParameterRowGenerator", "TokenDateTimeCompositeRowGenerator", "TokenNumberNumberCompositeRowGenerator",
            "TokenQuantityCompositeRowGenerator", "TokenSearchParameterRowGenerator", "TokenStringCompositeRowGenerator",
            "TokenTextRowGenerator", "TokenTokenCompositeRowGenerator", "UriSearchParameterRowGenerator"
        };

        var assembly = typeof(ISearchParameterRowGenerator).Assembly;
        var implementers = assembly.GetTypes()
            .Where(t => typeof(ISearchParameterRowGenerator).IsAssignableFrom(t) && !t.IsInterface)
            .Select(t => t.Name)
            .ToList();

        foreach (var expected in expectedTypeNames)
        {
            implementers.ShouldContain(expected);
        }
        implementers.Count.ShouldBe(15); // 15 files implement the interface (QuantityCodeRowGenerator + the 14 named above)
    }

    [Fact]
    public void GivenResourceRowGenerator_WhenConstructedWithACompressor_ThenSucceeds()
    {
        var compressor = new Compression.GzipResourceCompressor(new Microsoft.IO.RecyclableMemoryStreamManager());
        var generator = new ResourceRowGenerator(compressor);
        generator.ShouldNotBeNull();
    }

    [Fact]
    public void GivenResourceWriteClaimRowGenerator_WhenConstructedWithNoArguments_ThenSucceeds()
    {
        var generator = new ResourceWriteClaimRowGenerator();
        generator.ShouldNotBeNull();
    }
}

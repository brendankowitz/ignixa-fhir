using System.Text.Json.Nodes;
using Ignixa.Application.Features.Patch;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Features.Patch;

public class FhirPatchValuePartTests
{
    private static readonly FhirPatchValuePart Leaf = new("item", "String", JsonValue.Create("x"), []);

    [Fact]
    public void GivenValueOnly_WhenCreated_ThenPartIsALeaf()
    {
        Leaf.Parts.ShouldBeEmpty();
        Leaf.Value.ShouldNotBeNull();
    }

    [Fact]
    public void GivenNestedPartsOnly_WhenCreated_ThenPartIsAGroup()
    {
        var group = new FhirPatchValuePart("entry", null, null, [Leaf]);

        group.Value.ShouldBeNull();
        group.Parts.ShouldHaveSingleItem();
    }

    [Fact]
    public void GivenBothValueAndNestedParts_WhenCreated_ThenItIsRejected()
    {
        Should.Throw<ArgumentException>(() => new FhirPatchValuePart("entry", "String", JsonValue.Create("x"), [Leaf]));
    }

    [Fact]
    public void GivenNeitherValueNorNestedParts_WhenCreated_ThenItIsRejected()
    {
        Should.Throw<ArgumentException>(() => new FhirPatchValuePart("entry", null, null, []));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void GivenMissingName_WhenCreated_ThenItIsRejected(string? name)
    {
        Should.Throw<ArgumentException>(() => new FhirPatchValuePart(name!, "String", JsonValue.Create("x"), []));
    }

    [Fact]
    public void GivenCallerMutatesSourceList_WhenCreated_ThenPartsAreUnchanged()
    {
        var source = new List<FhirPatchValuePart> { Leaf };
        var group = new FhirPatchValuePart("entry", null, null, source);

        source.Clear();

        group.Parts.ShouldHaveSingleItem();
    }
}

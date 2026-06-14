// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using HotChocolate.Language;
using Ignixa.Application.Features.Experimental.GraphQl.Directives;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Experimental.GraphQl;

public class FlattenResultProcessorTests
{
    [Fact]
    public void GivenFlattenOnObject_WhenProcessing_ThenPromotesChildrenToParent()
    {
        // Arrange
        var query = "{ identifier @flatten { system value } active }";
        var document = Utf8GraphQLParser.Parse(query);
        var data = new Dictionary<string, object?>
        {
            ["identifier"] = new Dictionary<string, object?> { ["system"] = "urn:oid:1.2.3", ["value"] = "12345" },
            ["active"] = true,
        };

        // Act
        FlattenResultProcessor.Process(document, null, data);

        // Assert
        data.ShouldNotContainKey("identifier");
        data["system"].ShouldBe("urn:oid:1.2.3");
        data["value"].ShouldBe("12345");
        data["active"].ShouldBe(true);
    }

    [Fact]
    public void GivenFlattenOnArray_WhenProcessing_ThenCollatesChildrenIntoLists()
    {
        // Arrange
        var query = "{ name @flatten { given family } }";
        var document = Utf8GraphQLParser.Parse(query);
        var data = new Dictionary<string, object?>
        {
            ["name"] = new List<object?>
            {
                new Dictionary<string, object?> { ["given"] = new List<object?> { "Peter", "James" }, ["family"] = "Chalmers" },
                new Dictionary<string, object?> { ["given"] = new List<object?> { "Jim" } },
            },
        };

        // Act
        FlattenResultProcessor.Process(document, null, data);

        // Assert
        data.ShouldNotContainKey("name");
        var given = data["given"].ShouldBeOfType<List<object?>>();
        given.Count.ShouldBe(3); // Peter, James, Jim
        var family = data["family"].ShouldBeOfType<List<object?>>();
        family.Count.ShouldBe(1); // Chalmers (only first item had family)
    }

    [Fact]
    public void GivenSliceByIndex_WhenProcessing_ThenSuffixesFieldNames()
    {
        // Arrange
        var query = """{ name @slice(path: "$index") { given family } }""";
        var document = Utf8GraphQLParser.Parse(query);
        var data = new Dictionary<string, object?>
        {
            ["name"] = new List<object?>
            {
                new Dictionary<string, object?> { ["given"] = "Peter", ["family"] = "Chalmers" },
                new Dictionary<string, object?> { ["given"] = "Jim" },
            },
        };

        // Act
        FlattenResultProcessor.Process(document, null, data);

        // Assert
        data.ShouldNotContainKey("name");
        data["given.0"].ShouldBe("Peter");
        data["family.0"].ShouldBe("Chalmers");
        data["given.1"].ShouldBe("Jim");
    }

    [Fact]
    public void GivenSliceByProperty_WhenProcessing_ThenUsesPropertyValueAsSuffix()
    {
        // Arrange
        var query = """{ name @slice(path: "use") { given family } }""";
        var document = Utf8GraphQLParser.Parse(query);
        var data = new Dictionary<string, object?>
        {
            ["name"] = new List<object?>
            {
                new Dictionary<string, object?> { ["use"] = "official", ["given"] = "Peter", ["family"] = "Chalmers" },
                new Dictionary<string, object?> { ["use"] = "usual", ["given"] = "Jim" },
            },
        };

        // Act
        FlattenResultProcessor.Process(document, null, data);

        // Assert
        data.ShouldNotContainKey("name");
        data["given.official"].ShouldBe("Peter");
        data["family.official"].ShouldBe("Chalmers");
        data["given.usual"].ShouldBe("Jim");
        data.ShouldNotContainKey("use.official");
        data.ShouldNotContainKey("use.usual");
    }

    [Fact]
    public void GivenNoDirectives_WhenProcessing_ThenDataUnchanged()
    {
        // Arrange
        var query = "{ name { given family } }";
        var document = Utf8GraphQLParser.Parse(query);
        var data = new Dictionary<string, object?>
        {
            ["name"] = new Dictionary<string, object?> { ["given"] = "Peter", ["family"] = "Chalmers" },
        };

        // Act
        FlattenResultProcessor.Process(document, null, data);

        // Assert
        data.ShouldContainKey("name");
        var name = data["name"].ShouldBeOfType<Dictionary<string, object?>>();
        name["given"].ShouldBe("Peter");
    }

    [Fact]
    public void GivenNestedFlatten_WhenProcessing_ThenFlattensRecursively()
    {
        // Arrange
        var query = "{ name @flatten { text @flatten { value } } }";
        var document = Utf8GraphQLParser.Parse(query);
        var data = new Dictionary<string, object?>
        {
            ["name"] = new Dictionary<string, object?>
            {
                ["text"] = new Dictionary<string, object?> { ["value"] = "hello" },
            },
        };

        // Act
        FlattenResultProcessor.Process(document, null, data);

        // Assert
        data.ShouldNotContainKey("name");
        data.ShouldNotContainKey("text");
        data["value"].ShouldBe("hello");
    }

    [Fact]
    public void GivenSliceWithMissingPath_WhenProcessing_ThenFallsBackToIndex()
    {
        // Arrange
        var query = """{ name @slice(path: "use") { given } }""";
        var document = Utf8GraphQLParser.Parse(query);
        var data = new Dictionary<string, object?>
        {
            ["name"] = new List<object?>
            {
                new Dictionary<string, object?> { ["given"] = "Peter" }, // no "use" property
            },
        };

        // Act
        FlattenResultProcessor.Process(document, null, data);

        // Assert
        data.ShouldNotContainKey("name");
        data["given.0"].ShouldBe("Peter");
    }

    [Fact]
    public void GivenDeepCopyData_WhenSourceHasNestedStructures_ThenProducesMutableCopy()
    {
        // Arrange
        var source = new Dictionary<string, object?>
        {
            ["scalar"] = "value",
            ["nested"] = new Dictionary<string, object?> { ["inner"] = 42 },
            ["list"] = new List<object?> { "a", "b" },
        };

        // Act
        var copy = FlattenResultProcessor.DeepCopyData(source);

        // Assert
        copy.ShouldBeOfType<Dictionary<string, object?>>();
        copy["scalar"].ShouldBe("value");
        var nested = copy["nested"].ShouldBeOfType<Dictionary<string, object?>>();
        nested["inner"].ShouldBe(42);
        var list = copy["list"].ShouldBeOfType<List<object?>>();
        list.Count.ShouldBe(2);

        // Verify it's a true deep copy (not same references)
        copy.ShouldNotBeSameAs(source);
        nested.ShouldNotBeSameAs(source["nested"]);
    }

    [Fact]
    public void GivenFlattenOnNullField_WhenProcessing_ThenRemovesFieldOnly()
    {
        // Arrange
        var query = "{ identifier @flatten { system value } active }";
        var document = Utf8GraphQLParser.Parse(query);
        var data = new Dictionary<string, object?>
        {
            ["identifier"] = null,
            ["active"] = true,
        };

        // Act
        FlattenResultProcessor.Process(document, null, data);

        // Assert — null field gets removed, nothing promoted
        data.ShouldNotContainKey("identifier");
        data["active"].ShouldBe(true);
    }

    [Fact]
    public void GivenSliceOnNonList_WhenProcessing_ThenLeavesDataUnchanged()
    {
        // Arrange — @slice on a scalar (should be a no-op)
        var query = """{ name @slice(path: "$index") { given } }""";
        var document = Utf8GraphQLParser.Parse(query);
        var data = new Dictionary<string, object?>
        {
            ["name"] = "not-a-list",
        };

        // Act
        FlattenResultProcessor.Process(document, null, data);

        // Assert — non-list value stays
        data["name"].ShouldBe("not-a-list");
    }

    [Fact]
    public void GivenSingletonOnSingleElementList_WhenProcessing_ThenUnwrapsElement()
    {
        // Arrange
        var query = """{ identifier @singleton { system value } }""";
        var document = Utf8GraphQLParser.Parse(query);
        var data = new Dictionary<string, object?>
        {
            ["identifier"] = new List<object?>
            {
                new Dictionary<string, object?> { ["system"] = "urn:oid:1.2.3", ["value"] = "12345" },
            },
        };

        // Act
        FlattenResultProcessor.Process(document, null, data);

        // Assert
        var identifier = data["identifier"].ShouldBeOfType<Dictionary<string, object?>>();
        identifier["system"].ShouldBe("urn:oid:1.2.3");
        identifier["value"].ShouldBe("12345");
    }

    [Fact]
    public void GivenSingletonOnEmptyList_WhenProcessing_ThenSetsToNull()
    {
        // Arrange
        var query = """{ identifier @singleton { system value } }""";
        var document = Utf8GraphQLParser.Parse(query);
        var data = new Dictionary<string, object?>
        {
            ["identifier"] = new List<object?>(),
        };

        // Act
        FlattenResultProcessor.Process(document, null, data);

        // Assert
        data["identifier"].ShouldBeNull();
    }

    [Fact]
    public void GivenSingletonOnMultiElementList_WhenProcessing_ThenThrows()
    {
        // Arrange
        var query = """{ identifier @singleton { system value } }""";
        var document = Utf8GraphQLParser.Parse(query);
        var data = new Dictionary<string, object?>
        {
            ["identifier"] = new List<object?>
            {
                new Dictionary<string, object?> { ["system"] = "a", ["value"] = "1" },
                new Dictionary<string, object?> { ["system"] = "b", ["value"] = "2" },
            },
        };

        // Act + Assert
        Should.Throw<SingletonDirectiveViolationException>(() => FlattenResultProcessor.Process(document, null, data));
    }

    [Fact]
    public void GivenSliceByDottedPath_WhenProcessing_ThenResolvesNestedDiscriminator()
    {
        // Arrange
        var query = """{ name @slice(path: "use.coding") { given } }""";
        var document = Utf8GraphQLParser.Parse(query);
        var data = new Dictionary<string, object?>
        {
            ["name"] = new List<object?>
            {
                new Dictionary<string, object?>
                {
                    ["use"] = new Dictionary<string, object?> { ["coding"] = "official" },
                    ["given"] = "Peter",
                },
                new Dictionary<string, object?>
                {
                    ["use"] = new Dictionary<string, object?> { ["coding"] = "usual" },
                    ["given"] = "Jim",
                },
            },
        };

        // Act
        FlattenResultProcessor.Process(document, null, data);

        // Assert
        data.ShouldNotContainKey("name");
        data["given.official"].ShouldBe("Peter");
        data["given.usual"].ShouldBe("Jim");
    }

    [Fact]
    public void GivenSliceByDottedPathThroughList_WhenProcessing_ThenTakesFirstElement()
    {
        // Arrange
        var query = """{ name @slice(path: "coding.code") { given } }""";
        var document = Utf8GraphQLParser.Parse(query);
        var data = new Dictionary<string, object?>
        {
            ["name"] = new List<object?>
            {
                new Dictionary<string, object?>
                {
                    ["coding"] = new List<object?>
                    {
                        new Dictionary<string, object?> { ["code"] = "primary" },
                        new Dictionary<string, object?> { ["code"] = "secondary" },
                    },
                    ["given"] = "Peter",
                },
            },
        };

        // Act
        FlattenResultProcessor.Process(document, null, data);

        // Assert
        data["given.primary"].ShouldBe("Peter");
    }

    [Fact]
    public void GivenFirstOnEmptyList_WhenProcessing_ThenSetsToNull()
    {
        // Arrange
        var query = "{ name @first { given } }";
        var document = Utf8GraphQLParser.Parse(query);
        var data = new Dictionary<string, object?>
        {
            ["name"] = new List<object?>(),
        };

        // Act
        FlattenResultProcessor.Process(document, null, data);

        // Assert
        data["name"].ShouldBeNull();
    }

    [Fact]
    public void GivenFirstOnNonEmptyList_WhenProcessing_ThenReplacesWithFirstElement()
    {
        // Arrange
        var query = "{ name @first { given } }";
        var document = Utf8GraphQLParser.Parse(query);
        var data = new Dictionary<string, object?>
        {
            ["name"] = new List<object?>
            {
                new Dictionary<string, object?> { ["given"] = "Peter" },
                new Dictionary<string, object?> { ["given"] = "Jim" },
            },
        };

        // Act
        FlattenResultProcessor.Process(document, null, data);

        // Assert
        var first = data["name"].ShouldBeOfType<Dictionary<string, object?>>();
        first["given"].ShouldBe("Peter");
    }

    [Fact]
    public void GivenFirstOnNonList_WhenProcessing_ThenLeavesValueUnchanged()
    {
        // Arrange — @first on a scalar is a no-op
        var query = "{ name @first { given } }";
        var document = Utf8GraphQLParser.Parse(query);
        var data = new Dictionary<string, object?>
        {
            ["name"] = "not-a-list",
        };

        // Act
        FlattenResultProcessor.Process(document, null, data);

        // Assert
        data["name"].ShouldBe("not-a-list");
    }

    [Fact]
    public void GivenSingletonOnNonList_WhenProcessing_ThenLeavesValueUnchanged()
    {
        // Arrange — @singleton on a scalar is a no-op (no assertion, no unwrap)
        var query = "{ name @singleton { given } }";
        var document = Utf8GraphQLParser.Parse(query);
        var data = new Dictionary<string, object?>
        {
            ["name"] = "not-a-list",
        };

        // Act
        FlattenResultProcessor.Process(document, null, data);

        // Assert
        data["name"].ShouldBe("not-a-list");
    }

    [Fact]
    public void GivenFlattenAndSliceOnSameField_WhenProcessing_ThenSlicesBeforeFlatten()
    {
        // Arrange — @slice runs first (promoting suffixed children to the parent and removing
        // the field), so @flatten then has nothing left to flatten. The slice output survives.
        var query = """{ name @flatten @slice(path: "use") { given } }""";
        var document = Utf8GraphQLParser.Parse(query);
        var data = new Dictionary<string, object?>
        {
            ["name"] = new List<object?>
            {
                new Dictionary<string, object?> { ["use"] = "official", ["given"] = "Peter" },
                new Dictionary<string, object?> { ["use"] = "usual", ["given"] = "Jim" },
            },
        };

        // Act
        FlattenResultProcessor.Process(document, null, data);

        // Assert — slice-before-flatten: suffixed keys present, original field removed
        data.ShouldNotContainKey("name");
        data["given.official"].ShouldBe("Peter");
        data["given.usual"].ShouldBe("Jim");
    }

    [Fact]
    public void GivenSliceWithColludingSuffix_WhenProcessing_ThenDisambiguatesDeterministically()
    {
        // Arrange — two items share the same discriminator value, so suffixes collide
        var query = """{ name @slice(path: "use") { given } }""";
        var document = Utf8GraphQLParser.Parse(query);
        var data = new Dictionary<string, object?>
        {
            ["name"] = new List<object?>
            {
                new Dictionary<string, object?> { ["use"] = "official", ["given"] = "Peter" },
                new Dictionary<string, object?> { ["use"] = "official", ["given"] = "Paul" },
            },
        };

        // Act
        FlattenResultProcessor.Process(document, null, data);

        // Assert — first keeps the base key, the colliding second is suffixed with an index
        data["given.official"].ShouldBe("Peter");
        data["given.official.1"].ShouldBe("Paul");
    }
}

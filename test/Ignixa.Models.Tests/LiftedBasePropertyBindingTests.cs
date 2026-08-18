// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using System.Text.Json.Nodes;
using Ignixa.Serialization.SourceNodes;
using Shouldly;
using Xunit;

namespace Ignixa.Models.Tests;

/// <summary>
/// Locks the JSON keys the Resource- and DomainResource-level accessors bind to.
/// </summary>
/// <remarks>
/// Lifting these elements onto the base classes created a string-sync contract: the generator's skip
/// gates list JSON names (TypedModelClassifier.WalkType) that must match the names the base-class
/// accessors read and write. A typo on either side is invisible to the compiler -- the C# property
/// name is unchanged, so no CS0108 fires -- and set/get round-trip tests cannot catch it either,
/// because they read back through the same wrong key. These assertions go through the raw JsonObject
/// so the key itself is the thing under test.
/// </remarks>
public sealed class LiftedBasePropertyBindingTests
{
    [Fact]
    public void GivenLiftedBaseProperties_WhenSet_ThenTheyBindToTheFhirDefinedJsonKeys()
    {
        // Arrange
        var patient = new Ignixa.Models.R4.Patient();

        // Act
        patient.Language = "en-AU";
        patient.ImplicitRules = "http://example.org/rules";
        patient.Text = new Narrative { Div = "<div>summary</div>" };
        patient.Extension.Add(new Extension { Url = "http://example.org/ext" });
        patient.ModifierExtension.Add(new Extension { Url = "http://example.org/modifier" });
        patient.Contained.Add(new Ignixa.Models.R4.Patient { Id = "contained-1" });

        // Assert
        JsonObject node = patient.MutableNode;

        node["language"]!.GetValue<string>().ShouldBe("en-AU");
        node["implicitRules"]!.GetValue<string>().ShouldBe("http://example.org/rules");
        node["text"]!["div"]!.GetValue<string>().ShouldBe("<div>summary</div>");
        node["extension"]!.AsArray().Count.ShouldBe(1);
        node["modifierExtension"]!.AsArray().Count.ShouldBe(1);
        node["contained"]!.AsArray().Count.ShouldBe(1);
    }

    [Fact]
    public void GivenLiftedBaseProperties_WhenRead_ThenTheyProjectTheFhirDefinedJsonKeys()
    {
        // Arrange
        const string Json = """
        {
          "resourceType": "Patient",
          "implicitRules": "http://example.org/rules",
          "language": "en-AU",
          "text": { "status": "generated", "div": "<div>summary</div>" },
          "contained": [ { "resourceType": "Patient", "id": "contained-1" } ],
          "extension": [ { "url": "http://example.org/ext" } ],
          "modifierExtension": [ { "url": "http://example.org/modifier" } ]
        }
        """;

        // Act
        var patient = ResourceJsonNode.Parse(Json).As<Ignixa.Models.R4.Patient>();

        // Assert
        patient.ShouldNotBeNull();
        patient!.Language.ShouldBe("en-AU");
        patient.ImplicitRules.ShouldBe("http://example.org/rules");
        patient.Text!.Div.ShouldBe("<div>summary</div>");
        patient.Contained.Count.ShouldBe(1);
        patient.Contained[0].ResourceType.ShouldBe("Patient");
        patient.Extension.Count.ShouldBe(1);
        patient.ModifierExtension.Count.ShouldBe(1);
    }

    [Fact]
    public void GivenLiftedBaseProperty_WhenSetToNull_ThenTheKeyIsRemovedRatherThanWrittenAsJsonNull()
    {
        // Arrange
        var patient = new Ignixa.Models.R4.Patient
        {
            Language = "en-AU",
            Text = new Narrative { Div = "<div>summary</div>" },
        };

        // Act
        patient.Language = null;
        patient.Text = null;

        // Assert
        JsonObject node = patient.MutableNode;
        node.ContainsKey("language").ShouldBeFalse();
        node.ContainsKey("text").ShouldBeFalse();
    }

    [Fact]
    public void GivenDomainResourceList_WhenReadWithoutMutating_ThenNoEmptyArrayIsMaterialised()
    {
        // Reading must be side-effect free. Vivifying here would write "extension": [] into the document
        // -- invalid FHIR (arrays require at least one element) and a mutation of a resource nobody edited,
        // which changes its serialized bytes and therefore its ETag.
        var patient = ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"1"}""")
            .As<Ignixa.Models.R4.Patient>();

        patient.Contained.Count.ShouldBe(0);
        patient.Extension.Count.ShouldBe(0);
        patient.ModifierExtension.Any().ShouldBeFalse();
        _ = patient.Extension.ToList();

        JsonObject node = patient.MutableNode;
        node.ContainsKey("contained").ShouldBeFalse();
        node.ContainsKey("extension").ShouldBeFalse();
        node.ContainsKey("modifierExtension").ShouldBeFalse();
    }

    [Fact]
    public void GivenListElementPresentAsWrongJsonKind_WhenRead_ThenItThrowsInsteadOfDiscardingTheContent()
    {
        // A non-array value at a list key used to be treated as "absent", and the first write replaced it
        // with an empty array -- author-supplied content destroyed with no error anywhere.
        var patient = ResourceJsonNode.Parse(
                """{"resourceType":"Patient","extension":{"url":"http://x","valueString":"keepme"}}""")
            .As<Ignixa.Models.R4.Patient>();

        Should.Throw<InvalidOperationException>(() => patient.Extension)
            .Message.ShouldContain("extension");

        patient.MutableNode["extension"]!["valueString"]!.GetValue<string>().ShouldBe("keepme");
    }

    [Fact]
    public void GivenAbsentList_WhenItemAdded_ThenTheArrayIsCreated()
    {
        // The write path must still vivify -- the read/write split must not make lists unwritable.
        var patient = ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"1"}""")
            .As<Ignixa.Models.R4.Patient>();

        patient.Extension.Add(new Extension { Url = "http://example.org/ext" });

        patient.MutableNode["extension"]!.AsArray().Count.ShouldBe(1);
    }

    [Fact]
    public void GivenDomainResourceList_WhenAccessorIsObtained_ThenNoEmptyArrayIsMaterialised()
    {
        var patient = new Ignixa.Models.R4.Patient();

        _ = patient.Contained;
        _ = patient.Extension;
        _ = patient.ModifierExtension;

        JsonObject node = patient.MutableNode;
        node.ContainsKey("contained").ShouldBeFalse();
        node.ContainsKey("extension").ShouldBeFalse();
        node.ContainsKey("modifierExtension").ShouldBeFalse();
    }
}

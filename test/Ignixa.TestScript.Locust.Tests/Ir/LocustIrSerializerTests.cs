using System.Text.Json.Nodes;
using Ignixa.TestScript.Locust.Ir;

namespace Ignixa.TestScript.Locust.Tests.Ir;

public class LocustIrSerializerTests
{
    [Fact]
    public void GivenDocument_WhenSerialized_ThenUsesVersionedCamelCaseDiscriminatedShape()
    {
        var document = new LocustIrDocument
        {
            Metadata = new LocustIrMetadata("CRUD basic", "CRUD/basic.json", "4.0"),
            Setup =
            [
                new LocustIrOperation
                {
                    Id = "setup.0",
                    Type = "create",
                    Method = "POST",
                    Resource = "Patient"
                }
            ]
        };

        JsonNode json = JsonNode.Parse(LocustIrSerializer.Serialize(document))!;

        json["schemaVersion"]!.GetValue<string>().ShouldBe("1.0");
        json["metadata"]!["source"]!.GetValue<string>().ShouldBe("CRUD/basic.json");
        json["setup"]![0]!["kind"]!.GetValue<string>().ShouldBe("operation");
        json["setup"]![0]!["method"]!.GetValue<string>().ShouldBe("POST");
        json.AsObject().ContainsKey("requiresCapability").ShouldBeFalse();
    }

    [Fact]
    public void GivenAssertionAction_WhenSerialized_ThenUsesAssertDiscriminatorAndCamelCaseEnumValue()
    {
        var document = new LocustIrDocument
        {
            Metadata = new LocustIrMetadata("CRUD basic", "CRUD/basic.json", "4.0"),
            Tests =
            [
                new LocustIrTest
                {
                    Id = "test.0",
                    Name = "Read succeeds",
                    Actions =
                    [
                        new LocustIrAssertion
                        {
                            Id = "test.0.assert.0",
                            Criteria = new LocustIrAssertionCriteria
                            {
                                Kind = LocustIrAssertionKind.ResponseStatus,
                                Value = "200"
                            }
                        }
                    ]
                }
            ]
        };

        JsonNode json = JsonNode.Parse(LocustIrSerializer.Serialize(document))!;

        JsonNode assertion = json["tests"]![0]!["actions"]![0]!;
        assertion["kind"]!.GetValue<string>().ShouldBe("assert");
        assertion["criteria"]!["kind"]!.GetValue<string>().ShouldBe("responseStatus");
    }
}

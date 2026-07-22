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
    }
}

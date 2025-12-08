// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization;
using Ignixa.SqlOnFhir.Writers;
using Parquet.Data;
using Parquet.Schema;

namespace Ignixa.SqlOnFhir.Writers.Tests;

public class SchemaExtractorTests
{
    [Fact]
    public void ExtractParquetSchema_ShouldExtractColumnsFromViewDefinition()
    {
        // Arrange
        var viewDefJson = @"{
            ""resourceType"": ""ViewDefinition"",
            ""resource"": ""Patient"",
            ""select"": [
                {
                    ""column"": [
                        {
                            ""name"": ""id"",
                            ""path"": ""id"",
                            ""type"": ""string""
                        },
                        {
                            ""name"": ""family_name"",
                            ""path"": ""name.family"",
                            ""type"": ""string""
                        },
                        {
                            ""name"": ""birth_date"",
                            ""path"": ""birthDate"",
                            ""type"": ""date""
                        }
                    ]
                }
            ]
        }";

        var viewDefNode = JsonSourceNodeFactory.Parse(viewDefJson);
        var viewDefNavigator = viewDefNode!.ToSourceNavigator();

        // Act
        var (schema, columnTypeMap) = SchemaExtractor.ExtractParquetSchema(viewDefNavigator);

        // Assert
        schema.Should().NotBeNull();
        schema.Fields.Should().HaveCount(3);
        schema.Fields[0].Name.Should().Be("id");
        schema.Fields[1].Name.Should().Be("family_name");
        schema.Fields[2].Name.Should().Be("birth_date");

        columnTypeMap.Should().ContainKey("id");
        columnTypeMap["id"].Should().Be("STRING");
        columnTypeMap["family_name"].Should().Be("STRING");
        columnTypeMap["birth_date"].Should().Be("DATE");
    }

    [Fact]
    public void ExtractColumnTypes_ShouldReturnTypeDictionary()
    {
        // Arrange
        var viewDefJson = @"{
            ""resourceType"": ""ViewDefinition"",
            ""resource"": ""Observation"",
            ""select"": [
                {
                    ""column"": [
                        {
                            ""name"": ""value"",
                            ""path"": ""valueQuantity.value"",
                            ""type"": ""decimal""
                        },
                        {
                            ""name"": ""unit"",
                            ""path"": ""valueQuantity.unit"",
                            ""type"": ""string""
                        }
                    ]
                }
            ]
        }";

        var viewDefNode = JsonSourceNodeFactory.Parse(viewDefJson);
        var viewDefNavigator = viewDefNode!.ToSourceNavigator();

        // Act
        var columnTypes = SchemaExtractor.ExtractColumnTypes(viewDefNavigator);

        // Assert
        columnTypes.Should().ContainKeys("value", "unit");
        columnTypes["value"].Should().Be("DECIMAL");
        columnTypes["unit"].Should().Be("STRING");
    }

    // Helper method for other tests
    internal static ParquetSchema CreateSimpleParquetSchema()
    {
        var fields = new DataField[]
        {
            new DataField<string>("id"),
            new DataField<string>("name"),
            new DataField<int?>("age")
        };

        return new ParquetSchema(fields);
    }
}

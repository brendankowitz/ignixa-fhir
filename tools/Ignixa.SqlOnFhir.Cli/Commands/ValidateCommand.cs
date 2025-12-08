// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using Ignixa.Serialization;
using Ignixa.Specification;
using Ignixa.SqlOnFhir.Parsing;

namespace Ignixa.SqlOnFhir.Cli.Commands;

/// <summary>
/// Command for validating ViewDefinition files.
/// </summary>
internal static class ValidateCommand
{
    public static Command Create(IFhirSchemaProvider schemaProvider, string fhirVersion)
    {
        var validateCommand = new Command("validate", "Validate a ViewDefinition file");

        var viewDefinitionOption = new Option<string>("--viewdefinition", "Path to ViewDefinition JSON file") { IsRequired = true };

        validateCommand.AddOption(viewDefinitionOption);

        validateCommand.SetHandler(async (viewDefinitionPath) =>
        {
            await HandleValidateCommand(viewDefinitionPath);
        }, viewDefinitionOption);

        return validateCommand;
    }

    private static async Task HandleValidateCommand(string viewDefinitionPath)
    {
        try
        {
            // Validate file exists
            if (!File.Exists(viewDefinitionPath))
            {
                Console.WriteLine($"✗ ViewDefinition file not found: {viewDefinitionPath}");
                Environment.ExitCode = 1;
                return;
            }

            // Read and parse ViewDefinition
            var viewDefJson = await File.ReadAllTextAsync(viewDefinitionPath);
            var viewDefNode = JsonSourceNodeFactory.Parse(viewDefJson);
            if (viewDefNode == null)
            {
                Console.WriteLine($"✗ Failed to parse ViewDefinition JSON");
                Environment.ExitCode = 1;
                return;
            }

            Console.WriteLine("✓ Valid JSON format");

            var viewDefNavigator = viewDefNode.ToSourceNavigator();

            // Validate it's a ViewDefinition resource
            var resourceType = viewDefNavigator.Children("resourceType").FirstOrDefault()?.Text;
            if (resourceType != "ViewDefinition")
            {
                Console.WriteLine($"✗ Resource is not a ViewDefinition (found: {resourceType ?? "null"})");
                Environment.ExitCode = 1;
                return;
            }

            Console.WriteLine("✓ Resource type is ViewDefinition");

            // Try to parse using ViewDefinitionExpressionParser
            try
            {
                var viewDef = ViewDefinitionExpressionParser.Parse(viewDefNavigator);
                
                Console.WriteLine($"✓ ViewDefinition parsed successfully");
                Console.WriteLine($"  Resource: {viewDef.Resource}");
                Console.WriteLine($"  SELECT groups: {viewDef.Select.Length}");

                var totalColumns = 0;
                foreach (var selectGroup in viewDef.Select)
                {
                    totalColumns += selectGroup.Columns.Length;
                }

                Console.WriteLine($"  Total columns: {totalColumns}");

                if (!viewDef.Constants.IsDefaultOrEmpty)
                {
                    Console.WriteLine($"  Constants: {viewDef.Constants.Length}");
                }

                if (!viewDef.Where.IsDefaultOrEmpty)
                {
                    Console.WriteLine($"  WHERE clauses: {viewDef.Where.Length}");
                }

                // List all columns
                if (totalColumns > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("Columns:");
                    foreach (var selectGroup in viewDef.Select)
                    {
                        foreach (var column in selectGroup.Columns)
                        {
                            var type = string.IsNullOrEmpty(column.Type) ? "inferred" : column.Type;
                            Console.WriteLine($"  - {column.Name} ({type})");
                        }
                    }
                }

                Console.WriteLine();
                Console.WriteLine("✓ ViewDefinition is valid");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Failed to parse ViewDefinition: {ex.Message}");
                Environment.ExitCode = 1;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Error: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            Environment.ExitCode = 1;
        }
    }
}

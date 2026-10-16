// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Commands;
using Ignixa.CliOnFhir.Mcp;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.SearchFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>Every tool returns the same top-level shape, whether the call succeeded, failed or found nothing.</summary>
[Collection("Console")]
public class EnvelopeShapeTests
{
    // Regression (Task 6): at 523e80d there was no 'next'.
    private static readonly string[] TopLevel = ["facts", "evidence", "omitted", "absent", "issues", "freshness", "derived", "next"];

    private static readonly string[] Freshness = ["indexBuiltAt", "staleFiles"];

    [Fact]
    public async Task GivenMcpCalls_WhenSucceededOrFailedOrFolderMissing_ThenTheTopLevelKeysAreTheSame()
    {
        using TestFolder folder = Clinic();
        var tools = new ResourceTools(Open(folder));
        var patients = new PatientTools(Open(folder));
        string missing = Path.Combine(Path.GetTempPath(), "cli-on-fhir-tests", Guid.NewGuid().ToString("N"));
        var missingTools = new ResourceTools(new Ignixa.CliOnFhir.Session.FolderSession(missing, new R4CoreSchemaProvider(), NullLoggerFactory.Instance));

        CallToolResult[] results =
        [
            tools.SearchFhirResources("Patient"),
            tools.SearchFhirResources("Patient", summary: "count"),
            tools.SearchFhirResources("Patient", "_sort=family"),
            tools.ReadResources(["Patient/p1"]),
            tools.ReadResources(["nonsense"]),
            missingTools.SearchFhirResources("Patient"),
            await new FolderTools(Open(folder)).FolderInspect(),
            patients.PatientList(),
            patients.PatientContext("root", "p1"),
            patients.PatientContext("root", "nobody"),
            patients.PatientContext("expand", "p1", nodeKey: "labs"),
            patients.PatientContext("expand", "p1", nodeKey: "nonsense"),
            patients.PatientContext("find", "p1", query: "8867-4"),
        ];

        foreach (CallToolResult result in results)
        {
            JsonObject envelope = JsonNode.Parse(result.Content.OfType<TextContentBlock>().Single().Text)!.AsObject();
            envelope.Select(property => property.Key).ShouldBe(TopLevel, ignoreOrder: true);
            envelope["freshness"]!.AsObject().Select(property => property.Key).ShouldBe(Freshness, ignoreOrder: true);
            envelope["derived"].ShouldBeNull();
            envelope["next"].ShouldBeOfType<JsonArray>();
        }
    }

    [Fact]
    public async Task GivenCliCalls_WhenSucceededOrFailed_ThenTheTopLevelKeysAreTheSameAsOverMcp()
    {
        using TestFolder folder = Clinic();
        string missing = Path.Combine(Path.GetTempPath(), "cli-on-fhir-tests", Guid.NewGuid().ToString("N"));

        foreach (string[] args in new[]
        {
            new[] { "search", folder.Path, "Patient", "--no-cache" },
            new[] { "search", folder.Path, "Patient", "_sort=family", "--no-cache" },
            new[] { "search", missing, "Patient", "--no-cache" },
            new[] { "get", folder.Path, "Patient/none", "--no-cache" },
            new[] { "patients", folder.Path, "--no-cache" },
            new[] { "context", folder.Path, "p1", "--no-cache" },
            new[] { "context", folder.Path, "p1", "--node", "labs/http://loinc.org|8867-4", "--count", "1", "--no-cache" },
            new[] { "find", folder.Path, "p1", "8867-4", "--no-cache" },
        })
        {
            var root = new RootCommand();
            root.Subcommands.Add(args[0] switch
            {
                "search" => SearchCommand.Create(),
                "get" => GetCommand.Create(),
                "patients" => PatientsCommand.Create(),
                "context" => ContextCommand.Create(),
                _ => FindCommand.Create(),
            });
            TextWriter original = Console.Out;
            using var captured = new StringWriter();
            Console.SetOut(captured);
            try
            {
                await root.Parse(args).InvokeAsync();
            }
            finally
            {
                Console.SetOut(original);
            }

            JsonObject envelope = JsonNode.Parse(captured.ToString())!.AsObject();
            envelope.Select(property => property.Key).ShouldBe(TopLevel, ignoreOrder: true);
            envelope["freshness"]!.AsObject().Select(property => property.Key).ShouldBe(Freshness, ignoreOrder: true);
        }
    }
}

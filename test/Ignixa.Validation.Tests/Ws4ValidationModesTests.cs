// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;
using Ignixa.Validation.Tests.TestHelpers;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests;

/// <summary>
/// Covers the WS4 validation modes: default-OFF flags that gate the embedded-HTML (security-checks),
/// markdown-HTML (noHtmlInMarkdown), example-URL (examples) and contained-resource (validateContains)
/// behaviours. The default-off assertions guarantee zero over-strict exposure when a flag is unset.
/// </summary>
public sealed class Ws4ValidationModesTests
{
    private static readonly ISchema Schema = new R4CoreSchemaProvider();

    private static readonly IValidationSchemaResolver Resolver =
        new CachedValidationSchemaResolver(new StructureDefinitionSchemaResolver(Schema));

    private static ValidationResult Validate(string json, ValidationSettings settings)
    {
        var element = JsonNodeSourceNode.Create(JsonNode.Parse(json)!).ToElement(TestSchemaProvider.GetR4Schema());
        var schema = Resolver.GetSchema($"http://hl7.org/fhir/StructureDefinition/{element.InstanceType}")!;
        var state = ValidationState.ForRoot(element);
        return schema.Validate(element, settings, state);
    }

    private static ValidationSettings Full(Action<ValidationSettings>? configure = null)
    {
        var settings = new ValidationSettings { Depth = ValidationDepth.Full };
        configure?.Invoke(settings);
        return settings;
    }

    private const string PatientWithEmbeddedHtmlName = """
    {
        "resourceType": "Patient",
        "name": [{ "text": "Standard <script>somescript</script>" }]
    }
    """;

    private const string CommunicationWithHtmlMarkdown = """
    {
        "resourceType": "Communication",
        "status": "completed",
        "note": [{ "text": "<resource type>" }]
    }
    """;

    private const string DocumentReferenceWithExampleUrl = """
    {
        "resourceType": "DocumentReference",
        "status": "current",
        "content": [{ "attachment": { "url": "http://repository.example.org/fhir/x" } }]
    }
    """;

    [Fact]
    public void GivenEmbeddedHtmlString_WhenSecurityChecksOff_ThenNoSecurityError()
    {
        var result = Validate(PatientWithEmbeddedHtmlName, Full());

        result.Issues.ShouldNotContain(i => i.Code == "security");
    }

    [Fact]
    public void GivenEmbeddedHtmlString_WhenSecurityChecksOn_ThenSecurityErrorFires()
    {
        var result = Validate(PatientWithEmbeddedHtmlName, Full(s => s.SecurityChecks = true));

        result.Issues.ShouldContain(i => i.Code == "security" && i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public void GivenPlainString_WhenSecurityChecksOn_ThenNoSecurityError()
    {
        var result = Validate(
            """{ "resourceType": "Patient", "name": [{ "text": "Jane Doe" }] }""",
            Full(s => s.SecurityChecks = true));

        result.Issues.ShouldNotContain(i => i.Code == "security");
    }

    [Fact]
    public void GivenHtmlInMarkdown_WhenNoHtmlInMarkdownOff_ThenNoError()
    {
        var result = Validate(CommunicationWithHtmlMarkdown, Full());

        result.Issues.ShouldNotContain(i => i.Message.Contains("embedded HTML tag"));
    }

    [Fact]
    public void GivenHtmlInMarkdown_WhenNoHtmlInMarkdownOn_ThenErrorFires()
    {
        var result = Validate(CommunicationWithHtmlMarkdown, Full(s => s.NoHtmlInMarkdown = true));

        result.Issues.ShouldContain(i => i.Severity == IssueSeverity.Error && i.Message.Contains("embedded HTML tag"));
    }

    [Fact]
    public void GivenExampleOrgUrl_WhenCheckExampleUrlsOff_ThenNoError()
    {
        var result = Validate(DocumentReferenceWithExampleUrl, Full());

        result.Issues.ShouldNotContain(i => i.Message.Contains("Example URLs"));
    }

    [Fact]
    public void GivenExampleOrgUrl_WhenCheckExampleUrlsOn_ThenErrorFires()
    {
        var result = Validate(DocumentReferenceWithExampleUrl, Full(s => s.CheckExampleUrls = true));

        result.Issues.ShouldContain(i => i.Severity == IssueSeverity.Error && i.Message.Contains("Example URLs"));
    }

    [Fact]
    public void GivenNonExampleUrl_WhenCheckExampleUrlsOn_ThenNoError()
    {
        // example.com is NOT a reserved FHIR example host (only example.org / acme.com are).
        var result = Validate(
            """
            {
                "resourceType": "DocumentReference",
                "status": "current",
                "content": [{ "attachment": { "url": "http://repository.example.com/fhir/x" } }]
            }
            """,
            Full(s => s.CheckExampleUrls = true));

        result.Issues.ShouldNotContain(i => i.Message.Contains("Example URLs"));
    }

    [Fact]
    public void GivenContainedResourceWithBadId_WhenValidateContainedOff_ThenNoIdError()
    {
        // A 65-char contained id violates the id datatype, but validateContains=IGNORE skips it.
        var json = """
        {
            "resourceType": "Condition",
            "subject": { "reference": "http://example.org/x" },
            "contained": [{
                "resourceType": "Practitioner",
                "id": "12345678901234567890123456789012345678901234567890123456789012345"
            }]
        }
        """;

        var ignored = Validate(json, Full(s => s.ValidateContainedResources = false));
        var checked_ = Validate(json, Full(s => s.ValidateContainedResources = true));

        ignored.Issues.ShouldNotContain(i => i.Code == "type-1");
        checked_.Issues.ShouldContain(i => i.Code == "type-1");
    }
}

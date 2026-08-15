// <copyright file="NestingDepthGuardTests.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;
using Ignixa.Validation.Tests.TestHelpers;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests;

/// <summary>
/// The compiled schema graph is cycle-guarded at build time, so schema recursion is finite. Instance
/// recursion is not: contained-within-contained nests as deep as the JSON does, and every level costs
/// a stack frame per check in the chain. <see cref="ValidationState.MaxNestingDepth"/> bounds it.
/// </summary>
public class NestingDepthGuardTests
{
    private readonly ISchema _schema = TestSchemaProvider.GetR4Schema();
    private readonly IValidationSchemaResolver _resolver;

    public NestingDepthGuardTests()
    {
        _resolver = new CachedValidationSchemaResolver(new StructureDefinitionSchemaResolver(_schema));
    }

    [Fact]
    public void GivenAFreshState_WhenDescending_ThenTheDepthIncrementsWithoutMutatingTheOriginal()
    {
        // Arrange
        var state = new ValidationState();

        // Act
        var descended = state.TryDescend(out var next);

        // Assert
        descended.ShouldBeTrue();
        next.NestingDepth.ShouldBe(1);
        state.NestingDepth.ShouldBe(0);
    }

    [Fact]
    public void GivenAStateAtTheDepthLimit_WhenDescending_ThenItRefuses()
    {
        // Arrange
        var state = new ValidationState();
        for (var i = 0; i < ValidationState.MaxNestingDepth; i++)
        {
            state.TryDescend(out state).ShouldBeTrue();
        }

        // Act
        var descended = state.TryDescend(out var next);

        // Assert
        descended.ShouldBeFalse();
        next.NestingDepth.ShouldBe(ValidationState.MaxNestingDepth);
    }

    /// <summary>
    /// A resource nested far past the limit must terminate and say so. Reporting a clean result for a
    /// subtree the validator never entered would be the worst of both outcomes.
    /// </summary>
    /// <remarks>
    /// The instance is parsed with a raised <see cref="JsonDocumentOptions.MaxDepth"/> on purpose.
    /// System.Text.Json defaults to 64 and each contained level costs two JSON levels, so the default
    /// parser rejects this document long before the validator sees it - which is the outer guard, not
    /// this one. The validator's limit is the backstop for callers that raise the parser's ceiling or
    /// feed the element tree from a non-JSON source, and this is the only way to exercise it.
    /// </remarks>
    [Fact]
    public void GivenContainedResourcesNestedPastTheLimit_WhenValidatingAtFull_ThenTraversalStopsAndReports()
    {
        // Arrange
        var resource = NestedContainedPatients(ValidationState.MaxNestingDepth + 8);

        // Act
        var result = Validate(resource, "Patient", ValidationDepth.Full);

        // Assert — the truncation is surfaced, and it does not by itself invalidate the resource.
        result.Issues.ShouldContain(
            i => i.Code == "validation-nesting-limit" && i.Severity == IssueSeverity.Warning,
            Describe(result));
    }

    /// <summary>
    /// The guard must be far enough out that no realistic instance trips it: a nesting depth that
    /// FHIR's own dom-2 forbids outright still validates without a truncation warning.
    /// </summary>
    [Fact]
    public void GivenContainedResourcesNestedWellInsideTheLimit_WhenValidatingAtFull_ThenNoTruncationIsReported()
    {
        // Arrange
        var resource = NestedContainedPatients(8);

        // Act
        var result = Validate(resource, "Patient", ValidationDepth.Full);

        // Assert
        result.Issues.ShouldNotContain(i => i.Code == "validation-nesting-limit", Describe(result));
    }

    /// <summary>
    /// Builds <paramref name="levels"/> Patients each contained in the one above.
    /// </summary>
    private static string NestedContainedPatients(int levels)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < levels; i++)
        {
            sb.Append($$"""{"resourceType":"Patient","id":"p{{i}}","contained":[""");
        }

        sb.Append("""{"resourceType":"Patient","id":"leaf"}""");
        for (var i = 0; i < levels; i++)
        {
            sb.Append("]}");
        }

        return sb.ToString();
    }

    private ValidationResult Validate(string resourceJson, string resourceType, ValidationDepth depth)
    {
        var json = JsonNode.Parse(
            resourceJson,
            nodeOptions: null,
            documentOptions: new JsonDocumentOptions { MaxDepth = 512 })!;
        var sourceNode = JsonNodeSourceNode.Create(json);
        var schema = _resolver.GetSchema($"http://hl7.org/fhir/StructureDefinition/{resourceType}")
            ?? throw new InvalidOperationException($"No schema for {resourceType}");

        return schema.Validate(
            sourceNode.ToElement(_schema),
            new ValidationSettings { Depth = depth },
            new ValidationState());
    }

    private static string Describe(ValidationResult result)
        => result.Issues.Count == 0
            ? "(no issues)"
            : string.Join(" | ", result.Issues.Take(20).Select(i => $"{i.Severity}:{i.Code}@{i.Path}"));
}

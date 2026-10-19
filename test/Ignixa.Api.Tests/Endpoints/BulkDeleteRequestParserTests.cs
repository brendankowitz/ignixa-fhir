using System.Text;
using Ignixa.Api.Endpoints;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Shouldly;
using Xunit;

namespace Ignixa.Api.Tests.Endpoints;

public sealed class BulkDeleteRequestParserTests
{
    private static readonly ReadOnlyMemory<byte> NoBody = ReadOnlyMemory<byte>.Empty;

    [Fact]
    public void GivenMissingPreferHeader_WhenParsing_ThenBadRequestIsThrown()
    {
        var exception = Should.Throw<BadRequestException>(() =>
            BulkDeleteRequestParser.Parse([], preferHeader: null, NoBody));

        exception.Message.ShouldContain("respond-async");
    }

    [Theory]
    [InlineData("respond-async")]
    [InlineData("respond-async, handling=strict")]
    [InlineData("handling=strict, RESPOND-ASYNC")]
    public void GivenPreferHeaderWithRespondAsyncToken_WhenParsing_ThenRequestIsAccepted(string preferHeader)
    {
        var result = BulkDeleteRequestParser.Parse([], preferHeader, NoBody);

        result.Mode.ShouldBe(BulkDeleteMode.SoftDelete);
    }

    [Fact]
    public void GivenHardDeleteQueryFlag_WhenParsing_ThenModeIsHardDelete()
    {
        var result = BulkDeleteRequestParser.Parse(
            [new("_hardDelete", "true")], "respond-async", NoBody);

        result.Mode.ShouldBe(BulkDeleteMode.HardDelete);
    }

    [Fact]
    public void GivenHardDeleteQueryAlias_WhenParsing_ThenModeIsHardDelete()
    {
        var result = BulkDeleteRequestParser.Parse(
            [new("hardDelete", "TRUE")], "respond-async", NoBody);

        result.Mode.ShouldBe(BulkDeleteMode.HardDelete);
    }

    [Fact]
    public void GivenPurgeHistoryQueryFlag_WhenParsing_ThenModeIsPurgeHistory()
    {
        var result = BulkDeleteRequestParser.Parse(
            [new("_purgeHistory", "true")], "respond-async", NoBody);

        result.Mode.ShouldBe(BulkDeleteMode.PurgeHistory);
    }

    [Fact]
    public void GivenHardDeleteAndPurgeHistory_WhenParsing_ThenHardDeleteTakesPrecedence()
    {
        var result = BulkDeleteRequestParser.Parse(
            [new("_hardDelete", "true"), new("_purgeHistory", "true")], "respond-async", NoBody);

        result.Mode.ShouldBe(BulkDeleteMode.HardDelete);
    }

    [Fact]
    public void GivenRemoveReferencesAlias_WhenParsing_ThenRemoveReferencesIsTrue()
    {
        var result = BulkDeleteRequestParser.Parse(
            [new("removeReferences", "true")], "respond-async", NoBody);

        result.RemoveReferences.ShouldBeTrue();
    }

    [Fact]
    public void GivenInvalidBooleanValue_WhenParsing_ThenBadRequestIsThrown()
    {
        Should.Throw<BadRequestException>(() =>
            BulkDeleteRequestParser.Parse([new("_hardDelete", "maybe")], "respond-async", NoBody));
    }

    [Fact]
    public void GivenRepeatedControlParamWithConflictingValues_WhenParsing_ThenBadRequestIsThrown()
    {
        Should.Throw<BadRequestException>(() =>
            BulkDeleteRequestParser.Parse(
                [new("_hardDelete", "true"), new("hardDelete", "false")], "respond-async", NoBody));
    }

    [Fact]
    public void GivenExcludedResourceTypesCsvAcrossMultipleOccurrences_WhenParsing_ThenValuesAreFlattenedTrimmedAndDeduplicated()
    {
        var result = BulkDeleteRequestParser.Parse(
            [
                new("excludedResourceTypes", "Patient, Observation"),
                new("excludedResourceTypes", " Observation ,Encounter"),
            ],
            "respond-async",
            NoBody);

        result.ExcludedResourceTypes.ShouldBe(["Patient", "Observation", "Encounter"]);
    }

    [Fact]
    public void GivenControlParamsAndType_WhenParsing_ThenControlParamsAreRemovedAndTypeIsRetained()
    {
        var result = BulkDeleteRequestParser.Parse(
            [
                new("_hardDelete", "true"),
                new("excludedResourceTypes", "Patient"),
                new("_type", "Observation"),
                new("_include", "Observation:patient"),
            ],
            "respond-async",
            NoBody);

        result.SearchParameters.ShouldBe(
        [
            new("_type", "Observation"),
            new("_include", "Observation:patient"),
        ]);
    }

    [Fact]
    public void GivenAhdsClientParametersBody_WhenParsing_ThenHardDeleteModeIsSelected()
    {
        var body = Encode("""{"resourceType":"Parameters","parameter":[{"name":"hardDelete","valueBoolean":true},{"name":"purgeHistory","valueBoolean":true}]}""");

        var result = BulkDeleteRequestParser.Parse([], "respond-async", body);

        result.Mode.ShouldBe(BulkDeleteMode.HardDelete);
    }

    [Fact]
    public void GivenUnderscorePrefixedBodyParameterNames_WhenParsing_ThenTheyAreAccepted()
    {
        var body = Encode("""{"resourceType":"Parameters","parameter":[{"name":"_purgeHistory","valueBoolean":true}]}""");

        var result = BulkDeleteRequestParser.Parse([], "respond-async", body);

        result.Mode.ShouldBe(BulkDeleteMode.PurgeHistory);
    }

    [Fact]
    public void GivenConflictingQueryAndBodyFlags_WhenParsing_ThenBadRequestIsThrown()
    {
        var body = Encode("""{"resourceType":"Parameters","parameter":[{"name":"hardDelete","valueBoolean":false}]}""");

        Should.Throw<BadRequestException>(() =>
            BulkDeleteRequestParser.Parse([new("_hardDelete", "true")], "respond-async", body));
    }

    [Fact]
    public void GivenAgreeingQueryAndBodyFlags_WhenParsing_ThenRequestIsAccepted()
    {
        var body = Encode("""{"resourceType":"Parameters","parameter":[{"name":"hardDelete","valueBoolean":true}]}""");

        var result = BulkDeleteRequestParser.Parse([new("_hardDelete", "true")], "respond-async", body);

        result.Mode.ShouldBe(BulkDeleteMode.HardDelete);
    }

    [Fact]
    public void GivenUnknownBodyParameter_WhenParsing_ThenBadRequestIsThrown()
    {
        var body = Encode("""{"resourceType":"Parameters","parameter":[{"name":"excludedResourceTypes","valueString":"Patient"}]}""");

        Should.Throw<BadRequestException>(() =>
            BulkDeleteRequestParser.Parse([], "respond-async", body));
    }

    [Fact]
    public void GivenNonParametersBody_WhenParsing_ThenBadRequestIsThrown()
    {
        var body = Encode("""{"resourceType":"Patient"}""");

        Should.Throw<BadRequestException>(() =>
            BulkDeleteRequestParser.Parse([], "respond-async", body));
    }

    [Fact]
    public void GivenMalformedJsonBody_WhenParsing_ThenBadRequestIsThrown()
    {
        var body = Encode("{not json");

        Should.Throw<BadRequestException>(() =>
            BulkDeleteRequestParser.Parse([], "respond-async", body));
    }

    [Fact]
    public void GivenNonBooleanBodyValue_WhenParsing_ThenBadRequestIsThrown()
    {
        var body = Encode("""{"resourceType":"Parameters","parameter":[{"name":"hardDelete","valueString":"true"}]}""");

        Should.Throw<BadRequestException>(() =>
            BulkDeleteRequestParser.Parse([], "respond-async", body));
    }

    [Theory]
    [InlineData("""{"resourceType":"Parameters","parameter":{"name":"hardDelete","valueBoolean":true}}""")]
    [InlineData("""{"resourceType":"Parameters","parameter":"hardDelete"}""")]
    [InlineData("""{"resourceType":"Parameters","parameter":null}""")]
    public void GivenAParameterPropertyThatIsNotAnArray_WhenParsing_ThenBadRequestIsThrown(string json)
    {
        var exception = Should.Throw<BadRequestException>(() =>
            BulkDeleteRequestParser.Parse([], "respond-async", Encode(json)));

        exception.Message.ShouldContain("'parameter' must be an array");
    }

    [Fact]
    public void GivenNoControlFlags_WhenParsing_ThenModeIsSoftDelete()
    {
        var result = BulkDeleteRequestParser.Parse([], "respond-async", NoBody);

        result.Mode.ShouldBe(BulkDeleteMode.SoftDelete);
        result.RemoveReferences.ShouldBeFalse();
        result.ExcludedResourceTypes.ShouldBeEmpty();
    }

    private static ReadOnlyMemory<byte> Encode(string json) => Encoding.UTF8.GetBytes(json);
}

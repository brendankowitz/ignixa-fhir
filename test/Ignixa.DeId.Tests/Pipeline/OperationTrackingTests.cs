// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Collections.Immutable;
using System.Text;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.DeId.Configuration;
using Ignixa.DeId.Extensions;
using Ignixa.DeId.Models;
using Ignixa.DeId.Tools;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ignixa.DeId.Tests.Pipeline;

public class OperationTrackingTests
{
    private const string EncryptionKey = "1234567890123456";

    [Theory]
    [InlineData("REDACT", "valueString", "\"abc\"", null, "REDACTED", "REDACT")]
    [InlineData("KEEP", "valueString", "\"abc\"", "abc", null, null)]
    [InlineData("CRYPTOHASH", "valueString", "\"abc\"", "8f16771f9f8851b26f4d460fa17de93e2711c7e51337cb8a608a0f81e1c1b6ae", "CRYTOHASH", "CRYPTOHASH")]
    [InlineData("ENCRYPT", "valueString", "\"abc\"", "abc", "MASKED", "ENCRYPT")]
    [InlineData("PERTURB", "valueDecimal", "12.345", "12.34", "PERTURBED", "PERTURB")]
    [InlineData("SUBSTITUTE", "valueString", "\"abc\"", "replacement", "SUBSTITUTED", "SUBSTITUTE")]
    [InlineData("GENERALIZE", "valueString", "\"abc\"", "general", "GENERALIZED", "GENERALIZE")]
    [InlineData("DATESHIFT", "effectiveDateTime", "\"2015-02-07T12:30:00Z\"", "2015-02-10T00:00:00Z", "PERTURBED", "PERTURB")]
    public async Task GivenSingleOperation_WhenDeidentifying_ThenValueMetricsAndOnlyMatchingLabelAreReturned(
        string method, string field, string input, string? expected, string? label, string? operation)
    {
        ImmutableDictionary<string, object>? settings = method switch
        {
            "PERTURB" => new Dictionary<string, object> { ["span"] = 0, ["roundTo"] = 2 }.ToImmutableDictionary(),
            "SUBSTITUTE" => new Dictionary<string, object> { ["replaceWith"] = "replacement" }.ToImmutableDictionary(),
            "GENERALIZE" => new Dictionary<string, object> { ["cases"] = """{"true":"'general'"}""" }.ToImmutableDictionary(),
            _ => null
        };
        using var provider = CreateProvider(new FhirPathRule
        {
            Path = $"Observation.{field}", Method = method, Settings = settings
        });
        var engine = provider.GetRequiredService<IDeIdEngine>();

        var result = await engine.DeidentifyAsync($$"""{"resourceType":"Observation","{{field}}":{{input}}}""");

        result.IsSuccess.ShouldBeTrue();
        var element = result.Value.Resource.ToElement(provider.GetRequiredService<IFhirSchemaProvider>());
        var value = element.Scalar(field)?.ToString();
        if (method == "ENCRYPT")
        {
            value.ShouldNotBe("abc");
            value = EncryptTool.DecryptTextFromBase64WithAes(value, Encoding.UTF8.GetBytes(EncryptionKey));
        }
        value.ShouldBe(expected);
        result.Value.AppliedLabels.ShouldBe(new AppliedSecurityLabels
        {
            IsRedacted = method == "REDACT",
            IsCryptoHashed = method == "CRYPTOHASH",
            IsEncrypted = method == "ENCRYPT",
            IsPerturbed = operation == "PERTURB",
            IsSubstituted = method == "SUBSTITUTE",
            IsGeneralized = method == "GENERALIZE"
        });
        if (operation is null)
        {
            result.Value.Metrics.OperationCounts.ShouldBeEmpty();
            result.Value.Metrics.NodesProcessed.ShouldBe(0);
        }
        else
        {
            result.Value.Metrics.OperationCounts.Count.ShouldBe(1);
            result.Value.Metrics.OperationCounts[operation].ShouldBe(1);
            result.Value.Metrics.NodesProcessed.ShouldBe(1);
        }
        var labels = element.Select("meta.security.code").Select(code => code.Value?.ToString()).ToArray();
        labels.ShouldBe(label is null ? [] : [label]);
    }

    [Theory]
    [InlineData("REDACTED")]
    [InlineData("redacted")]
    public async Task GivenExistingSecurityLabel_WhenRedacting_ThenMetadataIsPreservedWithoutDuplicateLabels(string code)
    {
        using var provider = CreateProvider(new FhirPathRule { Path = "Observation.valueString", Method = "REDACT" });
        var engine = provider.GetRequiredService<IDeIdEngine>();
        var json = $$"""
            {"resourceType":"Observation","meta":{"versionId":"7","security":[
                {"system":"http://terminology.hl7.org/CodeSystem/v3-ObservationValue","code":"{{code}}"},
                {"system":"http://example.org/security","code":"OTHER"}]},"valueString":"secret"}
            """;

        var result = await engine.DeidentifyAsync(json);

        result.IsSuccess.ShouldBeTrue();
        var output = JsonNode.Parse(result.Value.DeidentifiedJson)!;
        output["valueString"].ShouldBeNull();
        output["meta"]!["versionId"]!.GetValue<string>().ShouldBe("7");
        var labels = output["meta"]!["security"]!.AsArray();
        labels.Count.ShouldBe(2);
        labels[0]!["code"]!.GetValue<string>().ShouldBe(code);
        labels[1]!["code"]!.GetValue<string>().ShouldBe("OTHER");
    }

    private static ServiceProvider CreateProvider(FhirPathRule rule)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFhirSchemaProvider>(new R4CoreSchemaProvider());
        services.AddSingleton<IValidationSchemaResolver>(sp =>
            new CachedValidationSchemaResolver(
                new StructureDefinitionSchemaResolver(sp.GetRequiredService<IFhirSchemaProvider>())));
        services.AddSingleton(Options.Create(new DeIdOptions
        {
            FhirVersion = "R4",
            Rules = [rule],
            Parameters = new ParameterOptions
            {
                CryptoHashKey = "123",
                EncryptKey = EncryptionKey,
                DateShiftKey = "dummy",
                DateShiftFixedOffsetInDays = 3
            }
        }));
        services.AddLogging();
        services.AddFhirDeId();
        return services.BuildServiceProvider();
    }
}

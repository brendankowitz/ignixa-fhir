// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Session;
using Ignixa.CliOnFhir.Terminology;
using Ignixa.CliOnFhir.Terminology.Packages;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// A hand-written FHIR npm package and a stub packages.fhir.org serving it. The CodeSystem <see cref="Body"/> has a
/// three-level hierarchy built three ways: disorder &gt; heart &gt; mi by nesting, disorder &gt; lung by a <c>child</c>
/// property, lung &gt; asthma by <c>subsumedBy</c>; <c>old</c> (under disorder) is retired, <c>device</c> is unrelated.
/// </summary>
internal static class TerminologyFixture
{
    public const string PackageId = "example.terminology";
    public const string PackageVersion = "1.0.0";
    public const string Body = "http://example.org/fhir/CodeSystem/body";
    public const string Partial = "http://example.org/fhir/CodeSystem/partial";
    public const string Snomed = "http://snomed.info/sct";
    public const string Icd = "http://hl7.org/fhir/sid/icd-10";
    public const string ValueSetBase = "http://example.org/fhir/ValueSet/";
    public const string ConceptMapUrl = "http://example.org/fhir/ConceptMap/body-to-icd";

    public static string BodyCodeSystem(string version = "1.0.0", string heartDisplay = "Heart disorder") => $$"""
        {"resourceType":"CodeSystem","id":"body","url":"{{Body}}","version":"{{version}}","status":"active","content":"complete","hierarchyMeaning":"is-a",
         "property":[{"code":"status","uri":"http://hl7.org/fhir/concept-properties#status","type":"code"},
                     {"code":"subsumedBy","uri":"http://hl7.org/fhir/concept-properties#parent","type":"code"},
                     {"code":"child","uri":"http://hl7.org/fhir/concept-properties#child","type":"code"}],
         "concept":[
           {"code":"disorder","display":"Disorder","definition":"Any disorder.","property":[{"code":"child","valueCode":"lung"}],
            "concept":[
              {"code":"heart","display":"{{heartDisplay}}","designation":[{"language":"de","value":"Herzkrankheit"}],
               "concept":[{"code":"mi","display":"Myocardial infarction"}]},
              {"code":"old","display":"Old heart disorder","property":[{"code":"status","valueCode":"retired"}]}]},
           {"code":"lung","display":"Lung disorder"},
           {"code":"asthma","display":"Asthma","property":[{"code":"subsumedBy","valueCode":"lung"}]},
           {"code":"device","display":"Device"}]}
        """;

    public static IReadOnlyList<(string Path, string Content)> Files(string? bodyCodeSystem = null) =>
    [
        ("package/package.json", $$"""{"name":"{{PackageId}}","version":"{{PackageVersion}}","fhirVersions":["4.0.1"]}"""),
        ("package/CodeSystem-body.json", bodyCodeSystem ?? BodyCodeSystem()),
        ("package/CodeSystem-partial.json", $$"""{"resourceType":"CodeSystem","id":"partial","url":"{{Partial}}","version":"2","content":"fragment","concept":[{"code":"a","display":"A","concept":[{"code":"b","display":"B"}]},{"code":"c","display":"C"}]}"""),
        ("package/CodeSystem-snomed.json", $$"""{"resourceType":"CodeSystem","id":"snomed","url":"{{Snomed}}","content":"not-present"}"""),
        ValueSet("heart", $$"""{"include":[{"system":"{{Body}}","filter":[{"property":"concept","op":"is-a","value":"heart"}]}]}"""),
        ValueSet("current-disorders", $$"""{"include":[{"system":"{{Body}}","filter":[{"property":"concept","op":"is-a","value":"disorder"}]}],"exclude":[{"system":"{{Body}}","concept":[{"code":"old"}]}]}"""),
        ValueSet("below-disorder", $$"""{"include":[{"system":"{{Body}}","filter":[{"property":"concept","op":"descendent-of","value":"disorder"}]}]}"""),
        ValueSet("regex", $$"""{"include":[{"system":"{{Body}}","filter":[{"property":"code","op":"regex","value":"^h.*"}]}]}"""),
        ValueSet("snomed-listed", $$"""{"include":[{"system":"{{Snomed}}","concept":[{"code":"22298006","display":"Myocardial infarction"}]}]}"""),
        ValueSet("snomed-filter", $$"""{"include":[{"system":"{{Snomed}}","filter":[{"property":"concept","op":"is-a","value":"404684003"}]}]}"""),
        ValueSet("nested", $$"""{"include":[{"valueSet":["{{ValueSetBase}}heart","{{ValueSetBase}}current-disorders"]}]}"""),
        ValueSet("partial-all", $$"""{"include":[{"system":"{{Partial}}"}]}"""),
        ("package/ConceptMap-body-to-icd.json", $$$"""
            {"resourceType":"ConceptMap","id":"body-to-icd","url":"{{{ConceptMapUrl}}}","version":"1","sourceUri":"{{{ValueSetBase}}}current-disorders",
             "group":[{"source":"{{{Body}}}","target":"{{{Icd}}}","element":[
                {"code":"mi","target":[{"code":"I21","display":"Acute myocardial infarction","equivalence":"equivalent"}]},
                {"code":"heart","target":[{"code":"I51.9","equivalence":"wider","comment":"unspecified"}]},
                {"code":"asthma","target":[{"equivalence":"unmatched"}]}],
              "unmapped":{"mode":"fixed","code":"R69","display":"Unknown"}}]}
            """),
        ("package/StructureDefinition-x.json", """{"resourceType":"StructureDefinition","id":"x","url":"http://example.org/fhir/StructureDefinition/x"}"""),
        ("package/.index.json", """{"index-version":1,"files":[]}"""),
        ("package/example/CodeSystem-ignored.json", """{"resourceType":"CodeSystem","id":"ignored","url":"http://example.org/fhir/CodeSystem/ignored","content":"complete"}"""),
        ("other/spec.internals", "not json"),
    ];

    /// <summary>A gzip tar of <paramref name="files"/>, written the way npm packs (regular file entries, POSIX paths).</summary>
    public static byte[] Archive(IEnumerable<(string Path, string Content)> files)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        using (var tar = new TarWriter(gzip, TarEntryFormat.Ustar, leaveOpen: true))
        {
            foreach ((string path, string content) in files)
            {
                tar.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, path) { DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content)) });
            }
        }

        return output.ToArray();
    }

    public static string Sha1(byte[] bytes) =>
#pragma warning disable CA5350 // The registry publishes SHA-1; the stub must too.
        Convert.ToHexStringLower(SHA1.HashData(bytes));
#pragma warning restore CA5350

    /// <summary>A stub registry serving <paramref name="archive"/> as <see cref="PackageId"/>@<see cref="PackageVersion"/> (the latest), or the given packages.</summary>
    public static StubHttpHandler Registry(byte[] archive, string? shasum = null) => Registry([(PackageId, PackageVersion, archive, shasum)]);

    public static StubHttpHandler Registry(IReadOnlyList<(string Id, string Version, byte[] Archive, string? Shasum)> packages) => new(request =>
    {
        string path = request.RequestUri!.AbsolutePath.Trim('/');
        if (request.RequestUri.Host != "packages.fhir.org")
        {
            return StubHttpHandler.Status(HttpStatusCode.Forbidden);
        }

        foreach (IGrouping<string, (string Id, string Version, byte[] Archive, string? Shasum)> package in packages.GroupBy(package => package.Id))
        {
            if (path == package.Key)
            {
                var versions = new JsonObject();
                foreach ((string _, string version, byte[] bytes, string? shasum) in package)
                {
                    versions[version] = new JsonObject { ["name"] = package.Key, ["version"] = version, ["dist"] = new JsonObject { ["shasum"] = shasum ?? Sha1(bytes), ["tarball"] = $"https://packages.simplifier.net/{package.Key}/{version}" } };
                }

                var document = new JsonObject { ["name"] = package.Key, ["dist-tags"] = new JsonObject { ["latest"] = package.Last().Version }, ["versions"] = versions };
                return StubHttpHandler.Bytes(Encoding.UTF8.GetBytes(document.ToJsonString()), "application/json");
            }

            foreach ((string _, string version, byte[] bytes, string? _) in package)
            {
                if (path == $"{package.Key}/{version}")
                {
                    return StubHttpHandler.Bytes(bytes);
                }
            }
        }

        return StubHttpHandler.Status(HttpStatusCode.NotFound);
    });

    /// <summary>Options that load the fixture package (or <paramref name="packages"/>) from the stub registry into <paramref name="storeFolder"/>.</summary>
    public static TerminologyOptions Online(StubHttpHandler registry, TestFolder storeFolder, params string[] packages) => new()
    {
        Packages = [.. (packages.Length == 0 ? [$"{PackageId}@{PackageVersion}"] : packages).Select(PackageRequest.Parse)],
        Store = PackageStore.Under(storeFolder.Path),
        Registry = new FhirPackageRegistry(registry),
    };

    public static TerminologyOptions Offline(TestFolder storeFolder, params string[] packages) => new()
    {
        Packages = [.. (packages.Length == 0 ? [$"{PackageId}@{PackageVersion}"] : packages).Select(PackageRequest.Parse)],
        Store = PackageStore.Under(storeFolder.Path),
    };

    public static FolderSession Open(TestFolder folder, TerminologyOptions options) =>
        new(folder.Path, new R4CoreSchemaProvider(), NullLoggerFactory.Instance, terminology: options);

    /// <summary>A session over <paramref name="folder"/> with the fixture package served by a fresh stub registry.</summary>
#pragma warning disable CA2000 // The registry client owns the stub handler; both live as long as the session, as in the tool.
    public static FolderSession OpenWithPackage(TestFolder folder, TestFolder storeFolder) =>
        Open(folder, Online(Registry(Archive(Files())), storeFolder));
#pragma warning restore CA2000

    public static JsonNode Json<TFacts>(ToolEnvelope<TFacts> envelope) =>
        JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(envelope, EnvelopeJson.Compact))!;

    public static JsonNode Json(CallToolResult result) => JsonNode.Parse(result.Content.OfType<TextContentBlock>().Single().Text)!;

    private static (string Path, string Content) ValueSet(string id, string compose) =>
        ($"package/ValueSet-{id}.json", $$"""{"resourceType":"ValueSet","id":"{{id}}","url":"{{ValueSetBase}}{{id}}","version":"1","compose":{{compose}}}""");
}

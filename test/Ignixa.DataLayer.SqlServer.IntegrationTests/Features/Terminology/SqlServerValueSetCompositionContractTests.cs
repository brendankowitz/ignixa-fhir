using System.Text.Json.Nodes;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Validation.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Features.Terminology;

public class SqlServerValueSetCompositionContractTests(TerminologyReplacementFixture fixture)
    : IClassFixture<TerminologyReplacementFixture>
{
    private TerminologyTestFixture Database => fixture.Database;

    [Theory]
    [InlineData(null, false, false)]
    [InlineData(null, false, true)]
    [InlineData(null, true, false)]
    [InlineData("V1", false, false)]
    [InlineData("V1", true, false)]
    [InlineData("V2", false, false)]
    [InlineData("V2", true, false)]
    public async Task GivenResidualReferenceVersionRestriction_WhenComparedWithExplicitRestriction_ThenCertaintyAndCodesAgree(
        string? sourceVersion, bool exclude, bool reverseReferences)
    {
        var root = Root();
        await ImportAsync(Expanded(root + "/A", root + "/cs", sourceVersion, "a"));
        await ImportAsync(Expanded(root + "/E", root + "/cs", "V1", "a"));
        var explicitClause = Concepts(root + "/cs", "a");
        explicitClause["version"] = "V1";
        if (!exclude)
        {
            explicitClause["valueSet"] = new JsonArray(root + "/A");
        }
        var referenceClause = exclude ? References(root + "/E")
            : reverseReferences ? References(root + "/E", root + "/A") : References(root + "/A", root + "/E");
        foreach (var (name, clause) in new[] { ("direct", explicitClause), ("reference", referenceClause) })
        {
            var compose = new JsonObject { ["include"] = new JsonArray(exclude ? References(root + "/A") : clause) };
            if (exclude)
            {
                compose["exclude"] = new JsonArray(clause);
            }
            await ImportAsync(Composed(root + "/" + name, compose));
        }

        var expected = exclude ? sourceVersion == "V1" ? Array.Empty<string>() : ["a"]
            : sourceVersion == "V1" ? ["a"] : Array.Empty<string>();
        var direct = await AssertExpansionAsync(root + "/direct", expected, sourceVersion is null);
        var reference = await AssertExpansionAsync(root + "/reference", expected, sourceVersion is null);
        reference.Contains.Select(row => row.Version).ShouldBe(direct.Contains.Select(row => row.Version));
        if (sourceVersion is null)
        {
            (await Database.ExecuteScalarAsync<string>(
                $"SELECT PartialExpansionReason FROM dbo.TermValueSet WHERE Canonical = '{root}/reference'"))
                .ShouldContain("unknown system versions");
        }
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("*", false)]
    [InlineData(null, true)]
    [InlineData("*", true)]
    public async Task GivenResidualUnqualifiedConceptSelection_WhenAppliedToVersionedCodes_ThenAllVersionBehaviorIsPreserved(
        string? version, bool exclude)
    {
        var root = Root();
        await ImportAsync(Expanded(root + "/A", root + "/cs", "V1", "a"));
        await ImportAsync(Expanded(root + "/E", root + "/cs", null, "a"));
        var clause = Concepts(root + "/cs", "a");
        if (version is not null)
        {
            clause["version"] = version;
        }
        if (!exclude)
        {
            clause["valueSet"] = new JsonArray(root + "/A");
        }
        var compose = new JsonObject { ["include"] = new JsonArray(exclude ? References(root + "/A") : clause) };
        if (exclude)
        {
            compose["exclude"] = new JsonArray(clause);
        }
        await ImportAsync(Composed(root + "/direct", compose));
        var result = await AssertExpansionAsync(root + "/direct", exclude ? [] : ["a"]);
        if (!exclude)
        {
            result.Contains.ShouldHaveSingleItem().Version.ShouldBe("V1");
        }
        else
        {
            await ImportAsync(Composed(root + "/reference", new JsonObject
            {
                ["include"] = new JsonArray(References(root + "/A")),
                ["exclude"] = new JsonArray(References(root + "/E")),
            }));
            await AssertExpansionAsync(root + "/reference", []);
        }
    }

    [Theory]
    [InlineData(false, "intersection")]
    [InlineData(true, "intersection")]
    [InlineData(false, "exclude")]
    [InlineData(true, "exclude")]
    [InlineData(false, "union")]
    [InlineData(true, "union")]
    [InlineData(false, "concept")]
    [InlineData(true, "concept")]
    public async Task GivenResidualVersionSpecificCodeCasePolicy_WhenCombiningCodes_ThenOnlyApplicablePolicyControlsEquality(
        bool caseSensitive, string operation)
    {
        var root = Root();
        await ImportCasePolicyAsync(root + "/cs", "V1", caseSensitive);
        await ImportCasePolicyAsync(root + "/cs", "V2", !caseSensitive);
        await ImportAsync(Expanded(root + "/A", root + "/cs", "V1", "ABC"));
        await ImportAsync(Expanded(root + "/B", root + "/cs", "V1", "abc"));
        JsonObject compose;
        if (operation == "concept")
        {
            var clause = Concepts(root + "/cs", "ABC");
            clause["valueSet"] = new JsonArray(root + "/B");
            compose = new JsonObject { ["include"] = new JsonArray(clause) };
        }
        else
        {
            compose = ReferenceOperation(root, operation);
        }

        await ImportAsync(Composed(root + "/result", compose));

        string[] expected = operation switch
        {
            "intersection" or "concept" => caseSensitive ? [] : ["ABC"],
            "exclude" => caseSensitive ? ["ABC"] : [],
            "union" => caseSensitive ? ["ABC", "abc"] : ["ABC"],
            _ => throw new InvalidOperationException(),
        };
        var expansion = await AssertExpansionAsync(root + "/result", expected);
        expansion.Contains.ShouldAllBe(row => row.Version == "V1");
    }

    [Theory]
    [InlineData("intersection")]
    [InlineData("exclude")]
    [InlineData("union")]
    public async Task GivenResidualInsensitiveCodesInDistinctVersions_WhenCombiningReferences_ThenVersionsRemainDistinct(string operation)
    {
        var root = Root();
        await ImportCasePolicyAsync(root + "/cs", "V1", false);
        await ImportCasePolicyAsync(root + "/cs", "V2", false);
        await ImportAsync(Expanded(root + "/A", root + "/cs", "V1", "ABC"));
        await ImportAsync(Expanded(root + "/B", root + "/cs", "V2", "abc"));

        await ImportAsync(Composed(root + "/result", ReferenceOperation(root, operation)));

        string[] expected = operation switch { "intersection" => [], "exclude" => ["ABC"], _ => ["ABC", "abc"] };
        var expansion = await AssertExpansionAsync(root + "/result", expected);
        expansion.Contains.Select(row => row.Version).Order().ShouldBe(
            operation == "intersection" ? [] : operation == "exclude" ? ["V1"] : ["V1", "V2"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenResidualInsensitivePinnedReferenceAgainstUnknownVersion_WhenCombined_ThenComparisonDoesNotInventCertainty(bool exclude)
    {
        var root = Root();
        await ImportCasePolicyAsync(root + "/cs", "V1", false);
        await ImportCasePolicyAsync(root + "/cs", "V2", true);
        await ImportAsync(Expanded(root + "/A", root + "/cs", null, "ABC"));
        await ImportAsync(Expanded(root + "/B", root + "/cs", "V1", "abc"));

        await ImportAsync(Composed(root + "/result", ReferenceOperation(root, exclude ? "exclude" : "intersection")));

        var expansion = await AssertExpansionAsync(root + "/result", exclude ? ["ABC"] : [], partial: true);
        expansion.Contains.ShouldAllBe(row => row.Version == null);
    }

    [Theory]
    [InlineData("intersection")]
    [InlineData("exclude")]
    [InlineData("union")]
    public async Task GivenResidualUnknownVersionWithConflictingCasePolicies_WhenCombiningReferences_ThenAmbiguityIsVisible(string operation)
    {
        var root = Root();
        await ImportCasePolicyAsync(root + "/cs", "V1", false);
        await ImportCasePolicyAsync(root + "/cs", "V2", true);
        await ImportAsync(Expanded(root + "/A", root + "/cs", null, "ABC"));
        await ImportAsync(Expanded(root + "/B", root + "/cs", null, "abc"));

        await ImportAsync(Composed(root + "/result", ReferenceOperation(root, operation)));

        string[] expected = operation switch { "intersection" => [], "exclude" => ["ABC"], _ => ["ABC", "abc"] };
        await AssertExpansionAsync(root + "/result", expected, partial: true);
    }

    private Task ImportCasePolicyAsync(string system, string version, bool caseSensitive) => ImportAsync(new JsonObject
    {
        ["resourceType"] = "CodeSystem", ["url"] = system, ["version"] = version,
        ["content"] = "complete", ["caseSensitive"] = caseSensitive,
        ["concept"] = Concepts(system, caseSensitive ? ["ABC", "abc"] : ["ABC"])["concept"]!.DeepClone(),
    });

    private static JsonObject ReferenceOperation(string root, string operation) => operation switch
    {
        "intersection" => new() { ["include"] = new JsonArray(References(root + "/A", root + "/B")) },
        "exclude" => new()
        {
            ["include"] = new JsonArray(References(root + "/A")),
            ["exclude"] = new JsonArray(References(root + "/B")),
        },
        "union" => new() { ["include"] = new JsonArray(References(root + "/A"), References(root + "/B")) },
        _ => throw new InvalidOperationException(),
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenConceptAndReferenceInOneClause_WhenImported_ThenTheirIntersectionIsApplied(bool exclude)
    {
        var root = Root();
        await ImportAsync(Expanded(root + "/source", root + "/cs", null, "a", "b"));
        var clause = Concepts(root + "/cs", "b");
        clause["valueSet"] = new JsonArray(root + "/source");

        await ImportAsync(Composed(root + "/result", Compose(clause, exclude, root + "/cs")));

        await AssertExpansionAsync(root + "/result", exclude ? ["a", "c"] : ["b"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenMultipleReferencesInOneClause_WhenImported_ThenOnlyCommonCodesAreSelected(bool exclude)
    {
        var root = Root();
        await ImportAsync(Expanded(root + "/A", root + "/cs", null, "a", "b"));
        await ImportAsync(Expanded(root + "/B", root + "/cs", null, "b", "c"));
        var clause = References(root + "/A", root + "/B");

        await ImportAsync(Composed(root + "/result", Compose(clause, exclude, root + "/cs")));

        await AssertExpansionAsync(root + "/result", exclude ? ["a", "c"] : ["b"]);
    }

    [Fact]
    public async Task GivenReferencesInDifferentIncludeClauses_WhenImported_ThenTheirUnionIsPreserved()
    {
        var root = Root();
        await ImportAsync(Expanded(root + "/A", root + "/cs", null, "a", "b"));
        await ImportAsync(Expanded(root + "/B", root + "/cs", null, "b", "c"));

        await ImportAsync(Composed(root + "/result", new JsonObject
        {
            ["include"] = new JsonArray(References(root + "/A"), References(root + "/B")),
        }));

        await AssertExpansionAsync(root + "/result", ["a", "b", "c"]);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(false, "V1")]
    [InlineData(false, "*")]
    [InlineData(true, null)]
    [InlineData(true, "V1")]
    [InlineData(true, "*")]
    public async Task GivenSystemAndVersionWithReference_WhenImported_ThenReferencedRowsAreRestricted(
        bool exclude, string? version)
    {
        var root = Root();
        var source = Expanded(root + "/source", root + "/cs", "V1", "a");
        var contains = source["expansion"]!["contains"]!.AsArray();
        contains.Add(new JsonObject { ["system"] = root + "/cs", ["version"] = "V2", ["code"] = "b" });
        contains.Add(new JsonObject { ["system"] = root + "/other", ["version"] = "V1", ["code"] = "c" });
        await ImportAsync(source);
        var clause = References(root + "/source");
        clause["system"] = root + "/cs";
        if (version is not null)
        {
            clause["version"] = version;
        }
        var compose = new JsonObject { ["include"] = new JsonArray(exclude ? References(root + "/source") : clause) };
        if (exclude)
        {
            compose["exclude"] = new JsonArray(clause);
        }

        await ImportAsync(Composed(root + "/result", compose));

        var allVersions = version is null or "*";
        var expansion = await AssertExpansionAsync(root + "/result",
            exclude ? allVersions ? ["c"] : ["b", "c"] : allVersions ? ["a", "b"] : ["a"]);
        if (!exclude)
        {
            expansion.Contains.ShouldAllBe(row => row.System == root + "/cs");
            if (!allVersions)
            {
                expansion.Contains.ShouldAllBe(row => row.Version == version);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenSameCodeInDifferentVersions_WhenOneVersionIsExcluded_ThenOtherVersionsSurvive(bool unknownVersion)
    {
        var root = Root();
        var source = Expanded(root + "/source", root + "/cs", "V1", "a");
        source["expansion"]!["contains"]!.AsArray().Add(new JsonObject
        {
            ["system"] = root + "/cs", ["version"] = "V2", ["code"] = "a",
        });
        if (unknownVersion)
        {
            source["expansion"]!["contains"]!.AsArray().Add(new JsonObject { ["system"] = root + "/cs", ["code"] = "a" });
        }
        await ImportAsync(source);

        await ImportAsync(Composed(root + "/result", new JsonObject
        {
            ["include"] = new JsonArray(References(root + "/source")),
            ["exclude"] = new JsonArray(new JsonObject { ["system"] = root + "/cs", ["version"] = "V1" }),
        }));

        var expansion = await AssertExpansionAsync(root + "/result", unknownVersion ? ["a", "a"] : ["a"], unknownVersion);
        expansion.Contains.ShouldAllBe(row => row.Version != "V1");
        expansion.Contains.Count(row => row.Version == "V2").ShouldBe(1);
    }

    [Fact]
    public async Task GivenReferencedCodesWithUnknownVersion_WhenASystemVersionIsRequired_ThenTheResultIsNotFalselyComplete()
    {
        var root = Root();
        await ImportAsync(Expanded(root + "/source", root + "/cs", null, "a"));
        var clause = References(root + "/source");
        clause["system"] = root + "/cs";
        clause["version"] = "V1";

        await ImportAsync(Composed(root + "/result", new JsonObject { ["include"] = new JsonArray(clause) }));

        await AssertExpansionAsync(root + "/result", [], partial: true);
        (await Database.ExecuteScalarAsync<string>(
            $"SELECT PartialExpansionReason FROM dbo.TermValueSet WHERE Canonical = '{root}/result'"))
            .ShouldContain(root + "/cs|V1");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenFilterAndReferenceInOneClause_WhenImported_ThenBothConditionsApply(bool exclude)
    {
        var root = Root();
        var codeSystem = new JsonObject
        {
            ["resourceType"] = "CodeSystem", ["url"] = root + "/cs", ["content"] = "complete",
            ["concept"] = Concepts(root + "/cs", "a", "b", "c")["concept"]!.DeepClone(),
        };
        await ImportAsync(codeSystem);
        await ImportAsync(Expanded(root + "/source", root + "/cs", null, "a", "b"));
        var clause = References(root + "/source");
        clause["system"] = root + "/cs";
        clause["filter"] = Filters();

        await ImportAsync(Composed(root + "/result", Compose(clause, exclude, root + "/cs")));

        await AssertExpansionAsync(root + "/result", exclude ? ["a", "c"] : ["b"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenConceptAndFilterTogether_WhenImported_ThenInvalidClauseFailsExplicitly(bool exclude)
    {
        var root = Root();
        var clause = Concepts(root + "/cs", "b");
        clause["filter"] = Filters();
        var json = Composed(root + "/result", Compose(clause, exclude, root + "/cs"));
        var package = await Database.SeedPackageResourceAsync("ValueSet", root + "/result", json.ToJsonString());

        var result = await Database.CreateSqlServerImporter().ImportValueSetAsync(1, package, CancellationToken.None);

        result.Success.ShouldBeFalse();
        result.ErrorMessage.ShouldNotBeNull().ShouldContain("concept and filter");
        (await Database.ExecuteScalarAsync<string>(
            $"SELECT TerminologyImportStatus FROM dbo.PackageResource WHERE PackageResourceId = {package.PackageResourceId}"))
            .ShouldBe("Failed");
        (await Database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.TermValueSet WHERE PackageResourceId = {package.PackageResourceId}")).ShouldBe(0);
    }

    [Theory]
    [InlineData("V1", "a", false)]
    [InlineData("V2", "b", false)]
    [InlineData(null, "b", false)]
    [InlineData("v1", null, false)]
    [InlineData("V1", null, true)]
    public async Task GivenVersionedReferences_WhenImported_ThenOnlyExactCanonicalAndBusinessVersionResolve(
        string? version, string? expectedCode, bool wrongUrlCase)
    {
        var root = Root();
        var first = Expanded(root + "/A", root + "/cs", null, "a");
        first["version"] = "V1";
        await ImportAsync(first);
        var second = Expanded(root + "/A", root + "/cs", null, "b");
        second["version"] = "V2";
        await ImportAsync(second);
        var reference = root + (wrongUrlCase ? "/a" : "/A") + (version is null ? "" : "|" + version);

        await ImportAsync(Composed(root + "/result", new JsonObject { ["include"] = new JsonArray(References(reference)) }));

        await AssertExpansionAsync(root + "/result", expectedCode is null ? [] : [expectedCode], expectedCode is null);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public async Task GivenReferencedExpansionChain_WhenImported_ThenPartialStateAndReasonSurviveIncludesAndExcludes(
        bool exclude, bool partial, bool empty)
    {
        var root = Root();
        var includes = new JsonArray();
        if (!empty)
        {
            includes.Add(Concepts(root + "/cs", "a"));
        }
        if (partial)
        {
            includes.Add(new JsonObject { ["system"] = root + "/external" });
        }
        await ImportAsync(Composed(root + "/A", new JsonObject { ["include"] = includes }));
        var bCompose = new JsonObject
        {
            ["include"] = new JsonArray(exclude ? Concepts(root + "/cs", "a", "b") : References(root + "/A")),
        };
        if (exclude)
        {
            bCompose["exclude"] = new JsonArray(References(root + "/A"));
        }
        await ImportAsync(Composed(root + "/B", bCompose));
        await ImportAsync(Composed(root + "/C", new JsonObject { ["include"] = new JsonArray(References(root + "/B")) }));

        string[] codes = exclude ? empty ? ["a", "b"] : ["b"] : empty ? [] : ["a"];
        foreach (var canonical in new[] { root + "/B", root + "/C" })
        {
            await AssertExpansionAsync(canonical, codes, partial);
            var reason = await Database.ExecuteScalarAsync<string>(
                $"SELECT COALESCE(PartialExpansionReason, '') FROM dbo.TermValueSet WHERE Canonical = '{canonical}'");
            if (partial)
            {
                reason.ShouldContain(root + "/A");
                reason.ShouldContain(root + "/external");
            }
            else
            {
                reason.ShouldBeEmpty();
            }
        }
    }

    private async Task ImportAsync(JsonObject resource)
    {
        var type = resource["resourceType"]!.GetValue<string>();
        var package = await Database.SeedPackageResourceAsync(type, resource["url"]!.GetValue<string>(), resource.ToJsonString());
        var importer = Database.CreateSqlServerImporter();
        var result = type == "CodeSystem"
            ? await importer.ImportCodeSystemAsync(1, package, CancellationToken.None)
            : await importer.ImportValueSetAsync(1, package, CancellationToken.None);
        result.Success.ShouldBeTrue(result.ErrorMessage);
        (await Database.ExecuteScalarAsync<string>(
            $"SELECT ContentHash FROM dbo.PackageResource WHERE PackageResourceId = {package.PackageResourceId}"))
            .ShouldBe(package.ComputeContentHash());
    }

    private async Task<ExpandResult> AssertExpansionAsync(string canonical, string[] codes, bool partial = false)
    {
        var result = await Database.CreateTerminologyService().ExpandValueSetAsync(
            new ExpansionParameters(canonical), CancellationToken.None);
        result.ShouldNotBeNull();
        result.Contains.Select(row => row.Code).Order(StringComparer.Ordinal).ShouldBe(codes);
        result.Total.ShouldBe(codes.Length);
        result.Incomplete.ShouldBe(partial);
        return result;
    }

    private static string Root() => $"https://composition.example/{Guid.NewGuid():N}";

    private static JsonObject Composed(string canonical, JsonObject compose) => new()
    {
        ["resourceType"] = "ValueSet", ["url"] = canonical, ["status"] = "active", ["compose"] = compose,
    };

    private static JsonObject Expanded(string canonical, string system, string? version, params string[] codes) => new()
    {
        ["resourceType"] = "ValueSet", ["url"] = canonical, ["status"] = "active",
        ["expansion"] = new JsonObject
        {
            ["contains"] = new JsonArray(codes.Select(code => (JsonNode)new JsonObject
            {
                ["system"] = system, ["version"] = version, ["code"] = code,
            }).ToArray()),
        },
    };

    private static JsonObject Concepts(string system, params string[] codes) => new()
    {
        ["system"] = system,
        ["concept"] = new JsonArray(codes.Select(code => (JsonNode)new JsonObject { ["code"] = code }).ToArray()),
    };

    private static JsonObject References(params string[] canonicals) => new()
    {
        ["valueSet"] = new JsonArray(canonicals.Select(canonical => (JsonNode)JsonValue.Create(canonical)!).ToArray()),
    };

    private static JsonArray Filters() => new(new JsonObject { ["property"] = "code", ["op"] = "in", ["value"] = "b,c" });

    private static JsonObject Compose(JsonObject clause, bool exclude, string system)
    {
        var compose = new JsonObject { ["include"] = new JsonArray(exclude ? Concepts(system, "a", "b", "c") : clause) };
        if (exclude)
        {
            compose["exclude"] = new JsonArray(clause);
        }
        return compose;
    }
}

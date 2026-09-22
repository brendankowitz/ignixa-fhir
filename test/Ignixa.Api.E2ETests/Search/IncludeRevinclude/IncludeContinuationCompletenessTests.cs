using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.E2ETests._Infrastructure.Base;
using Ignixa.Models;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Shouldly;

namespace Ignixa.Api.E2ETests.Search.IncludeRevinclude;

public class IncludeContinuationCompletenessTests(IgnixaApiFixture fixture) : IncludeTestBase(fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenTwentyFiveRevincludes_WhenEveryRelatedLinkIsFollowed_ThenEveryIncludeAppearsExactlyOnce(bool sorted)
    {
        var tag = Guid.NewGuid().ToString("N");
        await Harness.UpdateResourceAsync(ResourceJsonNode.Parse($$"""
            {"resourceType":"Patient","id":"fanout-patient","name":[{"family":"Fanout"}],
             "meta":{"tag":[{"system":"{{TestTagSystem}}","code":"{{tag}}"}]} }
            """));
        for (int i = 1; i <= 25; i++)
        {
            await Harness.UpdateResourceAsync(ResourceJsonNode.Parse($$"""
                {"resourceType":"Observation","id":"fanout-observation-{{i:D2}}","status":"final",
                 "code":{"text":"fanout"},"subject":{"reference":"Patient/fanout-patient"} }
                """));
        }

        string query = $"_tag={tag}&_count=1&_revinclude=Observation:subject&_includesCount=5" +
            (sorted ? "&_sort=family" : "");
        var bundle = await Harness.SearchBundleAsync("Patient", query);
        var ids = new List<string>();
        var links = new HashSet<string>();
        while (true)
        {
            ids.AddRange(IncludeIds(bundle));
            var related = bundle.Link.FirstOrDefault(l => l.GetRelationRaw() == "related")?.Url;
            if (related is null)
            {
                break;
            }
            links.Add(related).ShouldBeTrue("continuation must advance");
            links.Count.ShouldBeLessThanOrEqualTo(5);
            bundle = await Harness.GetBundleAsync(related);
        }

        ids.Order().ShouldBe(
        [
            "fanout-observation-01", "fanout-observation-02", "fanout-observation-03", "fanout-observation-04",
            "fanout-observation-05", "fanout-observation-06", "fanout-observation-07", "fanout-observation-08",
            "fanout-observation-09", "fanout-observation-10", "fanout-observation-11", "fanout-observation-12",
            "fanout-observation-13", "fanout-observation-14", "fanout-observation-15", "fanout-observation-16",
            "fanout-observation-17", "fanout-observation-18", "fanout-observation-19", "fanout-observation-20",
            "fanout-observation-21", "fanout-observation-22", "fanout-observation-23", "fanout-observation-24",
            "fanout-observation-25",
        ]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenTwoMatchPagesWithIteratedIncludes_WhenRelatedLinksAreFollowed_ThenOnlyCurrentPageSeedsIncludes(bool sorted)
    {
        var tag = Guid.NewGuid().ToString("N");
        foreach (string suffix in new[] { "a", "b" })
        {
            await Harness.UpdateResourceAsync(ResourceJsonNode.Parse($$"""
                {"resourceType":"Organization","id":"include-page-root-{{suffix}}"}
                """));
            await Harness.UpdateResourceAsync(ResourceJsonNode.Parse($$"""
                {"resourceType":"Organization","id":"include-page-child-{{suffix}}",
                 "partOf":{"reference":"Organization/include-page-root-{{suffix}}"} }
                """));
            await Harness.UpdateResourceAsync(ResourceJsonNode.Parse($$"""
                {"resourceType":"Patient","id":"include-page-patient-{{suffix}}","name":[{"family":"{{suffix}}"}],
                 "meta":{"tag":[{"system":"{{TestTagSystem}}","code":"{{tag}}"}]},
                 "managingOrganization":{"reference":"Organization/include-page-child-{{suffix}}"} }
                """));
        }

        var first = await Harness.SearchBundleAsync("Patient",
            $"_tag={tag}&_count=1&_includesCount=1&_include=Patient:organization&_include:iterate=Organization:partof" +
            (sorted ? "&_sort=family" : ""));
        var next = first.Link.Single(l => l.GetRelationRaw() == "next").Url!;
        var firstIds = await FollowRelatedAsync(first);
        firstIds.Order().ShouldBe(["include-page-child-a", "include-page-root-a"]);
        var secondIds = await FollowRelatedAsync(await Harness.GetBundleAsync(next));
        secondIds.Order().ShouldBe(["include-page-child-b", "include-page-root-b"]);
    }

    private async Task<List<string>> FollowRelatedAsync(Bundle bundle)
    {
        var ids = new List<string>();
        for (int page = 0; page < 6; page++)
        {
            ids.AddRange(IncludeIds(bundle));
            var related = bundle.Link.FirstOrDefault(l => l.GetRelationRaw() == "related")?.Url;
            if (related is null)
            {
                return ids;
            }
            bundle = await Harness.GetBundleAsync(related);
        }
        throw new InvalidOperationException("Includes continuation did not terminate.");
    }

    private static IEnumerable<string> IncludeIds(Bundle bundle) => bundle.Entry
        .Where(e => e.Search?.Mode?.GetLiteral() == "include")
        .Select(e => e.Resource!.Id!);
}

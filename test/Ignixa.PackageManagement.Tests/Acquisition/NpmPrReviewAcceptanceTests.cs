using System.Net;
using System.Text;
using Ignixa.PackageManagement.Models;
using Shouldly;

#pragma warning disable CA2025 // Responses transfer to the awaited acquirer.

namespace Ignixa.PackageManagement.Tests.Acquisition;

public class NpmPrReviewAcceptanceTests
{
    [Theory]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("true")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("\"sha512-not-base64\"")]
    [InlineData("\"\"")]
    public async Task GivenValidAdminPinAndPresentInvalidMetadataIntegrity_WhenAcquired_ThenNeverFallsBackToPin(string value)
    {
        using var registry = new SyntheticNpmRegistry();
        byte[] metadata = Encoding.UTF8.GetBytes(
            $$$"""{"name":"test.pkg","version":"1.2.3","dist":{"tarball":"{{{SyntheticNpmRegistry.Artifact}}}","integrity":{{{value}}}}}""");
        registry.Respond = (_, _, _) => Task.FromResult(registry.Bytes(metadata));
        using var acquirer = registry.Acquirer();
        var error = await Should.ThrowAsync<PackageAcquisitionException>(() =>
            acquirer.AcquireAsync(SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(), CancellationToken.None, new(registry.Integrity)));
        error.Error.ShouldBe(PackageAcquisitionError.InvalidIntegrity);
        registry.Requests.Count.ShouldBe(1);
        registry.Bodies.ShouldAllBe(body => body.Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenEscapeEquivalentDuplicateRegistryProperties_WhenRootOrNestedDist_ThenRejectsPermanently(bool nested)
    {
        using var registry = new SyntheticNpmRegistry();
        string json = Encoding.UTF8.GetString(registry.Metadata());
        json = nested
            ? json.Replace("\"dist\":{", "\"dist\":{\"\\u0074arball\":\"" + SyntheticNpmRegistry.Artifact + "\",", StringComparison.Ordinal)
            : json.Replace("\"name\":", "\"\\u006eame\":\"test.pkg\",\"name\":", StringComparison.Ordinal);
        registry.Respond = (_, _, _) => Task.FromResult(registry.Bytes(Encoding.UTF8.GetBytes(json)));
        using var acquirer = registry.Acquirer();
        (await Should.ThrowAsync<PackageAcquisitionException>(() =>
            acquirer.AcquireAsync(SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(), CancellationToken.None)))
            .Error.ShouldBe(PackageAcquisitionError.InvalidMetadata);
        registry.Requests.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("identity", "identity", true)]
    [InlineData(" IDENTITY \t", "identity, identity", true)]
    [InlineData("identity", "", false)]
    [InlineData("", "identity", false)]
    [InlineData("identity", "gzip;bad", false)]
    [InlineData("identity", "gzip", false)]
    public async Task GivenMultipleRawEncodingFields_WhenMetadataAndTarball_ThenValidatesEveryListMember(string first, string second, bool valid)
    {
        foreach (bool artifact in new[] { false, true })
        {
            using var registry = new SyntheticNpmRegistry();
            registry.Respond = (request, _, _) =>
            {
                bool metadata = request.RequestUri!.Host == "registry.test";
                HttpResponseMessage response = registry.Bytes(metadata ? registry.Metadata() : registry.Tarball);
                if (artifact != metadata)
                {
                    response.Content.Headers.TryAddWithoutValidation("Content-Encoding", new[] { first, second });
                }
                return Task.FromResult(response);
            };
            using var acquirer = registry.Acquirer();
            Task<AcquiredNpmPackage> task = acquirer.AcquireAsync(SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(), CancellationToken.None);
            if (valid)
            {
                _ = await task;
            }
            else
            {
                (await Should.ThrowAsync<PackageAcquisitionException>(() => task)).Error.ShouldBe(PackageAcquisitionError.UnsupportedContentEncoding);
            }
            registry.Requests.Count.ShouldBe(valid || artifact ? 2 : 1);
        }
    }
}

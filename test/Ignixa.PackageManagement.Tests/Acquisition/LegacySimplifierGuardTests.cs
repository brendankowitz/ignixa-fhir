using System.Net;
using Ignixa.PackageManagement.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.PackageManagement.Tests.Acquisition;

public class LegacySimplifierGuardTests
{
    [Fact]
    public async Task GivenLegacySimplifierLoader_WhenDownloading_ThenKeepsDirectProtocolAndCallerOwnedClient()
    {
        using var transport = new SyntheticNpmRegistry();
        transport.Respond = (_, _, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent("legacy arbitrary content"u8.ToArray())
        });
        using var client = new HttpClient(transport, disposeHandler: false);
        client.DefaultRequestHeaders.Add("X-Caller-Owned", "unchanged");
        var loader = new NpmPackageLoader(client, NullLogger<NpmPackageLoader>.Instance);
        await using Stream first = await loader.DownloadPackageAsync("hl7.fhir.us.core", "6.1.0", CancellationToken.None);
        await using Stream second = await loader.DownloadPackageAsync("hl7.fhir.us.core", "6.1.0", CancellationToken.None);
        using var text = new StreamReader(first);
        (await text.ReadToEndAsync()).ShouldBe("legacy arbitrary content");
        transport.Requests.Select(r => r.Uri).ShouldBe([
            "https://packages.simplifier.net/hl7.fhir.us.core/6.1.0",
            "https://packages.simplifier.net/hl7.fhir.us.core/6.1.0"]);
        transport.Disposed.ShouldBeFalse();
        client.DefaultRequestHeaders.GetValues("X-Caller-Owned").Single().ShouldBe("unchanged");
    }
}

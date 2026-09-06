using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

internal sealed class LastNFakeHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Production";
    public string ApplicationName { get; set; } = "Ignixa.DataLayer.SqlServer.IntegrationTests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

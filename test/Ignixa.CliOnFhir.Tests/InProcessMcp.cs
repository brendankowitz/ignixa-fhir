// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.IO.Pipelines;
using System.Reflection;
using Ignixa.CliOnFhir.Mcp;
using Ignixa.CliOnFhir.Session;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>A server and a client connected over in-memory pipes, so tools are called and listed as an agent's client does.</summary>
internal sealed class InProcessMcp : IAsyncDisposable
{
    private readonly CancellationTokenSource _cancellation;
    private readonly StreamServerTransport _serverTransport;
    private readonly McpServer _server;

    private InProcessMcp(CancellationTokenSource cancellation, StreamServerTransport serverTransport, McpServer server, McpClient client)
    {
        _cancellation = cancellation;
        _serverTransport = serverTransport;
        _server = server;
        Client = client;
    }

    public McpClient Client { get; }

    public CancellationToken CancellationToken => _cancellation.Token;

    /// <summary>Serves the tools of <paramref name="toolTypes"/> under <see cref="McpToolset.All"/>; see <see cref="StartAsync(McpToolset, IEnumerable{Type}, object?[])"/>.</summary>
    public static Task<InProcessMcp> StartAsync(IEnumerable<Type> toolTypes, params object?[] constructorArguments) =>
        StartAsync(McpToolset.All, toolTypes, constructorArguments);

    /// <summary>
    /// Serves what <paramref name="toolset"/> offers of the tools and resources of <paramref name="toolTypes"/>, built as the
    /// server builds them; each type is constructed with <paramref name="constructorArguments"/> in order, and the toolset for
    /// a <see cref="McpToolset"/> parameter.
    /// </summary>
    public static async Task<InProcessMcp> StartAsync(McpToolset toolset, IEnumerable<Type> toolTypes, params object?[] constructorArguments)
    {
        List<Type> types = [.. toolTypes];
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();
        var options = new McpServerOptions
        {
            ToolCollection = [.. McpServerSetup.Tools(toolset, (type, _) => Construct(type, toolset, constructorArguments), _ => constructorArguments.OfType<FolderSession>().FirstOrDefault()).Where(tool => types.Contains(DeclaringType(tool)))],
        };
        List<McpServerResource> resources = [.. types.Where(McpServerSetup.ResourceTypes.Contains).SelectMany(type => Resources(type, toolset, constructorArguments))];
        if (resources.Count > 0)
        {
            options.ResourceCollection = [.. resources];
        }

        McpServerSetup.Configure(options, toolset);
        return await StartAsync(options, clientToServer, serverToClient);
    }

    /// <summary>Serves exactly <paramref name="tools"/>, as built (for a test of a tool wrapper).</summary>
    public static Task<InProcessMcp> StartAsync(IReadOnlyList<McpServerTool> tools) =>
        StartAsync(new McpServerOptions { ToolCollection = [.. tools] }, new Pipe(), new Pipe());

    private static async Task<InProcessMcp> StartAsync(McpServerOptions options, Pipe clientToServer, Pipe serverToClient)
    {
        var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var serverTransport = new StreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream());
        McpServer server = McpServer.Create(serverTransport, options);
        _ = server.RunAsync(cancellation.Token);
        McpClient client = await McpClient.CreateAsync(
            new StreamClientTransport(clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream()),
            cancellationToken: cancellation.Token);
        return new InProcessMcp(cancellation, serverTransport, server, client);
    }

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        await _server.DisposeAsync();
        await _serverTransport.DisposeAsync();
        _cancellation.Dispose();
    }

    private static Type DeclaringType(McpServerTool tool) =>
        tool.Metadata.OfType<MethodInfo>().First().DeclaringType!;

    private static IEnumerable<McpServerResource> Resources(Type type, McpToolset toolset, object?[] constructorArguments)
    {
        object target = Construct(type, toolset, constructorArguments);
        return type.GetMethods()
            .Where(method => method.GetCustomAttribute<McpServerResourceAttribute>() is not null)
            .Select(method => McpServerResource.Create(method, target));
    }

    private static object Construct(Type type, McpToolset toolset, object?[] constructorArguments)
    {
        ConstructorInfo constructor = type.GetConstructors().Single();
        int next = 0;
        object?[] arguments = [.. constructor.GetParameters().Select(parameter => parameter.ParameterType == typeof(McpToolset) ? toolset : constructorArguments[next++])];
        return constructor.Invoke(arguments);
    }
}

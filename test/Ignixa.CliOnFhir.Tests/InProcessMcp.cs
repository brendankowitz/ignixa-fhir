// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.IO.Pipelines;
using System.Reflection;
using Ignixa.CliOnFhir.Mcp;
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

    /// <summary>Serves the tools of <paramref name="toolTypes"/>, each constructed with <paramref name="constructorArguments"/> (a type with no constructor parameters takes none).</summary>
    public static async Task<InProcessMcp> StartAsync(IEnumerable<Type> toolTypes, params object?[] constructorArguments)
    {
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();
        var options = new McpServerOptions { ToolCollection = [.. toolTypes.SelectMany(type => Tools(type, constructorArguments))] };
        McpServerSetup.Configure(options);
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

    private static IEnumerable<McpServerTool> Tools(Type type, object?[] constructorArguments)
    {
        ConstructorInfo constructor = type.GetConstructors().Single();
        object target = constructor.Invoke(constructor.GetParameters().Length == 0 ? [] : constructorArguments);
        return type.GetMethods()
            .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .Select(method => method.IsStatic ? McpServerTool.Create(method) : McpServerTool.Create(method, target));
    }
}

// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Shouldly;
using Xunit;

namespace Ignixa.RepoGuards.Tests;

/// <summary>
/// Every MCP tool method that takes a resource type from the caller must validate it through
/// <c>TenantAwareMcpTool.RequireKnownResourceTypeAsync</c> (#536).
/// </summary>
/// <remarks>
/// The check is opt-in per tool, so a new tool could silently skip it and feed an unvalidated value into a
/// query, a capability check or a storage key. This scan parses <c>src/</c> for classes derived from
/// <c>TenantAwareMcpTool</c> and requires each <c>[McpServerTool]</c> method with a <c>resourceType</c> or
/// <c>resourceTypes</c> parameter to call the helper. It checks that the call is made, not that its result is
/// used; that is what the tools' own tests are for.
/// </remarks>
public class McpResourceTypeValidationGuardTests
{
    private const string Helper = "RequireKnownResourceTypeAsync";

    /// <summary>
    /// Tool methods that take resource types without validating them yet, with the reason. Keyed by
    /// <c>Class.Method</c>; an entry that no longer matches a method fails the scan.
    /// </summary>
    private static readonly Dictionary<string, string> Allowlist = new(StringComparer.Ordinal)
    {
        ["StartExportJobTool.StartExportJobAsync"] =
            "resourceTypes[] feeds the export job's own type resolution, not capability expressions or storage "
            + "keys; validating it is tracked separately.",
    };

    [Fact]
    public void GivenMcpToolMethodsTakingAResourceType_WhenScanned_ThenEachValidatesItOrIsAllowlisted()
    {
        var root = Path.Combine(RepoRoot.Find(), "src");
        var methods = Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(file => FindToolMethods(File.ReadAllText(file)))
            .ToList();

        methods.Count.ShouldBeGreaterThanOrEqualTo(5,
            "Expected the get/history/search/patch tools at least; the scan is probably not finding the tools.");

        var offenders = methods
            .Where(method => !method.Validates && !Allowlist.ContainsKey(method.Name))
            .Select(method => method.Name)
            .ToList();
        var stale = Allowlist.Keys.Where(key => methods.All(method => method.Name != key)).ToList();

        offenders.ShouldBeEmpty(
            $"These MCP tool methods take a resource type but never call {Helper}. Call it before building "
            + "any query or command from the value, or add the method to the allowlist with a reason.");
        stale.ShouldBeEmpty("These allowlist entries no longer match a tool method; remove them.");
    }

    [Fact]
    public void GivenAToolThatSkipsTheHelper_WhenScanned_ThenItIsReported()
    {
        var methods = FindToolMethods("""
            class T : TenantAwareMcpTool
            {
                [McpServerTool(Name = "x")]
                public Task A(string resourceType) => Task.CompletedTask;

                [McpServerTool(Name = "y")]
                public async Task B(string resourceType) { await RequireKnownResourceTypeAsync(resourceType, 1, default); }

                [McpServerTool(Name = "z")]
                public Task C(string id) => Task.CompletedTask;

                public Task D(string resourceType) => Task.CompletedTask;
            }

            class Other { [McpServerTool(Name = "q")] public Task E(string resourceType) => Task.CompletedTask; }
            """);

        methods.Select(method => (method.Name, method.Validates))
            .ShouldBe([("T.A", false), ("T.B", true)], ignoreOrder: true);
    }

    private static IEnumerable<(string Name, bool Validates)> FindToolMethods(string source) =>
        CSharpSyntaxTree.ParseText(source).GetRoot()
            .DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .Where(type => type.BaseList?.Types.Any(b => b.Type.ToString().EndsWith("TenantAwareMcpTool", StringComparison.Ordinal)) == true)
            .SelectMany(type => type.Members.OfType<MethodDeclarationSyntax>().Select(method => (Type: type, Method: method)))
            .Where(entry => HasAttribute(entry.Method, "McpServerTool") && TakesResourceType(entry.Method))
            .Select(entry => (
                Name: $"{entry.Type.Identifier.ValueText}.{entry.Method.Identifier.ValueText}",
                Validates: entry.Method.DescendantNodes().OfType<InvocationExpressionSyntax>()
                    .Any(call => call.Expression.ToString() == Helper)));

    private static bool HasAttribute(MethodDeclarationSyntax method, string name) =>
        method.AttributeLists.SelectMany(list => list.Attributes)
            .Any(attribute => attribute.Name.ToString() is var text && (text == name || text == name + "Attribute"));

    private static bool TakesResourceType(MethodDeclarationSyntax method) =>
        method.ParameterList.Parameters.Any(parameter => parameter.Identifier.ValueText is "resourceType" or "resourceTypes");
}

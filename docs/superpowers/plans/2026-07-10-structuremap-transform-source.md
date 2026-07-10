# StructureMap Transform Source Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `POST /StructureMap/{id}/$transform-source`, accepting raw HL7v2 ER7 in a FHIR `Parameters` resource, executing a tenant-stored FML `StructureMap`, and returning the mapped FHIR resource or Bundle without persisting it.

**Architecture:** Keep source-format parsing behind an `ISourceDataProvider` registry and keep FML execution source-agnostic by accepting `IElement`. Resolve the requested `StructureMap`, imported maps, and external `ConceptMap` resources from the current tenant before falling back to installed package resources. The operation composes existing Ignixa FML, FHIRPath, serialization, and validation infrastructure with the NHapi-backed `Ignixa.Hl7v2` projection layer.

**Tech Stack:** .NET 10 server, .NET 9/10 `Ignixa.Hl7v2`, ASP.NET Core Minimal APIs, Medino, Autofac, NHapi 3.2.4, Ignixa FHIR Mapping Language, Ignixa FHIRPath, xUnit, Shouldly, NSubstitute.

---

## Scope and prerequisite

This plan is the server-runtime workstream. Implement the projection contracts and `ADT_A01` projection from:

`docs/superpowers/plans/2026-07-09-hl7v2-projection-architecture.md`

before Task 2. The prerequisite must provide:

- `IHl7v2Parser`
- `Hl7v2ParseResult`
- `Hl7v2ProjectionContext`
- `Hl7v2ProjectionResult`
- `IHl7v2Projection`
- `Hl7v2ProjectionRegistry`
- `AdtA01Projection`

The first server slice supports `sourceFormat = hl7v2-er7` and the message structures registered in `Hl7v2ProjectionRegistry`. Adding another source format requires another `ISourceDataProvider`; it does not change the endpoint or mapping engine.

The operation only transforms. It does not:

- listen on MLLP;
- acknowledge an HL7v2 sender;
- persist its input;
- persist its FHIR output;
- choose a map from `MSH-9`;
- accept inline FML or inline `StructureMap` resources.

The caller selects the map by invoking `StructureMap/{id}/$transform-source`.

---

## File structure

```text
src\Core\Ignixa.FhirMappingLanguage\
  Evaluation\
    MappingEvaluator.cs

src\Application\Ignixa.Domain\
  Abstractions\
    ICustomOperationFeature.cs

src\Application\Ignixa.Application\
  Ignixa.Application.csproj
  Features\Experimental\Configuration\
    ExperimentalOptions.cs
  Features\Experimental\Infrastructure\
    ExperimentalAutofacRegistration.cs
  Features\Experimental\Transform\
    IMappingExecutionService.cs
    MappingExecutionService.cs
    StructureMapTransformSourceFeature.cs
    Source\
      ISourceDataProvider.cs
      SourceDataException.cs
      SourceDataProviderRegistry.cs
      SourceDataFormatNotSupportedException.cs
      Hl7v2Er7SourceDataProvider.cs
    Resolution\
      CanonicalResource.cs
      ICanonicalResourceResolver.cs
      FhirServerCanonicalResourceResolver.cs
      ResolvedStructureMap.cs
      IStoredStructureMapResolver.cs
      StoredStructureMapResolver.cs
      StoredConceptMapResolver.cs
    TransformSource\
      TransformSourceCommand.cs
      TransformSourceHandler.cs

src\Application\Ignixa.Api\
  Endpoints\Experimental\
    TransformEndpoints.cs
    TransformSourceParametersParser.cs
    TransformSourceRequest.cs

test\Ignixa.FhirMappingLanguage.Tests\
  Evaluation\
    MappingEvaluatorImportTests.cs

test\Ignixa.Application.Tests\
  Features\Transform\Source\
    SourceDataProviderRegistryTests.cs
    Hl7v2Er7SourceDataProviderTests.cs
  Features\Transform\Resolution\
    FhirServerCanonicalResourceResolverTests.cs
    StoredStructureMapResolverTests.cs
    StoredConceptMapResolverTests.cs
  Features\Transform\
    MappingExecutionServiceTests.cs
    TransformSourceHandlerTests.cs

test\Ignixa.Api.Tests\
  Infrastructure\
    TransformSourceParametersParserTests.cs

test\Ignixa.Api.E2ETests\
  _Infrastructure\
    IgnixaApiFixture.cs
  Operations\Transform\
    TransformSourceTests.cs

docs\site\docs\server\features\
  structuremap-transform-source.md
```

---

### Task 1: Add the source-provider contract and registry

**Files:**
- Create: `src\Application\Ignixa.Application\Features\Experimental\Transform\Source\ISourceDataProvider.cs`
- Create: `src\Application\Ignixa.Application\Features\Experimental\Transform\Source\SourceDataException.cs`
- Create: `src\Application\Ignixa.Application\Features\Experimental\Transform\Source\SourceDataFormatNotSupportedException.cs`
- Create: `src\Application\Ignixa.Application\Features\Experimental\Transform\Source\SourceDataProviderRegistry.cs`
- Test: `test\Ignixa.Application.Tests\Features\Transform\Source\SourceDataProviderRegistryTests.cs`

- [ ] **Step 1: Write the failing registry tests**

Create `test\Ignixa.Application.Tests\Features\Transform\Source\SourceDataProviderRegistryTests.cs`:

```csharp
using Ignixa.Abstractions;
using Ignixa.Application.Features.Experimental.Transform.Source;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Transform.Source;

public class SourceDataProviderRegistryTests
{
    [Fact]
    public void GivenRegisteredFormat_WhenResolving_ThenReturnsMatchingProvider()
    {
        var provider = Substitute.For<ISourceDataProvider>();
        provider.Format.Returns("hl7v2-er7");
        var registry = new SourceDataProviderRegistry([provider]);

        var result = registry.Resolve("HL7V2-ER7");

        result.ShouldBeSameAs(provider);
    }

    [Fact]
    public void GivenUnknownFormat_WhenResolving_ThenThrowsExplicitException()
    {
        var registry = new SourceDataProviderRegistry([]);

        var exception = Should.Throw<SourceDataFormatNotSupportedException>(
            () => registry.Resolve("csv"));

        exception.SourceFormat.ShouldBe("csv");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run:

```powershell
dotnet test test\Ignixa.Application.Tests\Ignixa.Application.Tests.csproj --filter SourceDataProviderRegistryTests --verbosity minimal
```

Expected: build failure for missing source-provider types.

- [ ] **Step 3: Add the source-provider contract**

Create `src\Application\Ignixa.Application\Features\Experimental\Transform\Source\ISourceDataProvider.cs`:

```csharp
using Ignixa.Abstractions;

namespace Ignixa.Application.Features.Experimental.Transform.Source;

public interface ISourceDataProvider
{
    string Format { get; }

    ValueTask<IElement> ProjectAsync(
        string sourceData,
        CancellationToken cancellationToken);
}
```

Create `src\Application\Ignixa.Application\Features\Experimental\Transform\Source\SourceDataException.cs`:

```csharp
namespace Ignixa.Application.Features.Experimental.Transform.Source;

public class SourceDataException : Exception
{
    public SourceDataException(string message)
        : base(message)
    {
    }

    public SourceDataException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
```

Create `src\Application\Ignixa.Application\Features\Experimental\Transform\Source\SourceDataFormatNotSupportedException.cs`:

```csharp
namespace Ignixa.Application.Features.Experimental.Transform.Source;

public sealed class SourceDataFormatNotSupportedException : SourceDataException
{
    public SourceDataFormatNotSupportedException(string sourceFormat)
        : base($"Source format '{sourceFormat}' is not supported")
    {
        SourceFormat = sourceFormat;
    }

    public string SourceFormat { get; }
}
```

Create `src\Application\Ignixa.Application\Features\Experimental\Transform\Source\SourceDataProviderRegistry.cs`:

```csharp
namespace Ignixa.Application.Features.Experimental.Transform.Source;

public sealed class SourceDataProviderRegistry
{
    private readonly IReadOnlyDictionary<string, ISourceDataProvider> _providers;

    public SourceDataProviderRegistry(IEnumerable<ISourceDataProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        _providers = providers.ToDictionary(
            provider => provider.Format,
            StringComparer.OrdinalIgnoreCase);
    }

    public ISourceDataProvider Resolve(string sourceFormat)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFormat);

        return _providers.TryGetValue(sourceFormat, out var provider)
            ? provider
            : throw new SourceDataFormatNotSupportedException(sourceFormat);
    }
}
```

- [ ] **Step 4: Run the registry tests**

Run:

```powershell
dotnet test test\Ignixa.Application.Tests\Ignixa.Application.Tests.csproj --filter SourceDataProviderRegistryTests --verbosity minimal
```

Expected: PASS, 2 tests passed.

- [ ] **Step 5: Commit the source-provider contract**

```powershell
git add src\Application\Ignixa.Application\Features\Experimental\Transform\Source test\Ignixa.Application.Tests\Features\Transform\Source
git commit -m "Add source data provider registry" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 2: Integrate the NHapi-backed HL7v2 provider

**Files:**
- Modify: `src\Application\Ignixa.Application\Ignixa.Application.csproj`
- Create: `src\Application\Ignixa.Application\Features\Experimental\Transform\Source\Hl7v2Er7SourceDataProvider.cs`
- Test: `test\Ignixa.Application.Tests\Features\Transform\Source\Hl7v2Er7SourceDataProviderTests.cs`

- [ ] **Step 1: Add the failing HL7v2 provider tests**

Create `test\Ignixa.Application.Tests\Features\Transform\Source\Hl7v2Er7SourceDataProviderTests.cs`:

```csharp
using Ignixa.Application.Features.Experimental.Transform.Source;
using Ignixa.Hl7v2.Parsing;
using Ignixa.Hl7v2.Projection;
using Ignixa.Hl7v2.Validation;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Transform.Source;

public class Hl7v2Er7SourceDataProviderTests
{
    [Fact]
    public async Task GivenValidAdtA01_WhenProjecting_ThenReturnsLogicalRoot()
    {
        const string er7 = "MSH|^~\\&|||||||ADT^A01|1|P|2.5\rPID|1||12345";
        var parser = Substitute.For<IHl7v2Parser>();
        parser.Parse(er7).Returns(Hl7v2ParseResult.Success("ADT", "A01", "2.5", "1"));
        var projection = Substitute.For<IHl7v2Projection>();
        var root = new Ignixa.Hl7v2.LogicalModel.Hl7v2Element("msg", "Hl7v2AdtA01");
        projection.CanProject(Arg.Any<Hl7v2ProjectionContext>()).Returns(true);
        projection.Project(Arg.Any<Hl7v2ProjectionContext>())
            .Returns(Hl7v2ProjectionResult.Success(root));
        var provider = new Hl7v2Er7SourceDataProvider(
            parser,
            new Hl7v2ProjectionRegistry([projection]));

        var result = await provider.ProjectAsync(er7, CancellationToken.None);

        result.ShouldBeSameAs(root);
        provider.Format.ShouldBe("hl7v2-er7");
    }

    [Fact]
    public async Task GivenInvalidEr7_WhenProjecting_ThenThrowsDiagnosticMessage()
    {
        var parser = Substitute.For<IHl7v2Parser>();
        parser.Parse("invalid").Returns(Hl7v2ParseResult.Failure(
            new Hl7v2Diagnostic(
                Hl7v2DiagnosticSeverity.Error,
                "HL7_PARSE_ERROR",
                "Invalid HL7v2 message")));
        var provider = new Hl7v2Er7SourceDataProvider(
            parser,
            new Hl7v2ProjectionRegistry([]));

        var exception = await Should.ThrowAsync<SourceDataException>(
            () => provider.ProjectAsync("invalid", CancellationToken.None).AsTask());

        exception.Message.ShouldContain("HL7_PARSE_ERROR");
    }
}
```

- [ ] **Step 2: Add the project reference and verify the tests still fail**

Add to the first `ItemGroup` in `src\Application\Ignixa.Application\Ignixa.Application.csproj`:

```xml
<ProjectReference Include="..\..\Core\Ignixa.Hl7v2\Ignixa.Hl7v2.csproj" />
```

Run:

```powershell
dotnet test test\Ignixa.Application.Tests\Ignixa.Application.Tests.csproj --filter Hl7v2Er7SourceDataProviderTests --verbosity minimal
```

Expected: build failure for missing `Hl7v2Er7SourceDataProvider`.

- [ ] **Step 3: Implement the HL7v2 provider**

Create `src\Application\Ignixa.Application\Features\Experimental\Transform\Source\Hl7v2Er7SourceDataProvider.cs`:

```csharp
using Ignixa.Abstractions;
using Ignixa.Hl7v2.Parsing;
using Ignixa.Hl7v2.Projection;

namespace Ignixa.Application.Features.Experimental.Transform.Source;

public sealed class Hl7v2Er7SourceDataProvider(
    IHl7v2Parser parser,
    Hl7v2ProjectionRegistry projectionRegistry) : ISourceDataProvider
{
    public string Format => "hl7v2-er7";

    public ValueTask<IElement> ProjectAsync(
        string sourceData,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceData);

        var parseResult = parser.Parse(sourceData);
        if (!parseResult.IsSuccess)
        {
            throw new SourceDataException(FormatDiagnostics(parseResult.Diagnostics));
        }

        var context = new Hl7v2ProjectionContext(
            parseResult.MessageCode,
            parseResult.TriggerEvent,
            parseResult.Version,
            parseResult.MessageControlId,
            sourceData);

        Hl7v2ProjectionResult projectionResult;
        try
        {
            projectionResult = projectionRegistry.Resolve(context).Project(context);
        }
        catch (InvalidOperationException exception)
        {
            throw new SourceDataException(exception.Message, exception);
        }

        if (!projectionResult.IsSuccess || projectionResult.Root is null)
        {
            throw new SourceDataException(FormatDiagnostics(projectionResult.Diagnostics));
        }

        return ValueTask.FromResult(projectionResult.Root);
    }

    private static string FormatDiagnostics(
        IEnumerable<Ignixa.Hl7v2.Validation.Hl7v2Diagnostic> diagnostics) =>
        string.Join(
            "; ",
            diagnostics.Select(diagnostic =>
                $"{diagnostic.Code}: {diagnostic.Message}"));
}
```

- [ ] **Step 4: Run the provider and HL7v2 tests**

Run:

```powershell
dotnet test test\Ignixa.Application.Tests\Ignixa.Application.Tests.csproj --filter Hl7v2Er7SourceDataProviderTests --verbosity minimal
dotnet test test\Ignixa.Hl7v2.Tests\Ignixa.Hl7v2.Tests.csproj --framework net10.0 --verbosity minimal
```

Expected: both commands pass.

- [ ] **Step 5: Commit the HL7v2 provider**

```powershell
git add src\Application\Ignixa.Application\Ignixa.Application.csproj src\Application\Ignixa.Application\Features\Experimental\Transform\Source\Hl7v2Er7SourceDataProvider.cs test\Ignixa.Application.Tests\Features\Transform\Source\Hl7v2Er7SourceDataProviderTests.cs
git commit -m "Add HL7v2 source data provider" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 3: Expose import-aware mapping evaluation

**Files:**
- Modify: `src\Core\Ignixa.FhirMappingLanguage\Evaluation\MappingEvaluator.cs`
- Test: `test\Ignixa.FhirMappingLanguage.Tests\Evaluation\MappingEvaluatorImportTests.cs`

- [ ] **Step 1: Write a compile-time import evaluator test**

Create `test\Ignixa.FhirMappingLanguage.Tests\Evaluation\MappingEvaluatorImportTests.cs`:

```csharp
using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.FhirMappingLanguage.Parser;
using Ignixa.FhirMappingLanguage.Registry;
using Shouldly;

namespace Ignixa.FhirMappingLanguage.Tests.Evaluation;

public class MappingEvaluatorImportTests
{
    [Fact]
    public void GivenRegistryImportResolver_WhenCreatingEvaluator_ThenPublicConstructorIsAvailable()
    {
        var parser = new MappingParser();
        var registry = new MapRegistry();
        var importResolver = new ImportResolver(registry, parser);

        var evaluator = new MappingEvaluator(
            MappingEvaluatorOptions.Default,
            mutator: null,
            importResolver);

        evaluator.ShouldNotBeNull();
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run:

```powershell
dotnet test test\Ignixa.FhirMappingLanguage.Tests\Ignixa.FhirMappingLanguage.Tests.csproj --framework net10.0 --filter MappingEvaluatorImportTests --verbosity minimal
```

Expected: build failure because the constructor accepting `ImportResolver` is inaccessible.

- [ ] **Step 3: Make the import-aware constructor public**

In `src\Core\Ignixa.FhirMappingLanguage\Evaluation\MappingEvaluator.cs`, change:

```csharp
internal MappingEvaluator(
    MappingEvaluatorOptions? options,
    IJsonNodeMutator? mutator,
    ImportResolver? importResolver)
```

to:

```csharp
public MappingEvaluator(
    MappingEvaluatorOptions? options,
    IJsonNodeMutator? mutator,
    ImportResolver? importResolver)
```

- [ ] **Step 4: Run the focused FML tests**

Run:

```powershell
dotnet test test\Ignixa.FhirMappingLanguage.Tests\Ignixa.FhirMappingLanguage.Tests.csproj --framework net10.0 --filter "MappingEvaluatorImportTests|ImportResolverTests" --verbosity minimal
```

Expected: PASS.

- [ ] **Step 5: Commit the evaluator API**

```powershell
git add src\Core\Ignixa.FhirMappingLanguage\Evaluation\MappingEvaluator.cs test\Ignixa.FhirMappingLanguage.Tests\Evaluation\MappingEvaluatorImportTests.cs
git commit -m "Expose import-aware mapping evaluator" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 4: Resolve conformance resources from tenant storage and packages

**Files:**
- Create: `src\Application\Ignixa.Application\Features\Experimental\Transform\Resolution\CanonicalResource.cs`
- Create: `src\Application\Ignixa.Application\Features\Experimental\Transform\Resolution\ICanonicalResourceResolver.cs`
- Create: `src\Application\Ignixa.Application\Features\Experimental\Transform\Resolution\FhirServerCanonicalResourceResolver.cs`
- Test: `test\Ignixa.Application.Tests\Features\Transform\Resolution\FhirServerCanonicalResourceResolverTests.cs`

- [ ] **Step 1: Write failing resolver precedence tests**

Create `test\Ignixa.Application.Tests\Features\Transform\Resolution\FhirServerCanonicalResourceResolverTests.cs` with these cases:

```csharp
using System.Text;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Experimental.Transform.Resolution;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Transform.Resolution;

public class FhirServerCanonicalResourceResolverTests
{
    [Fact]
    public async Task GivenTenantResourceAndPackageResource_WhenResolving_ThenTenantWins()
    {
        var fixture = ResolverFixture.Create();
        fixture.SearchService.SearchStreamAsync(
                Arg.Any<SearchOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(SearchEntries(fixture.TenantConceptMap));

        var result = await fixture.Resolver.ResolveByCanonicalAsync(
            "ConceptMap",
            "https://example.org/ConceptMap/gender",
            CancellationToken.None);

        result.ShouldNotBeNull();
        result.Source.ShouldBe(CanonicalResourceSource.Tenant);
        result.ResourceId.ShouldBe("tenant-gender");
        await fixture.PackageRepository.DidNotReceiveWithAnyArgs()
            .GetLatestByCanonicalAsync(default!, default, default);
    }

    [Fact]
    public async Task GivenNoTenantResource_WhenResolving_ThenUsesPackageFallback()
    {
        var fixture = ResolverFixture.Create();
        fixture.SearchService.SearchStreamAsync(
                Arg.Any<SearchOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(SearchEntries());
        fixture.PackageRepository.GetLatestByCanonicalAsync(
                "https://example.org/ConceptMap/gender",
                "ConceptMap",
                Arg.Any<CancellationToken>())
            .Returns(fixture.PackageConceptMap);

        var result = await fixture.Resolver.ResolveByCanonicalAsync(
            "ConceptMap",
            "https://example.org/ConceptMap/gender",
            CancellationToken.None);

        result.ShouldNotBeNull();
        result.Source.ShouldBe(CanonicalResourceSource.Package);
    }

    private static async IAsyncEnumerable<SearchEntryResult> SearchEntries(
        params SearchEntryResult[] entries)
    {
        foreach (var entry in entries)
        {
            yield return entry;
        }

        await Task.CompletedTask;
    }

    private sealed class ResolverFixture
    {
        private const string ConceptMapJson = """
            {
              "resourceType": "ConceptMap",
              "id": "tenant-gender",
              "url": "https://example.org/ConceptMap/gender",
              "status": "active"
            }
            """;

        private ResolverFixture(
            FhirServerCanonicalResourceResolver resolver,
            ISearchService searchService,
            IPackageResourceRepository packageRepository)
        {
            Resolver = resolver;
            SearchService = searchService;
            PackageRepository = packageRepository;
            TenantConceptMap = new SearchEntryResult(
                "ConceptMap",
                "tenant-gender",
                "1",
                DateTimeOffset.UtcNow,
                Encoding.UTF8.GetBytes(ConceptMapJson));
            PackageConceptMap = new PackageResource
            {
                PackageId = "example.maps",
                PackageVersion = "1.0.0",
                ResourceType = "ConceptMap",
                Canonical = "https://example.org/ConceptMap/gender",
                Version = "1.0.0",
                ResourceId = "package-gender",
                ResourceJson = ConceptMapJson.Replace(
                    "tenant-gender",
                    "package-gender",
                    StringComparison.Ordinal),
                FhirVersion = "4.0.1",
            };
        }

        public FhirServerCanonicalResourceResolver Resolver { get; }

        public ISearchService SearchService { get; }

        public IPackageResourceRepository PackageRepository { get; }

        public SearchEntryResult TenantConceptMap { get; }

        public PackageResource PackageConceptMap { get; }

        public static ResolverFixture Create()
        {
            var repositoryFactory =
                Substitute.For<IFhirRepositoryFactory>();
            var searchServiceFactory =
                Substitute.For<ISearchServiceFactory>();
            var searchService = Substitute.For<ISearchService>();
            var searchOptionsBuilderFactory =
                Substitute.For<ISearchOptionsBuilderFactory>();
            var searchOptionsBuilder =
                Substitute.For<ISearchOptionsBuilder>();
            var packageRepository =
                Substitute.For<IPackageResourceRepository>();
            var versionContext = Substitute.For<IFhirVersionContext>();
            var contextAccessor =
                Substitute.For<IFhirRequestContextAccessor>();
            var schema = Substitute.For<ISchema>();

            contextAccessor.RequestContext.Returns(new FhirRequestContext
            {
                TenantId = 1,
                FhirVersion = FhirVersion.R4,
            });
            searchOptionsBuilderFactory.Create(FhirVersion.R4, 1)
                .Returns(searchOptionsBuilder);
            searchOptionsBuilder.Build(
                    Arg.Any<string>(),
                    Arg.Any<IReadOnlyList<QueryParameter>>(),
                    Arg.Any<ISchema>())
                .Returns(new SearchOptions
                {
                    ResourceType = "ConceptMap",
                    MaxItemCount = 2,
                });
            searchServiceFactory.GetSearchServiceAsync(
                    1,
                    Arg.Any<CancellationToken>())
                .Returns(searchService);
            versionContext.GetSchemaProvider(FhirVersion.R4, 1)
                .Returns(schema);

            var resolver = new FhirServerCanonicalResourceResolver(
                repositoryFactory,
                searchServiceFactory,
                searchOptionsBuilderFactory,
                packageRepository,
                versionContext,
                contextAccessor);

            return new ResolverFixture(
                resolver,
                searchService,
                packageRepository);
        }
    }
}
```

- [ ] **Step 2: Run the resolver tests to verify they fail**

Run:

```powershell
dotnet test test\Ignixa.Application.Tests\Ignixa.Application.Tests.csproj --filter FhirServerCanonicalResourceResolverTests --verbosity minimal
```

Expected: build failure for missing canonical resolver types.

- [ ] **Step 3: Add the resolver models and interface**

Create `src\Application\Ignixa.Application\Features\Experimental\Transform\Resolution\CanonicalResource.cs`:

```csharp
namespace Ignixa.Application.Features.Experimental.Transform.Resolution;

public enum CanonicalResourceSource
{
    Tenant,
    Package,
}

public sealed record CanonicalResource(
    string ResourceType,
    string ResourceId,
    string? VersionId,
    string Json,
    CanonicalResourceSource Source);
```

Create `src\Application\Ignixa.Application\Features\Experimental\Transform\Resolution\ICanonicalResourceResolver.cs`:

```csharp
namespace Ignixa.Application.Features.Experimental.Transform.Resolution;

public interface ICanonicalResourceResolver
{
    Task<CanonicalResource?> ResolveByIdAsync(
        string resourceType,
        string resourceId,
        CancellationToken cancellationToken);

    Task<CanonicalResource?> ResolveByCanonicalAsync(
        string resourceType,
        string canonical,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 4: Implement tenant-first canonical resolution**

Create `src\Application\Ignixa.Application\Features\Experimental\Transform\Resolution\FhirServerCanonicalResourceResolver.cs`:

```csharp
using System.Text;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Parsing;

namespace Ignixa.Application.Features.Experimental.Transform.Resolution;

public sealed class FhirServerCanonicalResourceResolver(
    IFhirRepositoryFactory repositoryFactory,
    ISearchServiceFactory searchServiceFactory,
    ISearchOptionsBuilderFactory searchOptionsBuilderFactory,
    IPackageResourceRepository packageRepository,
    IFhirVersionContext versionContext,
    IFhirRequestContextAccessor contextAccessor)
    : ICanonicalResourceResolver
{
    public async Task<CanonicalResource?> ResolveByIdAsync(
        string resourceType,
        string resourceId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceType);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        var requestContext = GetRequestContext();
        var repository = await repositoryFactory.GetRepositoryAsync(
            requestContext.TenantId,
            cancellationToken);
        var result = await repository.GetAsync(
            new ResourceKey(
                resourceType,
                resourceId,
                TenantId: requestContext.TenantId),
            cancellationToken);

        return result is null || result.IsDeleted
            ? null
            : FromTenant(result);
    }

public async Task<CanonicalResource?> ResolveByCanonicalAsync(
    string resourceType,
    string canonical,
    CancellationToken cancellationToken)
{
    var (url, version) = SplitCanonical(canonical);
    var requestContext = GetRequestContext();
    var builder = searchOptionsBuilderFactory.Create(
        requestContext.FhirVersion,
        requestContext.TenantId);
    var parameters = new List<QueryParameter>
    {
        new("url", url),
        new("status", "active"),
        new("_count", "2"),
    };

    if (version is not null)
    {
        parameters.Add(new QueryParameter("version", version));
    }

    var schema = versionContext.GetSchemaProvider(
        requestContext.FhirVersion,
        requestContext.TenantId);
    var searchOptions = builder.Build(resourceType, parameters, schema);
    var searchService = await searchServiceFactory.GetSearchServiceAsync(
        requestContext.TenantId,
        cancellationToken);
    var matches = new List<SearchEntryResult>(2);

    await foreach (var match in searchService.SearchStreamAsync(
        searchOptions,
        cancellationToken))
    {
        matches.Add(match);
        if (matches.Count == 2)
        {
            break;
        }
    }

    if (matches.Count > 1)
    {
        throw new InvalidOperationException(
            $"Canonical '{canonical}' resolves to multiple active {resourceType} resources");
    }

    if (matches.Count == 1)
    {
        return FromTenant(matches[0]);
    }

    var packageResource = version is null
        ? await packageRepository.GetLatestByCanonicalAsync(
            url,
            resourceType,
            cancellationToken)
        : await packageRepository.GetByCanonicalAsync(
            url,
            version,
            cancellationToken);

    return packageResource is null ||
        !string.Equals(
            packageResource.ResourceType,
            resourceType,
            StringComparison.Ordinal)
        ? null
        : FromPackage(packageResource);
}

    private FhirRequestContext GetRequestContext() =>
        contextAccessor.RequestContext
        ?? throw new InvalidOperationException(
            "FHIR request context not available");

    private static (string Url, string? Version) SplitCanonical(
        string canonical)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonical);
        var separator = canonical.LastIndexOf('|');
        return separator <= 0
            ? (canonical, null)
            : (canonical[..separator], canonical[(separator + 1)..]);
    }

    private static CanonicalResource FromTenant(
        SearchEntryResult resource) =>
        new(
            resource.ResourceType,
            resource.ResourceId,
            resource.VersionId,
            Encoding.UTF8.GetString(resource.ResourceBytes.Span),
            CanonicalResourceSource.Tenant);

    private static CanonicalResource FromPackage(
        PackageResource resource) =>
        new(
            resource.ResourceType,
            resource.ResourceId,
            resource.Version,
            resource.ResourceJson,
            CanonicalResourceSource.Package);
}
```

- [ ] **Step 5: Run the resolver tests**

Run:

```powershell
dotnet test test\Ignixa.Application.Tests\Ignixa.Application.Tests.csproj --filter FhirServerCanonicalResourceResolverTests --verbosity minimal
```

Expected: PASS.

- [ ] **Step 6: Commit canonical resolution**

```powershell
git add src\Application\Ignixa.Application\Features\Experimental\Transform\Resolution test\Ignixa.Application.Tests\Features\Transform\Resolution\FhirServerCanonicalResourceResolverTests.cs
git commit -m "Resolve tenant conformance resources" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 5: Compile stored StructureMaps and their imports

**Files:**
- Create: `src\Application\Ignixa.Application\Features\Experimental\Transform\Resolution\ResolvedStructureMap.cs`
- Create: `src\Application\Ignixa.Application\Features\Experimental\Transform\Resolution\IStoredStructureMapResolver.cs`
- Create: `src\Application\Ignixa.Application\Features\Experimental\Transform\Resolution\StoredStructureMapResolver.cs`
- Test: `test\Ignixa.Application.Tests\Features\Transform\Resolution\StoredStructureMapResolverTests.cs`

- [ ] **Step 1: Write failing recursive import tests**

Create `test\Ignixa.Application.Tests\Features\Transform\Resolution\StoredStructureMapResolverTests.cs`:

```csharp
using Ignixa.Application.Features.Experimental.Transform.Resolution;
using Ignixa.FhirMappingLanguage.Parser;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Transform.Resolution;

public class StoredStructureMapResolverTests
{
    [Fact]
    public async Task GivenStoredMapWithImport_WhenResolving_ThenCompilesBothMaps()
    {
        var resources = Substitute.For<ICanonicalResourceResolver>();
        resources.ResolveByIdAsync(
                "StructureMap",
                "adt-a01-to-bundle",
                Arg.Any<CancellationToken>())
            .Returns(new CanonicalResource(
                "StructureMap",
                "adt-a01-to-bundle",
                "1",
                RootMapJson,
                CanonicalResourceSource.Tenant));
        resources.ResolveByCanonicalAsync(
                "StructureMap",
                "https://example.org/StructureMap/pid-to-patient",
                Arg.Any<CancellationToken>())
            .Returns(new CanonicalResource(
                "StructureMap",
                "pid-to-patient",
                "1",
                ImportedMapJson,
                CanonicalResourceSource.Tenant));
        var resolver = new StoredStructureMapResolver(
            resources,
            new StructureMapParser());

        var result = await resolver.ResolveByIdAsync(
            "adt-a01-to-bundle",
            CancellationToken.None);

        result.Map.Url.ShouldBe("https://example.org/StructureMap/adt-a01-to-bundle");
        result.Registry.Contains(
            "https://example.org/StructureMap/pid-to-patient").ShouldBeTrue();
    }

    private const string RootMapJson = """
        {
          "resourceType": "StructureMap",
          "id": "adt-a01-to-bundle",
          "url": "https://example.org/StructureMap/adt-a01-to-bundle",
          "status": "active",
          "structure": [
            {
              "url": "https://example.org/StructureDefinition/Hl7v2AdtA01",
              "mode": "source",
              "alias": "AdtA01"
            },
            {
              "url": "http://hl7.org/fhir/StructureDefinition/Bundle",
              "mode": "target",
              "alias": "Bundle"
            }
          ],
          "import": [
            "https://example.org/StructureMap/pid-to-patient"
          ],
          "group": [{
            "name": "Main",
            "typeMode": "none",
            "input": [
              { "name": "src", "mode": "source" },
              { "name": "tgt", "mode": "target" }
            ]
          }]
        }
        """;

    private const string ImportedMapJson = """
        {
          "resourceType": "StructureMap",
          "id": "pid-to-patient",
          "url": "https://example.org/StructureMap/pid-to-patient",
          "status": "active",
          "structure": [
            {
              "url": "https://example.org/StructureDefinition/Pid",
              "mode": "source",
              "alias": "Pid"
            },
            {
              "url": "http://hl7.org/fhir/StructureDefinition/Patient",
              "mode": "target",
              "alias": "Patient"
            }
          ],
          "group": [{
            "name": "PidToPatient",
            "typeMode": "none",
            "input": [
              { "name": "src", "mode": "source" },
              { "name": "tgt", "mode": "target" }
            ]
          }]
        }
        """;
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run:

```powershell
dotnet test test\Ignixa.Application.Tests\Ignixa.Application.Tests.csproj --filter StoredStructureMapResolverTests --verbosity minimal
```

Expected: build failure for missing stored-map resolver types.

- [ ] **Step 3: Add the resolved-map contract**

Create `src\Application\Ignixa.Application\Features\Experimental\Transform\Resolution\ResolvedStructureMap.cs`:

```csharp
using Ignixa.FhirMappingLanguage.Expressions;
using Ignixa.FhirMappingLanguage.Registry;

namespace Ignixa.Application.Features.Experimental.Transform.Resolution;

public sealed record ResolvedStructureMap(
    MapExpression Map,
    IMapRegistry Registry);
```

Create `src\Application\Ignixa.Application\Features\Experimental\Transform\Resolution\IStoredStructureMapResolver.cs`:

```csharp
namespace Ignixa.Application.Features.Experimental.Transform.Resolution;

public interface IStoredStructureMapResolver
{
    Task<ResolvedStructureMap> ResolveByIdAsync(
        string structureMapId,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 4: Implement recursive stored-map resolution**

Create `src\Application\Ignixa.Application\Features\Experimental\Transform\Resolution\StoredStructureMapResolver.cs`:

```csharp
using Ignixa.FhirMappingLanguage.Expressions;
using Ignixa.FhirMappingLanguage.Parser;
using Ignixa.FhirMappingLanguage.Registry;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Application.Features.Experimental.Transform.Resolution;

public sealed class StoredStructureMapResolver(
    ICanonicalResourceResolver resourceResolver,
    StructureMapParser structureMapParser) : IStoredStructureMapResolver
{
    public async Task<ResolvedStructureMap> ResolveByIdAsync(
        string structureMapId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(structureMapId);

        var resource = await resourceResolver.ResolveByIdAsync(
            "StructureMap",
            structureMapId,
            cancellationToken)
            ?? throw new KeyNotFoundException(
                $"StructureMap/{structureMapId} was not found");
        var registry = new MapRegistry();
        var map = Parse(resource);
        await RegisterImportsAsync(
            map,
            registry,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            cancellationToken);
        registry.Register(map);
        return new ResolvedStructureMap(map, registry);
    }

    private async Task RegisterImportsAsync(
        MapExpression map,
        IMapRegistry registry,
        HashSet<string> visiting,
        CancellationToken cancellationToken)
    {
        if (!visiting.Add(map.Url))
        {
            throw new InvalidOperationException(
                $"Circular StructureMap import detected at '{map.Url}'");
        }

        foreach (var import in map.Imports)
        {
            if (registry.Contains(import.Url))
            {
                continue;
            }

            var resource = await resourceResolver.ResolveByCanonicalAsync(
                "StructureMap",
                import.Url,
                cancellationToken)
                ?? throw new KeyNotFoundException(
                    $"Imported StructureMap '{import.Url}' was not found");
            var importedMap = Parse(resource);
            await RegisterImportsAsync(
                importedMap,
                registry,
                visiting,
                cancellationToken);
            registry.Register(importedMap);
        }

        visiting.Remove(map.Url);
    }

    private MapExpression Parse(CanonicalResource resource)
    {
        var structureMap = JsonSourceNodeFactory.Parse<StructureMapJsonNode>(
            resource.Json);
        return structureMapParser.Parse(structureMap);
    }
}
```

- [ ] **Step 5: Run the stored-map tests**

Run:

```powershell
dotnet test test\Ignixa.Application.Tests\Ignixa.Application.Tests.csproj --filter StoredStructureMapResolverTests --verbosity minimal
```

Expected: PASS.

- [ ] **Step 6: Commit stored-map compilation**

```powershell
git add src\Application\Ignixa.Application\Features\Experimental\Transform\Resolution test\Ignixa.Application.Tests\Features\Transform\Resolution\StoredStructureMapResolverTests.cs
git commit -m "Resolve stored StructureMap imports" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 6: Translate codes from tenant-stored ConceptMaps

**Files:**
- Create: `src\Application\Ignixa.Application\Features\Experimental\Transform\Resolution\StoredConceptMapResolver.cs`
- Test: `test\Ignixa.Application.Tests\Features\Transform\Resolution\StoredConceptMapResolverTests.cs`

- [ ] **Step 1: Write failing stored ConceptMap tests**

Create `test\Ignixa.Application.Tests\Features\Transform\Resolution\StoredConceptMapResolverTests.cs`:

```csharp
using Ignixa.Application.Features.Experimental.Transform.Resolution;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Transform.Resolution;

public class StoredConceptMapResolverTests
{
    [Fact]
    public async Task GivenStoredGenderMap_WhenTranslatingFemale_ThenReturnsFhirCode()
    {
        var resources = Substitute.For<ICanonicalResourceResolver>();
        resources.ResolveByCanonicalAsync(
                "ConceptMap",
                "https://example.org/ConceptMap/administrative-sex",
                Arg.Any<CancellationToken>())
            .Returns(new CanonicalResource(
                "ConceptMap",
                "administrative-sex",
                "1",
                ConceptMapJson,
                CanonicalResourceSource.Tenant));
        var resolver = new StoredConceptMapResolver(resources);

        var result = await resolver.TranslateAsync(
            "https://example.org/ConceptMap/administrative-sex",
            "http://terminology.hl7.org/CodeSystem/v2-0001",
            "F",
            CancellationToken.None);

        result.ShouldBe("female");
    }

    private const string ConceptMapJson = """
        {
          "resourceType": "ConceptMap",
          "id": "administrative-sex",
          "url": "https://example.org/ConceptMap/administrative-sex",
          "status": "active",
          "group": [{
            "source": "http://terminology.hl7.org/CodeSystem/v2-0001",
            "target": "http://hl7.org/fhir/administrative-gender",
            "element": [{
              "code": "F",
              "target": [{ "code": "female", "equivalence": "equivalent" }]
            }]
          }]
        }
        """;
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run:

```powershell
dotnet test test\Ignixa.Application.Tests\Ignixa.Application.Tests.csproj --filter StoredConceptMapResolverTests --verbosity minimal
```

Expected: build failure for missing `StoredConceptMapResolver`.

- [ ] **Step 3: Implement direct ConceptMap translation**

Create `src\Application\Ignixa.Application\Features\Experimental\Transform\Resolution\StoredConceptMapResolver.cs`:

```csharp
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Application.Features.Experimental.Transform.Resolution;

public sealed class StoredConceptMapResolver(
    ICanonicalResourceResolver resourceResolver)
{
    public async Task<string?> TranslateAsync(
        string conceptMapCanonical,
        string sourceSystem,
        string sourceCode,
        CancellationToken cancellationToken)
    {
        var resource = await resourceResolver.ResolveByCanonicalAsync(
            "ConceptMap",
            conceptMapCanonical,
            cancellationToken)
            ?? throw new KeyNotFoundException(
                $"ConceptMap '{conceptMapCanonical}' was not found");
        var conceptMap = JsonSourceNodeFactory.Parse<ConceptMapJsonNode>(
            resource.Json);
        var group = conceptMap.Group.FirstOrDefault(candidate =>
            string.Equals(
                candidate.Source,
                sourceSystem,
                StringComparison.Ordinal));
        var element = group?.Element.FirstOrDefault(candidate =>
            string.Equals(
                candidate.Code,
                sourceCode,
                StringComparison.Ordinal));

        return element?.Target
            .Select(target => target.Code)
            .FirstOrDefault(code => !string.IsNullOrWhiteSpace(code));
    }
}
```

- [ ] **Step 4: Run the ConceptMap tests**

Run:

```powershell
dotnet test test\Ignixa.Application.Tests\Ignixa.Application.Tests.csproj --filter StoredConceptMapResolverTests --verbosity minimal
```

Expected: PASS.

- [ ] **Step 5: Commit stored ConceptMap translation**

```powershell
git add src\Application\Ignixa.Application\Features\Experimental\Transform\Resolution\StoredConceptMapResolver.cs test\Ignixa.Application.Tests\Features\Transform\Resolution\StoredConceptMapResolverTests.cs
git commit -m "Resolve stored ConceptMap translations" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 7: Extract a source-agnostic FML execution service

**Files:**
- Create: `src\Application\Ignixa.Application\Features\Experimental\Transform\IMappingExecutionService.cs`
- Create: `src\Application\Ignixa.Application\Features\Experimental\Transform\MappingExecutionService.cs`
- Modify: `src\Application\Ignixa.Application\Features\Experimental\Transform\TransformResourceHandler.cs`
- Test: `test\Ignixa.Application.Tests\Features\Transform\MappingExecutionServiceTests.cs`
- Modify: `test\Ignixa.Application.Tests\Features\Transform\TransformResourceHandlerTests.cs`

- [ ] **Step 1: Write the failing non-FHIR source execution test**

Create `test\Ignixa.Application.Tests\Features\Transform\MappingExecutionServiceTests.cs`:

```csharp
using Ignixa.Abstractions;
using Ignixa.Application.Features.Experimental.Transform;
using Ignixa.Application.Features.Experimental.Transform.Resolution;
using Ignixa.Application.Infrastructure;
using Ignixa.FhirMappingLanguage.Parser;
using Ignixa.FhirMappingLanguage.Registry;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Hl7v2.LogicalModel;
using Ignixa.Serialization;
using Ignixa.Specification.Generated;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Transform;

public class MappingExecutionServiceTests
{
    [Fact]
    public async Task GivenHl7v2LogicalSource_WhenExecutingMap_ThenReturnsPatient()
    {
        var parser = new MappingParser();
        var map = parser.Parse("""
            map 'https://example.org/StructureMap/adt-a01-patient' = 'AdtA01Patient'
            uses 'https://example.org/StructureDefinition/Hl7v2AdtA01' alias AdtA01 as source
            uses 'http://hl7.org/fhir/StructureDefinition/Patient' alias Patient as target
            group Main(source msg : AdtA01, target patient : Patient) {
              msg.PID as pid then {
                pid.administrativeSex as sex -> patient.gender = copy(sex);
              };
            }
            """);
        var registry = new MapRegistry();
        registry.Register(map);
        var source = new Hl7v2Element(
            "msg",
            "Hl7v2AdtA01",
            children:
            [
                new Hl7v2Element(
                    "PID",
                    "Pid",
                    children:
                    [
                        new Hl7v2Element(
                            "administrativeSex",
                            "string",
                            "female"),
                    ]),
            ]);
        var service = CreateService();

        var result = await service.ExecuteAsync(
            new ResolvedStructureMap(map, registry),
            source,
            CancellationToken.None);

        result.ResourceType.ShouldBe("Patient");
        result.MutableNode()!["gender"]!.GetValue<string>().ShouldBe("female");
    }

    private static MappingExecutionService CreateService()
    {
        var versionContext = Substitute.For<IFhirVersionContext>();
        versionContext.GetSchemaProvider(
                FhirVersion.R4,
                Arg.Any<int?>())
            .Returns(new R4CoreSchemaProvider());
        var requestContext = Substitute.For<IFhirRequestContextAccessor>();
        requestContext.RequestContext.Returns(new FhirRequestContext
        {
            TenantId = 1,
            FhirVersion = FhirVersion.R4,
        });
        var fhirPathParser = new FhirPathParser();
        var fhirPathEvaluator = new FhirPathEvaluator();
        var expressionCache = new FhirPathExpressionCache(
            fhirPathParser,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<FhirPathExpressionCache>.Instance);
        var evaluatorWithTimeout = new FhirPathEvaluatorWithTimeout(
            expressionCache,
            fhirPathEvaluator,
            TimeSpan.FromSeconds(5),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<FhirPathEvaluatorWithTimeout>.Instance);

        return new MappingExecutionService(
            new MappingParser(),
            new StoredConceptMapResolver(
                Substitute.For<ICanonicalResourceResolver>()),
            fhirPathParser,
            fhirPathEvaluator,
            evaluatorWithTimeout,
            versionContext,
            requestContext,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<MappingExecutionService>.Instance);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run:

```powershell
dotnet test test\Ignixa.Application.Tests\Ignixa.Application.Tests.csproj --filter MappingExecutionServiceTests --verbosity minimal
```

Expected: build failure for missing `MappingExecutionService`.

- [ ] **Step 3: Add the execution contract**

Create `src\Application\Ignixa.Application\Features\Experimental\Transform\IMappingExecutionService.cs`:

```csharp
using Ignixa.Abstractions;
using Ignixa.Application.Features.Experimental.Transform.Resolution;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Application.Features.Experimental.Transform;

public interface IMappingExecutionService
{
    Task<ResourceJsonNode> ExecuteAsync(
        ResolvedStructureMap resolvedMap,
        IElement source,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 4: Implement source-agnostic execution**

Create `src\Application\Ignixa.Application\Features\Experimental\Transform\MappingExecutionService.cs`:

```csharp
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Experimental.Transform.Resolution;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.FhirMappingLanguage.Expressions;
using Ignixa.FhirMappingLanguage.Mutator;
using Ignixa.FhirMappingLanguage.Parser;
using Ignixa.FhirMappingLanguage.Registry;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Experimental.Transform;

public sealed class MappingExecutionService(
    MappingParser mappingParser,
    StoredConceptMapResolver conceptMapResolver,
    FhirPathParser fhirPathParser,
    FhirPathEvaluator fhirPathEvaluator,
    FhirPathEvaluatorWithTimeout fhirPathEvaluatorWithTimeout,
    IFhirVersionContext versionContext,
    IFhirRequestContextAccessor contextAccessor,
    ILogger<MappingExecutionService> logger)
    : IMappingExecutionService
{
    public async Task<ResourceJsonNode> ExecuteAsync(
        ResolvedStructureMap resolvedMap,
        IElement source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resolvedMap);
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();

        var requestContext = contextAccessor.RequestContext
            ?? throw new InvalidOperationException(
                "FHIR request context not available");
        var schema = versionContext.GetSchemaProvider(
            requestContext.FhirVersion,
            requestContext.TenantId);
        var entryGroup = resolvedMap.Map.Groups.FirstOrDefault()
            ?? throw new InvalidOperationException(
                "StructureMap has no groups");
        var sourceParameter = entryGroup.Parameters.FirstOrDefault(
            parameter => parameter.Mode == ParameterMode.Source)
            ?? throw new InvalidOperationException(
                $"StructureMap group '{entryGroup.Name}' has no source parameter");
        var targetParameter = entryGroup.Parameters.FirstOrDefault(
            parameter => parameter.Mode == ParameterMode.Target)
            ?? throw new InvalidOperationException(
                $"StructureMap group '{entryGroup.Name}' has no target parameter");
        var targetType = DetermineTargetType(resolvedMap.Map);
        var target = CreateResource(targetType);
        var targetElement = target.ToElement(schema);
        var mappingContext = new MappingContext
        {
            ErrorMode = ErrorMode.Strict,
            ResourceCreator = resourceType =>
                CreateResource(resourceType).ToElement(schema),
            ConceptMapResolver =
                (conceptMapUrl, sourceSystem, sourceCode) =>
                    conceptMapResolver.TranslateAsync(
                            conceptMapUrl,
                            sourceSystem,
                            sourceCode,
                            cancellationToken)
                        .GetAwaiter()
                        .GetResult(),
            Logger = message => logger.LogDebug(
                "Mapping execution: {Message}",
                message),
            FhirPathEvaluator = (expression, element) =>
                fhirPathEvaluatorWithTimeout.Evaluate(
                    expression,
                    element),
        };
        mappingContext.SetSource(sourceParameter.Name, source);
        mappingContext.SetTarget(targetParameter.Name, targetElement);
        mappingContext.SetTargetResource(targetParameter.Name, target);

        var mutator = new JsonNodeMutator(
            fhirPathEvaluator,
            fhirPathParser,
            () => schema);
        var importResolver = new ImportResolver(
            resolvedMap.Registry,
            mappingParser);
        await importResolver.ResolveImportsAsync(resolvedMap.Map);
        var evaluator = new MappingEvaluator(
            MappingEvaluatorOptions.Default,
            mutator,
            importResolver);

        cancellationToken.ThrowIfCancellationRequested();
        evaluator.Execute(resolvedMap.Map, mappingContext);
        cancellationToken.ThrowIfCancellationRequested();
        return target;
    }

    private static string DetermineTargetType(MapExpression map)
    {
        var targetUses = map.Uses.FirstOrDefault(
            uses => uses.Mode == ModelMode.Target)
            ?? throw new InvalidOperationException(
                "StructureMap has no target structure");
        var separator = targetUses.Url.LastIndexOf('/');
        if (separator < 0 || separator == targetUses.Url.Length - 1)
        {
            throw new InvalidOperationException(
                $"Cannot determine target type from '{targetUses.Url}'");
        }

        return targetUses.Url[(separator + 1)..];
    }

    private static ResourceJsonNode CreateResource(
        string resourceType)
    {
        var json = new JsonObject
        {
            ["resourceType"] = resourceType,
        };
        return JsonSourceNodeFactory.Parse<ResourceJsonNode>(
            json.ToJsonString());
    }
}
```

- [ ] **Step 5: Refactor existing `$transform` to use the service**

Inject `IMappingExecutionService` into `TransformResourceHandler`. Keep `ResolveMapAsync` and supporting-map registration unchanged, then replace target creation and evaluator setup with:

```csharp
var fhirContext = contextAccessor.RequestContext
    ?? throw new InvalidOperationException(
        "FHIR request context not available");
var schema = versionContext.GetSchemaProvider(
    fhirContext.FhirVersion,
    fhirContext.TenantId);
var source = request.Content.ToElement(schema);
var resolvedMap = new ResolvedStructureMap(map, mapCache);

return await mappingExecutionService.ExecuteAsync(
    resolvedMap,
    source,
    cancellationToken);
```

Remove constructor dependencies and private methods used only by the deleted evaluator block. Keep map resolution priority `SrcMaps`, `SourceMap`, then `Source`.

- [ ] **Step 6: Run old and new transform tests**

Run:

```powershell
dotnet test test\Ignixa.Application.Tests\Ignixa.Application.Tests.csproj --filter "MappingExecutionServiceTests|TransformResourceHandlerTests" --verbosity minimal
```

Expected: PASS.

- [ ] **Step 7: Commit shared execution**

```powershell
git add src\Application\Ignixa.Application\Features\Experimental\Transform\IMappingExecutionService.cs src\Application\Ignixa.Application\Features\Experimental\Transform\MappingExecutionService.cs src\Application\Ignixa.Application\Features\Experimental\Transform\TransformResourceHandler.cs test\Ignixa.Application.Tests\Features\Transform\MappingExecutionServiceTests.cs test\Ignixa.Application.Tests\Features\Transform\TransformResourceHandlerTests.cs
git commit -m "Share source-agnostic FML execution" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 8: Add the transform-source command and handler

**Files:**
- Create: `src\Application\Ignixa.Application\Features\Experimental\Transform\TransformSource\TransformSourceCommand.cs`
- Create: `src\Application\Ignixa.Application\Features\Experimental\Transform\TransformSource\TransformSourceHandler.cs`
- Test: `test\Ignixa.Application.Tests\Features\Transform\TransformSourceHandlerTests.cs`

- [ ] **Step 1: Write failing orchestration tests**

Create `test\Ignixa.Application.Tests\Features\Transform\TransformSourceHandlerTests.cs`:

```csharp
using Ignixa.Application.Features.Experimental.Transform.Resolution;
using Ignixa.Application.Features.Experimental.Transform.Source;
using Ignixa.Application.Features.Experimental.Transform.TransformSource;
using Ignixa.FhirMappingLanguage.Expressions;
using Ignixa.FhirMappingLanguage.Registry;
using Ignixa.Hl7v2.LogicalModel;
using Ignixa.Serialization.Models;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Transform;

public class TransformSourceHandlerTests
{
    [Fact]
    public async Task GivenStoredMapAndHl7v2Source_WhenHandling_ThenReturnsMappedResource()
    {
        var mapResolver = Substitute.For<IStoredStructureMapResolver>();
        var provider = Substitute.For<ISourceDataProvider>();
        provider.Format.Returns("hl7v2-er7");
        var root = new Hl7v2Element("msg", "Hl7v2AdtA01");
        provider.ProjectAsync("MSH|^~\\&", Arg.Any<CancellationToken>())
            .Returns(root);
        var execution = Substitute.For<IMappingExecutionService>();
        var expected = new BundleJsonNode();
        execution.ExecuteAsync(
                Arg.Any<ResolvedStructureMap>(),
                root,
                Arg.Any<CancellationToken>())
            .Returns(expected);
        var handler = new TransformSourceHandler(
            mapResolver,
            new SourceDataProviderRegistry([provider]),
            execution);

        var result = await handler.HandleAsync(
            new TransformSourceCommand(
                "adt-a01-to-bundle",
                "hl7v2-er7",
                "MSH|^~\\&"),
            CancellationToken.None);

        result.ShouldBeSameAs(expected);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run:

```powershell
dotnet test test\Ignixa.Application.Tests\Ignixa.Application.Tests.csproj --filter TransformSourceHandlerTests --verbosity minimal
```

Expected: build failure for missing command and handler.

- [ ] **Step 3: Add the command**

Create `src\Application\Ignixa.Application\Features\Experimental\Transform\TransformSource\TransformSourceCommand.cs`:

```csharp
using Ignixa.Serialization.SourceNodes;
using Medino;

namespace Ignixa.Application.Features.Experimental.Transform.TransformSource;

public sealed record TransformSourceCommand(
    string StructureMapId,
    string SourceFormat,
    string SourceData) : IRequest<ResourceJsonNode>;
```

- [ ] **Step 4: Add the handler**

Create `src\Application\Ignixa.Application\Features\Experimental\Transform\TransformSource\TransformSourceHandler.cs`:

```csharp
using Ignixa.Application.Features.Experimental.Transform.Resolution;
using Ignixa.Application.Features.Experimental.Transform.Source;
using Ignixa.Serialization.SourceNodes;
using Medino;

namespace Ignixa.Application.Features.Experimental.Transform.TransformSource;

public sealed class TransformSourceHandler(
    IStoredStructureMapResolver structureMapResolver,
    SourceDataProviderRegistry sourceDataProviderRegistry,
    IMappingExecutionService mappingExecutionService)
    : IRequestHandler<TransformSourceCommand, ResourceJsonNode>
{
    public async Task<ResourceJsonNode> HandleAsync(
        TransformSourceCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.StructureMapId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourceFormat);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourceData);

        var map = await structureMapResolver.ResolveByIdAsync(
            request.StructureMapId,
            cancellationToken);
        var provider = sourceDataProviderRegistry.Resolve(
            request.SourceFormat);
        var source = await provider.ProjectAsync(
            request.SourceData,
            cancellationToken);

        return await mappingExecutionService.ExecuteAsync(
            map,
            source,
            cancellationToken);
    }
}
```

- [ ] **Step 5: Run the handler tests**

Run:

```powershell
dotnet test test\Ignixa.Application.Tests\Ignixa.Application.Tests.csproj --filter TransformSourceHandlerTests --verbosity minimal
```

Expected: PASS.

- [ ] **Step 6: Commit orchestration**

```powershell
git add src\Application\Ignixa.Application\Features\Experimental\Transform\TransformSource test\Ignixa.Application.Tests\Features\Transform\TransformSourceHandlerTests.cs
git commit -m "Add transform source handler" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 9: Add FHIR Parameters parsing and the HTTP operation

**Files:**
- Create: `src\Application\Ignixa.Api\Endpoints\Experimental\TransformSourceRequest.cs`
- Create: `src\Application\Ignixa.Api\Endpoints\Experimental\TransformSourceParametersParser.cs`
- Modify: `src\Application\Ignixa.Application\Features\Experimental\Configuration\ExperimentalOptions.cs`
- Modify: `src\Application\Ignixa.Api\Endpoints\Experimental\TransformEndpoints.cs`
- Modify: `src\Application\Ignixa.Api\Endpoints\Experimental\ExperimentalEndpointExtensions.cs`
- Modify: `src\Application\Ignixa.Web\appsettings.json`
- Test: `test\Ignixa.Api.Tests\Infrastructure\TransformSourceParametersParserTests.cs`

- [ ] **Step 1: Write failing request-parser tests**

Create `test\Ignixa.Api.Tests\Infrastructure\TransformSourceParametersParserTests.cs`:

```csharp
using Ignixa.Api.Endpoints.Experimental;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;
using Shouldly;

namespace Ignixa.Api.Tests.Infrastructure;

public class TransformSourceParametersParserTests
{
    [Fact]
    public void GivenValidParameters_WhenParsing_ThenReturnsSourceValues()
    {
        var parameters = JsonSourceNodeFactory.Parse<ParametersJsonNode>("""
            {
              "resourceType": "Parameters",
              "parameter": [
                { "name": "sourceFormat", "valueCode": "hl7v2-er7" },
                { "name": "sourceData", "valueString": "MSH|^~\\&|SendingApp" }
              ]
            }
            """);

        var result = TransformSourceParametersParser.Parse(parameters);

        result.SourceFormat.ShouldBe("hl7v2-er7");
        result.SourceData.ShouldStartWith("MSH|");
    }

    [Theory]
    [InlineData("""{"resourceType":"Parameters","parameter":[{"name":"sourceData","valueString":"MSH|"}]}""")]
    [InlineData("""{"resourceType":"Parameters","parameter":[{"name":"sourceFormat","valueCode":"hl7v2-er7"}]}""")]
    public void GivenMissingRequiredParameter_WhenParsing_ThenThrows(
        string json)
    {
        var parameters = JsonSourceNodeFactory.Parse<ParametersJsonNode>(json);

        Should.Throw<ArgumentException>(
            () => TransformSourceParametersParser.Parse(parameters));
    }
}
```

- [ ] **Step 2: Run parser tests to verify they fail**

Run:

```powershell
dotnet test test\Ignixa.Api.Tests\Ignixa.Api.Tests.csproj --filter TransformSourceParametersParserTests --verbosity minimal
```

Expected: build failure for missing parser and request types.

- [ ] **Step 3: Implement request parsing**

Create `src\Application\Ignixa.Api\Endpoints\Experimental\TransformSourceRequest.cs`:

```csharp
namespace Ignixa.Api.Endpoints.Experimental;

public sealed record TransformSourceRequest(
    string SourceFormat,
    string SourceData);
```

Create `src\Application\Ignixa.Api\Endpoints\Experimental\TransformSourceParametersParser.cs`:

```csharp
using Ignixa.Serialization;
using Ignixa.Serialization.Models;

namespace Ignixa.Api.Endpoints.Experimental;

public static class TransformSourceParametersParser
{
    public static TransformSourceRequest Parse(
        ParametersJsonNode parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var sourceFormat = parameters.GetParameterStringValue(
            "sourceFormat");
        var sourceData = parameters.GetParameterStringValue(
            "sourceData");

        ArgumentException.ThrowIfNullOrWhiteSpace(
            sourceFormat,
            "sourceFormat");
        ArgumentException.ThrowIfNullOrWhiteSpace(
            sourceData,
            "sourceData");

        return new TransformSourceRequest(sourceFormat, sourceData);
    }
}
```

- [ ] **Step 4: Register tenant and single-tenant routes**

Extend `TransformExperimentalOptions`:

```csharp
public bool SourceTransformEnabled { get; set; }

public int MaxSourceDataBytes { get; set; } = 1_048_576;
```

Add these properties under the existing transform settings in
`src\Application\Ignixa.Web\appsettings.json`:

```json
"SourceTransformEnabled": false,
"MaxSourceDataBytes": 1048576
```

Change `MapTransformEndpoints` and its two private registration methods to accept
`sourceTransformEnabled` and `maxSourceDataBytes`. Keep the existing `$transform`
registrations unconditional inside those methods. Place each complete
`$transform-source` route block shown below inside a conditional guarded by
`sourceTransformEnabled`.

```csharp
public static IEndpointRouteBuilder MapTransformEndpoints(
    this IEndpointRouteBuilder endpoints,
    bool sourceTransformEnabled,
    int maxSourceDataBytes,
    Action<RouteGroupBuilder>? configureTenantGroup = null)

private static void MapTransformTenantEndpoints(
    this IEndpointRouteBuilder endpoints,
    bool sourceTransformEnabled,
    int maxSourceDataBytes,
    Action<RouteGroupBuilder>? configureTenantGroup = null)

private static void MapTransformAgnosticEndpoints(
    this IEndpointRouteBuilder endpoints,
    bool sourceTransformEnabled,
    int maxSourceDataBytes)
```

The public method body forwards the values:

```csharp
endpoints.MapTransformTenantEndpoints(
    sourceTransformEnabled,
    maxSourceDataBytes,
    configureTenantGroup);
endpoints.MapTransformAgnosticEndpoints(
    sourceTransformEnabled,
    maxSourceDataBytes);
return endpoints;
```

Change the call in `ExperimentalEndpointExtensions` to:

```csharp
app.MapTransformEndpoints(
    options.Features.Transform.SourceTransformEnabled,
    options.Features.Transform.MaxSourceDataBytes,
    configureTenantGroup);
```

Modify `TransformEndpoints.cs` to add:

```csharp
tenantGroup.MapPost(
        "/StructureMap/{id}/$transform-source",
        (
            HttpContext context,
            int tenantId,
            string id,
            IMediator mediator,
            RecyclableMemoryStreamManager memoryStreamManager,
            CancellationToken cancellationToken) =>
            HandleTransformSource(
                context,
                tenantId,
                id,
                mediator,
                memoryStreamManager,
                maxSourceDataBytes,
                cancellationToken))
    .WithName("TransformSourceInstance")
    .WithTags("Experimental", "Transform")
    .Accepts<object>(KnownContentTypes.ApplicationFhirJson)
    .Produces<object>(
        StatusCodes.Status200OK,
        KnownContentTypes.ApplicationFhirJson)
    .Produces<object>(
        StatusCodes.Status400BadRequest,
        KnownContentTypes.ApplicationFhirJson)
    .Produces<object>(
        StatusCodes.Status404NotFound,
        KnownContentTypes.ApplicationFhirJson)
    .Produces<object>(
        StatusCodes.Status413PayloadTooLarge,
        KnownContentTypes.ApplicationFhirJson);
```

Add the tenant-agnostic route:

```csharp
endpoints.MapPost(
        "/StructureMap/{id}/$transform-source",
        (
            HttpContext context,
            string id,
            IMediator mediator,
            RecyclableMemoryStreamManager memoryStreamManager,
            CancellationToken cancellationToken) =>
            HandleTransformSourceAgnostic(
                context,
                id,
                mediator,
                memoryStreamManager,
                maxSourceDataBytes,
                cancellationToken))
    .WithName("TransformSourceInstanceAgnostic")
    .WithTags("Experimental", "Transform");
```

Implement the tenant handler:

```csharp
private static async Task<IResult> HandleTransformSource(
    HttpContext httpContext,
    int tenantId,
    string id,
    IMediator mediator,
    RecyclableMemoryStreamManager recyclableMemoryStreamManager,
    int maxSourceDataBytes,
    CancellationToken cancellationToken)
{
    if (httpContext.Request.ContentLength > maxSourceDataBytes)
    {
        return Results.Json(
            CreateOperationOutcome(
                OperationOutcomeJsonNode.IssueSeverity.Error,
                OperationOutcomeJsonNode.IssueType.TooCostly,
                $"Request body exceeds {maxSourceDataBytes} bytes."),
            statusCode: StatusCodes.Status413PayloadTooLarge,
            contentType: KnownContentTypes.ApplicationFhirJson);
    }

    await using var body =
        recyclableMemoryStreamManager.GetStream("transform-source-request");
    await httpContext.Request.Body.CopyToAsync(body, cancellationToken);

    if (body.Length > maxSourceDataBytes)
    {
        return Results.Json(
            CreateOperationOutcome(
                OperationOutcomeJsonNode.IssueSeverity.Error,
                OperationOutcomeJsonNode.IssueType.TooCostly,
                $"Request body exceeds {maxSourceDataBytes} bytes."),
            statusCode: StatusCodes.Status413PayloadTooLarge,
            contentType: KnownContentTypes.ApplicationFhirJson);
    }

    body.Position = 0;

    try
    {
        ParametersJsonNode parameters =
            await JsonSourceNodeFactory.ParseAsync<ParametersJsonNode>(
                body,
                cancellationToken);
        TransformSourceRequest request =
            TransformSourceParametersParser.Parse(parameters);

        ResourceJsonNode result = await mediator.SendAsync(
            new TransformSourceCommand(
                id,
                request.SourceFormat,
                request.SourceData),
            cancellationToken);

        return Results.Bytes(
            result.SerializeToBytes(),
            KnownContentTypes.ApplicationFhirJson);
    }
    catch (JsonException exception)
    {
        return Results.BadRequest(
            CreateOperationOutcome(
                OperationOutcomeJsonNode.IssueSeverity.Error,
                OperationOutcomeJsonNode.IssueType.Invalid,
                exception.Message));
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(
            CreateOperationOutcome(
                OperationOutcomeJsonNode.IssueSeverity.Error,
                OperationOutcomeJsonNode.IssueType.Invalid,
                exception.Message));
    }
    catch (SourceDataFormatNotSupportedException exception)
    {
        return Results.BadRequest(
            CreateOperationOutcome(
                OperationOutcomeJsonNode.IssueSeverity.Error,
                OperationOutcomeJsonNode.IssueType.NotSupported,
                exception.Message));
    }
    catch (SourceDataException exception)
    {
        return Results.BadRequest(
            CreateOperationOutcome(
                OperationOutcomeJsonNode.IssueSeverity.Error,
                OperationOutcomeJsonNode.IssueType.Invalid,
                exception.Message));
    }
    catch (KeyNotFoundException exception)
    {
        return Results.NotFound(
            CreateOperationOutcome(
                OperationOutcomeJsonNode.IssueSeverity.Error,
                OperationOutcomeJsonNode.IssueType.NotFound,
                exception.Message));
    }
    catch (MappingExecutionException exception)
    {
        return Results.BadRequest(
            CreateOperationOutcome(
                OperationOutcomeJsonNode.IssueSeverity.Error,
                OperationOutcomeJsonNode.IssueType.Processing,
                exception.Message));
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(
            CreateOperationOutcome(
                OperationOutcomeJsonNode.IssueSeverity.Error,
                OperationOutcomeJsonNode.IssueType.Processing,
                exception.Message));
    }
}
```

Add the tenant-agnostic wrapper:

```csharp
private static async Task<IResult> HandleTransformSourceAgnostic(
    HttpContext context,
    string id,
    IMediator mediator,
    RecyclableMemoryStreamManager memoryStreamManager,
    int maxSourceDataBytes,
    CancellationToken cancellationToken)
{
    if (!context.Items.TryGetValue("TenantId", out var tenantIdObject)
        || tenantIdObject is not int tenantId)
    {
        return Results.BadRequest(CreateOperationOutcome(
            OperationOutcomeJsonNode.IssueSeverity.Error,
            OperationOutcomeJsonNode.IssueType.Required,
            "TenantId not found. In multi-tenant mode, use "
                + "/tenant/{tenantId}/StructureMap/{id}/$transform-source"));
    }

    return await HandleTransformSource(
        context,
        tenantId,
        id,
        mediator,
        memoryStreamManager,
        maxSourceDataBytes,
        cancellationToken);
}
```

`MapTransformTenantEndpoints` and `MapTransformAgnosticEndpoints` receive
`maxSourceDataBytes` from `MapTransformEndpoints`, which receives
`TransformExperimentalOptions.MaxSourceDataBytes` from
`ExperimentalEndpointExtensions`. Do not log `SourceData` or include it in exception
messages.

- [ ] **Step 5: Run API parser tests**

Run:

```powershell
dotnet test test\Ignixa.Api.Tests\Ignixa.Api.Tests.csproj --filter TransformSourceParametersParserTests --verbosity minimal
```

Expected: PASS.

- [ ] **Step 6: Commit the endpoint**

```powershell
git add src\Application\Ignixa.Application\Features\Experimental\Configuration\ExperimentalOptions.cs src\Application\Ignixa.Api\Endpoints\Experimental\ExperimentalEndpointExtensions.cs src\Application\Ignixa.Api\Endpoints\Experimental\TransformEndpoints.cs src\Application\Ignixa.Api\Endpoints\Experimental\TransformSourceRequest.cs src\Application\Ignixa.Api\Endpoints\Experimental\TransformSourceParametersParser.cs src\Application\Ignixa.Web\appsettings.json test\Ignixa.Api.Tests\Infrastructure\TransformSourceParametersParserTests.cs
git commit -m "Add transform source endpoint" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 10: Register services, configuration, and capability metadata

**Files:**
- Create: `src\Application\Ignixa.Domain\Abstractions\ICustomOperationFeature.cs`
- Create: `src\Application\Ignixa.Application\Features\Experimental\Transform\StructureMapTransformSourceFeature.cs`
- Modify: `src\Application\Ignixa.Application\Features\Experimental\Infrastructure\ExperimentalAutofacRegistration.cs`
- Modify: `src\Application\Ignixa.Application\Features\Metadata\Segments\OperationsSegment.cs`
- Test: `test\Ignixa.Application.Tests\Features\Metadata\Segments\OperationsSegmentTests.cs`

- [ ] **Step 1: Add custom operation canonical metadata**

Create `src\Application\Ignixa.Domain\Abstractions\ICustomOperationFeature.cs`:

```csharp
namespace Ignixa.Domain.Abstractions;

public interface ICustomOperationFeature
{
    IReadOnlyDictionary<string, string> OperationDefinitionCanonicals { get; }
}
```

Create `src\Application\Ignixa.Application\Features\Experimental\Transform\StructureMapTransformSourceFeature.cs`:

```csharp
using Ignixa.Domain.Abstractions;

namespace Ignixa.Application.Features.Experimental.Transform;

public sealed class StructureMapTransformSourceFeature
    : IPackageFeature, ICustomOperationFeature
{
    private static readonly IReadOnlyList<string> Operations =
        ["transform-source"];

    public string PackageId => "ignixa.fhir.operations";

    public IReadOnlyList<string> SystemOperations => [];

    public IReadOnlyDictionary<string, IReadOnlyList<string>>
        ResourceOperations { get; } =
        new Dictionary<string, IReadOnlyList<string>>
        {
            ["StructureMap"] = Operations,
        };

    public IReadOnlyList<string>? SupportedFhirVersions => null;

    public IReadOnlyDictionary<string, string>
        OperationDefinitionCanonicals { get; } =
        new Dictionary<string, string>
        {
            ["transform-source"] =
                "https://ignixa.dev/fhir/OperationDefinition/StructureMap-transform-source",
        };
}
```

After `opDefsByName` is built in `OperationsSegment.ApplyAsync`, collect custom
canonicals:

```csharp
var customCanonicals = _features
    .OfType<ICustomOperationFeature>()
    .SelectMany(feature => feature.OperationDefinitionCanonicals)
    .GroupBy(pair => pair.Key, StringComparer.Ordinal)
    .ToDictionary(
        group => group.Key,
        group => group.First().Value,
        StringComparer.Ordinal);
```

Replace both fallback-canonical assignments with:

```csharp
var fallbackCanonical = customCanonicals.GetValueOrDefault(opName)
    ?? $"http://hl7.org/fhir/OperationDefinition/{opName}";
```

Include custom canonicals in `GetVersionHashAsync` so a canonical change invalidates
the capability cache. Add this block inside the feature loop before its final
semicolon append:

```csharp
if (feature is ICustomOperationFeature customOperationFeature)
{
    foreach (var (operationName, canonical) in
        customOperationFeature.OperationDefinitionCanonicals.OrderBy(
            pair => pair.Key))
    {
        featureDeclarations.Append('C');
        featureDeclarations.Append(operationName);
        featureDeclarations.Append(':');
        featureDeclarations.Append(canonical);
        featureDeclarations.Append('|');
    }
}
```

- [ ] **Step 2: Register the runtime services**

In `ExperimentalAutofacRegistration.RegisterExperimentalServices`, pass `options.Features.Transform` into `RegisterTransformHandlers`.

When `SourceTransformEnabled` is true, register:

```csharp
builder.RegisterType<NhapiHl7v2Parser>()
    .As<IHl7v2Parser>()
    .SingleInstance();
builder.RegisterType<AdtA01Projection>()
    .As<IHl7v2Projection>()
    .SingleInstance();
builder.RegisterType<Hl7v2ProjectionRegistry>()
    .AsSelf()
    .SingleInstance();
builder.RegisterType<Hl7v2Er7SourceDataProvider>()
    .As<ISourceDataProvider>()
    .SingleInstance();
builder.RegisterType<SourceDataProviderRegistry>()
    .AsSelf()
    .SingleInstance();
builder.RegisterType<FhirServerCanonicalResourceResolver>()
    .As<ICanonicalResourceResolver>()
    .InstancePerLifetimeScope();
builder.RegisterType<StoredStructureMapResolver>()
    .As<IStoredStructureMapResolver>()
    .InstancePerLifetimeScope();
builder.RegisterType<StoredConceptMapResolver>()
    .AsSelf()
    .InstancePerLifetimeScope();
builder.RegisterType<MappingExecutionService>()
    .As<IMappingExecutionService>()
    .InstancePerLifetimeScope();
builder.RegisterType<TransformSourceHandler>()
    .As<IRequestHandler<TransformSourceCommand, ResourceJsonNode>>()
    .InstancePerLifetimeScope();
builder.RegisterType<StructureMapTransformSourceFeature>()
    .As<IPackageFeature>()
    .SingleInstance();
```

- [ ] **Step 3: Add capability tests**

Extend `OperationsSegmentTests` with:

```csharp
[Fact]
public async Task GivenTransformSourceEnabled_WhenBuildingCapability_ThenAdvertisesIgnixaCanonical()
{
    // Arrange
    _features.Add(new StructureMapTransformSourceFeature());
    var segment = new OperationsSegment(
        _features,
        _packageResourceRepository,
        NullLogger<OperationsSegment>.Instance);
    var statement = new CapabilityStatementJsonNode();
    var context = new CapabilityContext(
        FhirVersion: FhirVersion.R4,
        TenantId: 1);
    _packageResourceRepository
        .GetOperationDefinitionsAsync(
            Arg.Is<List<string>>(
                operations => operations.Contains("transform-source")),
            "R4",
            Arg.Any<CancellationToken>())
        .Returns([]);

    // Act
    await segment.ApplyAsync(
        statement,
        context,
        CancellationToken.None);

    // Assert
    var structureMap = statement.Rest[0].Resource
        .Single(resource => resource.Type == "StructureMap");
    var operation = structureMap.MutableNode()["operation"]!
        .AsArray()
        .Single(candidate =>
            candidate!["name"]!.GetValue<string>() == "transform-source");
    operation!["definition"]!.GetValue<string>().ShouldBe(
        "https://ignixa.dev/fhir/OperationDefinition/StructureMap-transform-source");
}
```

Add the `Ignixa.Application.Features.Experimental.Transform` using required for
`StructureMapTransformSourceFeature`.

- [ ] **Step 4: Run registration and capability tests**

Run:

```powershell
dotnet test test\Ignixa.Application.Tests\Ignixa.Application.Tests.csproj --filter "OperationsSegmentTests|TransformSourceHandlerTests" --verbosity minimal
dotnet build src\Application\Ignixa.Web\Ignixa.Web.csproj --no-restore
```

Expected: tests pass and web host builds with 0 warnings and 0 errors.

- [ ] **Step 5: Commit registration and metadata**

```powershell
git add src\Application\Ignixa.Domain\Abstractions\ICustomOperationFeature.cs src\Application\Ignixa.Application\Features\Experimental src\Application\Ignixa.Application\Features\Metadata\Segments\OperationsSegment.cs test\Ignixa.Application.Tests\Features\Metadata\Segments\OperationsSegmentTests.cs
git commit -m "Register transform source operation" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 11: Add an end-to-end stored-map conversion test

**Files:**
- Modify: `test\Ignixa.Api.E2ETests\_Infrastructure\IgnixaApiFixture.cs`
- Create: `test\Ignixa.Api.E2ETests\Operations\Transform\TransformSourceTests.cs`

- [ ] **Step 1: Enable source transforms in the E2E fixture**

Add this entry beside the other experimental feature settings in
`IgnixaApiFixture.ConfigureWebHost`:

```csharp
["Experimental:Features:Transform:SourceTransformEnabled"] = "true",
```

- [ ] **Step 2: Write the end-to-end test**

Create `test\Ignixa.Api.E2ETests\Operations\Transform\TransformSourceTests.cs`:

```csharp
using System.Net;
using System.Text;
using System.Text.Json;
using Ignixa.Abstractions;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.E2ETests._Infrastructure.Base;
using Ignixa.FhirMappingLanguage.Parser;
using Ignixa.FhirMappingLanguage.Serialization;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Serialization;
using Ignixa.Serialization.Extensions;
using Shouldly;

namespace Ignixa.Api.E2ETests.Operations.Transform;

public class TransformSourceTests : CapabilityDrivenTestBase
{
    private const string Mapping = """
        map 'http://ignixa.org/fhir/StructureMap/adt-a01-to-patient' = 'AdtA01ToPatient'

        conceptmap '#AdministrativeSex' {
          prefix v2 = 'http://terminology.hl7.org/CodeSystem/v2-0001'
          prefix fhir = 'http://hl7.org/fhir/administrative-gender'

          v2:M == fhir:male
          v2:F == fhir:female
          v2:U == fhir:unknown
        }

        uses 'http://ignixa.org/fhir/StructureDefinition/Hl7v2AdtA01' alias Hl7v2AdtA01 as source
        uses 'http://hl7.org/fhir/StructureDefinition/Patient' alias Patient as target

        group Main(source msg : Hl7v2AdtA01, target patient : Patient) {
          msg.PID.patientIdentifierList as cx -> patient.id = copy(cx.idNumber);
          msg.PID.patientIdentifierList as cx -> patient.identifier = create('Identifier') as identifier then MapCx(cx, identifier);
          msg.PID.patientName as xpn -> patient.name = create('HumanName') as name then MapXpn(xpn, name);
          msg.PID.dateTimeOfBirth -> patient.birthDate = dateOp(msg.PID.dateTimeOfBirth);
          msg.PID.administrativeSex -> patient.gender = translate(msg.PID.administrativeSex, '#AdministrativeSex', 'code');
        }

        group MapCx(source cx : Cx, target identifier : Identifier) {
          cx.idNumber -> identifier.value;
        }

        group MapXpn(source xpn : Xpn, target name : HumanName) {
          xpn.familyName.surname -> name.family;
          xpn.givenName -> name.given;
        }
        """;

    private const string Message =
        "MSH|^~\\&|SendingApp|SendingFacility|ReceivingApp|ReceivingFacility|202607091200||ADT^A01|MSG00001|P|2.5\r"
        + "EVN|A01|202607091200\r"
        + "PID|1||12345^^^MRN^MR||Doe^Jane^A||19800101|F\r"
        + "PV1|1|I\r";

    public TransformSourceTests(IgnixaApiFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task GivenStoredMap_WhenTransformingEr7_ThenReturnsUnpersistedPatient()
    {
        // Arrange
        RequireOperationAnywhere("transform-source");
        await StoreStructureMapAsync();
        using StringContent content = CreateRequest("hl7v2-er7", Message);

        // Act
        using HttpResponseMessage response = await Client.PostAsync(
            "/StructureMap/adt-a01-to-patient/$transform-source",
            content);
        string responseJson = await response.Content.ReadAsStringAsync();

        // Assert
        response.EnsureSuccessStatusCode();
        ResourceJsonNode patient =
            JsonSourceNodeFactory.Parse<ResourceJsonNode>(responseJson);
        patient.ResourceType.ShouldBe("Patient");

        var patientElement = patient.ToElement(SchemaProvider);
        patientElement.Scalar("id").ShouldBe("12345");
        patientElement.Scalar("identifier.value").ShouldBe("12345");
        patientElement.Scalar("name.family").ShouldBe("Doe");
        patientElement.Scalar("name.given.first()").ShouldBe("Jane");
        patientElement.Scalar("birthDate").ShouldBe("1980-01-01");
        patientElement.Scalar("gender").ShouldBe("female");

        using HttpResponseMessage readResponse =
            await Client.GetAsync("/Patient/12345");
        readResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GivenStoredMap_WhenSourceFormatIsUnsupported_ThenReturnsNotSupportedOutcome()
    {
        // Arrange
        await StoreStructureMapAsync();
        using StringContent content = CreateRequest("csv", "id,name");

        // Act
        using HttpResponseMessage response = await Client.PostAsync(
            "/StructureMap/adt-a01-to-patient/$transform-source",
            content);

        // Assert
        await AssertIssueAsync(
            response,
            HttpStatusCode.BadRequest,
            "not-supported");
    }

    [Fact]
    public async Task GivenStoredMap_WhenEr7IsInvalid_ThenReturnsInvalidOutcome()
    {
        // Arrange
        await StoreStructureMapAsync();
        using StringContent content =
            CreateRequest("hl7v2-er7", "not-an-hl7-message");

        // Act
        using HttpResponseMessage response = await Client.PostAsync(
            "/StructureMap/adt-a01-to-patient/$transform-source",
            content);

        // Assert
        await AssertIssueAsync(
            response,
            HttpStatusCode.BadRequest,
            "invalid");
    }

    [Fact]
    public async Task GivenUnknownMap_WhenTransformingEr7_ThenReturnsNotFoundOutcome()
    {
        // Arrange
        using StringContent content = CreateRequest("hl7v2-er7", Message);

        // Act
        using HttpResponseMessage response = await Client.PostAsync(
            "/StructureMap/map-that-does-not-exist/$transform-source",
            content);

        // Assert
        await AssertIssueAsync(
            response,
            HttpStatusCode.NotFound,
            "not-found");
    }

    [Fact]
    public async Task GivenOversizedRequest_WhenTransformingEr7_ThenReturnsTooCostlyOutcome()
    {
        // Arrange
        using StringContent content = CreateRequest(
            "hl7v2-er7",
            new string('X', 1_048_577));

        // Act
        using HttpResponseMessage response = await Client.PostAsync(
            "/StructureMap/adt-a01-to-patient/$transform-source",
            content);

        // Assert
        await AssertIssueAsync(
            response,
            HttpStatusCode.RequestEntityTooLarge,
            "too-costly");
    }

    private async Task StoreStructureMapAsync()
    {
        var parser = new MappingParser();
        var builder = new StructureMapBuilder(FhirVersion.R4);
        ResourceJsonNode structureMap = builder.Build(parser.Parse(Mapping));
        structureMap.Id = "adt-a01-to-patient";
        await Harness.UpdateResourceAsync(structureMap);
    }

    private static StringContent CreateRequest(
        string sourceFormat,
        string sourceData)
    {
        string requestJson = $$"""
            {
              "resourceType": "Parameters",
              "parameter": [
                {
                  "name": "sourceFormat",
                  "valueCode": {{JsonSerializer.Serialize(sourceFormat)}}
                },
                {
                  "name": "sourceData",
                  "valueString": {{JsonSerializer.Serialize(sourceData)}}
                }
              ]
            }
            """;
        return new StringContent(
            requestJson,
            Encoding.UTF8,
            "application/fhir+json");
    }

    private async Task AssertIssueAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedIssueCode)
    {
        response.StatusCode.ShouldBe(expectedStatus);
        string responseJson = await response.Content.ReadAsStringAsync();
        ResourceJsonNode outcome =
            JsonSourceNodeFactory.Parse<ResourceJsonNode>(responseJson);
        outcome.ResourceType.ShouldBe("OperationOutcome");
        outcome.ToElement(SchemaProvider)
            .Scalar("issue.code")
            .ShouldBe(expectedIssueCode);
    }
}
```

- [ ] **Step 3: Run the end-to-end test**

Run:

```powershell
dotnet test test\Ignixa.Api.E2ETests\Ignixa.Api.E2ETests.csproj --filter TransformSourceTests --verbosity minimal
```

Expected: PASS.

- [ ] **Step 4: Commit the end-to-end test**

```powershell
git add test\Ignixa.Api.E2ETests\_Infrastructure\IgnixaApiFixture.cs test\Ignixa.Api.E2ETests\Operations\Transform\TransformSourceTests.cs
git commit -m "Test stored HL7v2 source transform" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 12: Document operation usage and verify the solution

**Files:**
- Create: `docs\site\docs\server\features\structuremap-transform-source.md`
- Modify: `docs\features\hl7v2-mapping\readme.md`
- Modify: `docs\features\hl7v2-mapping\investigations\fhir-mapping-language.md`

- [ ] **Step 1: Document the public contract**

Create `docs\site\docs\server\features\structuremap-transform-source.md`:

````markdown
---
sidebar_position: 8
title: StructureMap source transform
---

# StructureMap source transform

The experimental `$transform-source` operation runs a stored FHIR Mapping Language
(FML) `StructureMap` against non-FHIR source data. Ignixa parses the source format,
projects it to an `IElement` logical model, executes FML, and returns the resulting
FHIR resource.

The operation does not persist the returned resource. A client that wants to store
the result must submit a separate FHIR create, update, or transaction request.

## Enable the operation

The operation is disabled by default:

```json
{
  "Experimental": {
    "Features": {
      "Transform": {
        "Enabled": true,
        "SourceTransformEnabled": true,
        "MaxSourceDataBytes": 1048576
      }
    }
  }
}
```

`MaxSourceDataBytes` limits the complete FHIR `Parameters` request body. Requests
larger than the configured limit receive HTTP 413.

## Store the mapping

Store maps and terminology through normal tenant FHIR APIs:

```http
PUT /tenant/1/StructureMap/adt-a01-to-patient
Content-Type: application/fhir+json

{
  "resourceType": "StructureMap",
  "id": "adt-a01-to-patient",
  "url": "https://example.org/fhir/StructureMap/adt-a01-to-patient",
  "name": "AdtA01ToPatient",
  "status": "active",
  "structure": [
    {
      "url": "https://ignixa.dev/fhir/StructureDefinition/Hl7v2AdtA01",
      "mode": "source",
      "alias": "Hl7v2AdtA01"
    },
    {
      "url": "http://hl7.org/fhir/StructureDefinition/Patient",
      "mode": "target",
      "alias": "Patient"
    }
  ],
  "group": [
    {
      "name": "Main",
      "typeMode": "none",
      "input": [
        { "name": "msg", "type": "Hl7v2AdtA01", "mode": "source" },
        { "name": "patient", "type": "Patient", "mode": "target" }
      ],
      "rule": [
        {
          "name": "MapGender",
          "source": [
            {
              "context": "msg",
              "element": "PID.administrativeSex",
              "variable": "sex"
            }
          ],
          "target": [
            {
              "context": "patient",
              "element": "gender",
              "transform": "translate",
              "parameter": [
                { "valueId": "sex" },
                {
                  "valueString": "https://example.org/fhir/ConceptMap/administrative-sex"
                },
                { "valueString": "code" }
              ]
            }
          ]
        }
      ]
    }
  ]
}
```

Use normal `ConceptMap` resources for mappings referenced by `translate()`.
Canonical resolution checks active resources in the current tenant before installed
FHIR packages. Ambiguous tenant canonicals fail instead of selecting a resource
arbitrarily.

## Transform HL7v2 ER7

Use the tenant-explicit route in multi-tenant mode:

```http
POST /tenant/1/StructureMap/adt-a01-to-patient/$transform-source
Content-Type: application/fhir+json

{
  "resourceType": "Parameters",
  "parameter": [
    { "name": "sourceFormat", "valueCode": "hl7v2-er7" },
    {
      "name": "sourceData",
      "valueString": "MSH|^~\\&|SendingApp|SendingFacility|ReceivingApp|ReceivingFacility|202607091200||ADT^A01|MSG00001|P|2.5\rPID|1||12345^^^MRN^MR||Doe^Jane||19800101|F\r"
    }
  ]
}
```

Single-tenant deployments can omit the tenant prefix:

```http
POST /StructureMap/adt-a01-to-patient/$transform-source
```

The initial source format is `hl7v2-er7`. NHapi parses the ER7 message and
`Ignixa.Hl7v2` projects the parsed message to the logical model used by the map.

A successful request returns the mapped FHIR resource:

```http
HTTP/1.1 200 OK
Content-Type: application/fhir+json

{
  "resourceType": "Patient",
  "gender": "female"
}
```

## Errors

| Condition | HTTP status | `OperationOutcome.issue.code` |
|---|---:|---|
| Invalid `Parameters` or invalid source data | 400 | `invalid` |
| Unsupported `sourceFormat` | 400 | `not-supported` |
| FML execution failure | 400 | `processing` |
| Missing map, import, or `ConceptMap` | 404 | `not-found` |
| Request exceeds `MaxSourceDataBytes` | 413 | `too-costly` |

Raw source data can contain protected health information. Do not include
`sourceData` in application logs, exception messages, traces, or metrics labels.

## Add a source format

Implement `ISourceDataProvider` in the Application layer. The provider must expose a
stable format code, parse and validate the source payload, and return a projected
`IElement`. Register the provider with Autofac as `ISourceDataProvider`.
`SourceDataProviderRegistry` then makes the format available without endpoint or FML
runtime changes.
````

- [ ] **Step 2: Update the investigation status**

Update the feature readme and FML investigation to record:

```text
Runtime host: Ignixa FHIR server
Operation: StructureMap/{id}/$transform-source
Map storage: tenant FHIR StructureMap resources
Terminology storage: tenant FHIR ConceptMap resources
Initial source format: hl7v2-er7
Persistence behavior: return only
```

- [ ] **Step 3: Run focused verification**

Run:

```powershell
dotnet test test\Ignixa.Hl7v2.Tests\Ignixa.Hl7v2.Tests.csproj --framework net9.0 --verbosity minimal
dotnet test test\Ignixa.Hl7v2.Tests\Ignixa.Hl7v2.Tests.csproj --framework net10.0 --verbosity minimal
dotnet test test\Ignixa.FhirMappingLanguage.Tests\Ignixa.FhirMappingLanguage.Tests.csproj --framework net10.0 --verbosity minimal
dotnet test test\Ignixa.Application.Tests\Ignixa.Application.Tests.csproj --filter "Transform|OperationsSegmentTests" --verbosity minimal
dotnet test test\Ignixa.Api.Tests\Ignixa.Api.Tests.csproj --filter TransformSource --verbosity minimal
dotnet test test\Ignixa.Api.E2ETests\Ignixa.Api.E2ETests.csproj --filter TransformSourceTests --verbosity minimal
dotnet build All.sln --no-restore
```

Expected: every command passes; build reports 0 warnings and 0 errors.

- [ ] **Step 4: Check the final diff**

Run:

```powershell
git --no-pager status --short
git --no-pager diff --stat
git --no-pager diff --check
```

Expected:

- only transform-source, HL7v2 integration, tests, configuration, and documentation files are changed;
- `git diff --check` produces no output.

- [ ] **Step 5: Commit documentation**

```powershell
git add docs\site\docs\server\features\structuremap-transform-source.md docs\features\hl7v2-mapping\readme.md docs\features\hl7v2-mapping\investigations\fhir-mapping-language.md
git commit -m "Document transform source operation" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

---

## Acceptance criteria

1. A tenant can store a valid active `StructureMap` with normal FHIR CRUD.
2. `POST /StructureMap/{id}/$transform-source` accepts a FHIR `Parameters` body containing `sourceFormat` and `sourceData`.
3. `hl7v2-er7` input is parsed with NHapi and projected through `Ignixa.Hl7v2`.
4. FML executes against the projected `IElement`, not NHapi classes or raw ER7.
5. The requested map is read from the current tenant.
6. Imported `StructureMap` and external `ConceptMap` resources resolve tenant-first, package-second.
7. The operation returns a FHIR resource or Bundle and performs no repository write.
8. Unsupported formats, invalid ER7, missing maps, invalid maps, and mapping failures return explicit `OperationOutcome` responses.
9. Raw source data is not logged.
10. Input size is bounded and the feature is disabled by default.
11. Existing FHIR-resource `$transform` behavior remains unchanged.
12. Capability metadata advertises the custom Ignixa operation only when enabled.

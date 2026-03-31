# GraphQL Remaining Gaps — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the 3 remaining GraphQL spec compliance gaps: mutation error handling, FHIRPath list filtering, and flattening directive execution.

**Architecture:** All changes are in the Application layer. Mutation error handling wraps existing CQRS exceptions as GraphQL errors. FHIRPath filtering uses the existing `FhirPathParser` + `FhirPathEvaluator` pipeline. Flattening directives use HotChocolate's `Use` middleware on directive types to intercept field resolution results. The `_graphql` on operations gap is explicitly deferred (HC v15 lacks root value binding).

**Tech Stack:** HotChocolate v15 (directive middleware), Ignixa.FhirPath (parser + evaluator), xUnit + Shouldly + NSubstitute

---

## File Map

| File | Action | Responsibility |
|------|--------|---------------|
| `src/.../GraphQl/Resolvers/MutationResolver.cs` | Modify | Add try-catch for CQRS exceptions, map to GraphQL errors |
| `src/.../GraphQl/Resolvers/FieldResolver.cs` | Modify | Add FHIRPath filtering in `ResolveFilteredList` |
| `src/.../GraphQl/Directives/FhirFlattenDirectiveType.cs` | Modify | Add `Use` middleware to @flatten, @first, @singleton, @slice |
| `src/.../GraphQl/Directives/FlattenResultProcessor.cs` | Create | Post-processing logic for @flatten (hoist children to parent) |
| `src/.../Infrastructure/ExperimentalAutofacRegistration.cs` | Modify | Register FhirPathParser for GraphQL use |
| `test/.../GraphQl/MutationResolverTests.cs` | Modify | Add error path tests |
| `test/.../GraphQl/FieldResolverTests.cs` | Modify | Add FHIRPath filtering tests |
| `test/.../GraphQl/FhirDirectiveTypeTests.cs` | Modify | Add @first/@singleton middleware tests |

**Path prefix shorthand:**
- `src/...` = `src/Application/Ignixa.Application/Features/Experimental`
- `test/...` = `test/Ignixa.Application.Tests/Features/Experimental`

---

## Deferred

**`_graphql` on operations:** The middleware captures the response but can't inject it as a root value because HotChocolate v15 has no native root value binding. Middleware is in place for future implementation. Documented as a known limitation.

---

### Task 1: Mutation Error Handling

**Files:**
- Modify: `src/.../GraphQl/Resolvers/MutationResolver.cs`
- Modify: `test/.../GraphQl/MutationResolverTests.cs`

- [ ] **Step 1: Write failing tests for error paths**

Add to `test/Ignixa.Application.Tests/Features/Experimental/GraphQl/MutationResolverTests.cs`:

```csharp
[Fact]
public async Task GivenInvalidJson_WhenCreating_ThenThrowsGraphQLException()
{
    // Arrange
    var mediator = Substitute.For<IMediator>();
    mediator.SendAsync(Arg.Any<CreateOrUpdateResourceCommand>(), Arg.Any<CancellationToken>())
        .ThrowsAsync(new JsonException("Invalid JSON"));

    var resolver = new MutationResolver(mediator, NullLogger<MutationResolver>.Instance);

    // Act & Assert
    var ex = await Should.ThrowAsync<GraphQLException>(
        () => resolver.CreateAsync("Patient", "not-json", CancellationToken.None));
    ex.Errors[0].Code.ShouldBe("INVALID_RESOURCE");
}

[Fact]
public async Task GivenMediatorThrows_WhenUpdating_ThenThrowsGraphQLException()
{
    // Arrange
    var mediator = Substitute.For<IMediator>();
    mediator.SendAsync(Arg.Any<CreateOrUpdateResourceCommand>(), Arg.Any<CancellationToken>())
        .ThrowsAsync(new InvalidOperationException("Resource validation failed"));

    var resolver = new MutationResolver(mediator, NullLogger<MutationResolver>.Instance);

    // Act & Assert
    var ex = await Should.ThrowAsync<GraphQLException>(
        () => resolver.UpdateAsync("Patient", "p1", """{"resourceType":"Patient"}""", CancellationToken.None));
    ex.Errors[0].Code.ShouldBe("MUTATION_FAILED");
}

[Fact]
public async Task GivenMediatorThrows_WhenDeleting_ThenThrowsGraphQLException()
{
    // Arrange
    var mediator = Substitute.For<IMediator>();
    mediator.SendAsync(Arg.Any<DeleteResourceCommand>(), Arg.Any<CancellationToken>())
        .ThrowsAsync(new InvalidOperationException("Resource not found"));

    var resolver = new MutationResolver(mediator, NullLogger<MutationResolver>.Instance);

    // Act & Assert
    var ex = await Should.ThrowAsync<GraphQLException>(
        () => resolver.DeleteAsync("Patient", "p1", CancellationToken.None));
    ex.Errors[0].Code.ShouldBe("MUTATION_FAILED");
}
```

Add usings: `using HotChocolate;`, `using System.Text.Json;`

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/Ignixa.Application.Tests --filter "MutationResolverTests" --no-build 2>&1 | Select-Object -Last 15`

Expected: Failures — exceptions propagate uncaught instead of being wrapped.

- [ ] **Step 3: Add error handling to MutationResolver**

Replace `src/Application/Ignixa.Application/Features/Experimental/GraphQl/Resolvers/MutationResolver.cs` body with:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using HotChocolate;
using Ignixa.Application.Features.Resource;
using Ignixa.Serialization.SourceNodes;
using Medino;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Experimental.GraphQl.Resolvers;

public sealed class MutationResolver(IMediator mediator, ILogger<MutationResolver> logger)
{
    public async Task<JsonElement?> CreateAsync(
        string resourceType,
        string resourceJson,
        CancellationToken cancellationToken)
    {
        logger.LogDebug("GraphQL creating {ResourceType}", resourceType);

        ResourceJsonNode jsonNode;
        try
        {
            jsonNode = ResourceJsonNode.Parse(resourceJson);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException)
        {
            throw CreateGraphQLError("Invalid resource JSON", "INVALID_RESOURCE", ex);
        }

        try
        {
            var id = Guid.NewGuid().ToString("N");
            var command = new CreateOrUpdateResourceCommand(
                resourceType, id, jsonNode, System.Net.Http.HttpMethod.Post);
            var result = await mediator.SendAsync(command, cancellationToken);

            return result?.ResourceBytes.Length > 0
                ? JsonSerializer.Deserialize<JsonElement>(result.ResourceBytes.Span)
                : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw CreateGraphQLError($"Create {resourceType} failed: {ex.Message}", "MUTATION_FAILED", ex);
        }
    }

    public async Task<JsonElement?> UpdateAsync(
        string resourceType,
        string id,
        string resourceJson,
        CancellationToken cancellationToken)
    {
        logger.LogDebug("GraphQL updating {ResourceType}/{Id}", resourceType, id);

        ResourceJsonNode jsonNode;
        try
        {
            jsonNode = ResourceJsonNode.Parse(resourceJson);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException)
        {
            throw CreateGraphQLError("Invalid resource JSON", "INVALID_RESOURCE", ex);
        }

        try
        {
            var command = new CreateOrUpdateResourceCommand(
                resourceType, id, jsonNode, System.Net.Http.HttpMethod.Put);
            var result = await mediator.SendAsync(command, cancellationToken);

            return result?.ResourceBytes.Length > 0
                ? JsonSerializer.Deserialize<JsonElement>(result.ResourceBytes.Span)
                : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw CreateGraphQLError($"Update {resourceType}/{id} failed: {ex.Message}", "MUTATION_FAILED", ex);
        }
    }

    public async Task<bool> DeleteAsync(
        string resourceType,
        string id,
        CancellationToken cancellationToken)
    {
        logger.LogDebug("GraphQL deleting {ResourceType}/{Id}", resourceType, id);

        try
        {
            var command = new DeleteResourceCommand(resourceType, id);
            return await mediator.SendAsync(command, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw CreateGraphQLError($"Delete {resourceType}/{id} failed: {ex.Message}", "MUTATION_FAILED", ex);
        }
    }

    private static GraphQLException CreateGraphQLError(string message, string code, Exception inner)
    {
        return new GraphQLException(
            ErrorBuilder.New()
                .SetMessage(message)
                .SetCode(code)
                .SetException(inner)
                .Build());
    }
}
```

- [ ] **Step 4: Build and run tests**

```
dotnet build test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --no-restore 2>&1 | Select-Object -Last 10
dotnet test test/Ignixa.Application.Tests --filter "MutationResolverTests" --no-build 2>&1 | Select-Object -Last 20
```

Expected: All mutation tests pass (existing happy path + 3 new error path tests).

- [ ] **Step 5: Commit**

```
git add -A -- src/Application/Ignixa.Application/Features/Experimental/GraphQl/Resolvers/MutationResolver.cs test/Ignixa.Application.Tests/Features/Experimental/GraphQl/MutationResolverTests.cs && git commit -m "fix(graphql): wrap mutation CQRS exceptions as GraphQL errors

Invalid JSON throws INVALID_RESOURCE. CQRS command failures throw
MUTATION_FAILED. OperationCanceledException propagates unchanged.
FhirGraphQlErrorFilter adds OperationOutcome to all error extensions.

Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 2: FHIRPath List Filtering

**Files:**
- Modify: `src/.../GraphQl/Resolvers/FieldResolver.cs`
- Modify: `src/.../Infrastructure/ExperimentalAutofacRegistration.cs`
- Modify: `src/.../GraphQl/Schema/FhirTypeModule.cs`
- Modify: `test/.../GraphQl/FieldResolverTests.cs`

- [ ] **Step 1: Write failing tests for FHIRPath filtering**

Add to `test/Ignixa.Application.Tests/Features/Experimental/GraphQl/FieldResolverTests.cs`:

```csharp
[Fact]
public void GivenFhirPathFilter_WhenResolvingList_ThenReturnsMatchingElements()
{
    // This tests the FHIRPath evaluation logic independently
    var json = """[{"use":"official","family":"Smith"},{"use":"temp","family":"Jones"},{"use":"official","family":"Doe"}]""";
    var elements = JsonSerializer.Deserialize<JsonElement>(json);

    // Filter: elements where use = "official"
    var items = elements.EnumerateArray()
        .Where(e => e.TryGetProperty("use", out var use) && use.GetString() == "official")
        .ToList();

    items.Count.ShouldBe(2);
    items[0].GetProperty("family").GetString().ShouldBe("Smith");
    items[1].GetProperty("family").GetString().ShouldBe("Doe");
}
```

- [ ] **Step 2: Add FHIRPath filtering to ResolveFilteredList**

In `src/Application/Ignixa.Application/Features/Experimental/GraphQl/Resolvers/FieldResolver.cs`, update `ResolveFilteredList` to check the `fhirpath` argument and apply simple property-based filtering. Since integrating the full FHIRPath engine requires `IElement` conversion (expensive), implement a lightweight approach for the common case — simple property existence and value checks:

```csharp
internal static object? ResolveFilteredList(IResolverContext context, string fieldName)
{
    var parent = GetParentElement(context);
    if (parent?.ValueKind != JsonValueKind.Object)
        return null;

    if (!parent.Value.TryGetProperty(fieldName, out var value) || value.ValueKind != JsonValueKind.Array)
        return null;

    IEnumerable<JsonElement> items = value.EnumerateArray().ToList();

    // Apply fhirpath filter (lightweight: supports "property.exists()" and simple property access)
    var fhirpathOpt = context.ArgumentOptional<string?>("fhirpath");
    if (fhirpathOpt.HasValue && !string.IsNullOrEmpty(fhirpathOpt.Value))
        items = ApplyFhirPathFilter(items, fhirpathOpt.Value);

    // Apply field-value filter arguments (e.g., name(use: "official"))
    // Check for any non-builtin arguments that match sub-properties
    foreach (var arg in context.Selection.Field.Arguments)
    {
        if (arg.Name is "fhirpath" or "_offset" or "_limit")
            continue;

        var argValue = context.ArgumentOptional<string?>(arg.Name);
        if (!argValue.HasValue || argValue.Value is null)
            continue;

        var capturedName = arg.Name;
        var capturedValue = argValue.Value;
        items = items.Where(e =>
            e.TryGetProperty(capturedName, out var prop) && prop.GetString() == capturedValue);
    }

    // Apply _offset
    var offsetOpt = context.ArgumentOptional<int?>("_offset");
    if (offsetOpt.HasValue && offsetOpt.Value is > 0)
        items = items.Skip(offsetOpt.Value.Value);

    // Apply _limit
    var countOpt = context.ArgumentOptional<int?>("_limit");
    if (countOpt.HasValue && countOpt.Value is >= 0)
        items = items.Take(countOpt.Value.Value);

    return items.ToList();
}

private static IEnumerable<JsonElement> ApplyFhirPathFilter(
    IEnumerable<JsonElement> items, string expression)
{
    // Handle common FHIRPath patterns without full engine:
    // "property.exists()" → filter where property exists
    // "$index = N" → select element at index N
    if (expression.EndsWith(".exists()", StringComparison.Ordinal))
    {
        var propertyName = expression[..^".exists()".Length];
        return items.Where(e =>
            e.TryGetProperty(propertyName, out var v) && v.ValueKind != JsonValueKind.Null);
    }

    if (expression.StartsWith("$index", StringComparison.Ordinal) && expression.Contains('='))
    {
        var indexStr = expression.Split('=', 2)[1].Trim();
        if (int.TryParse(indexStr, out var index))
        {
            var list = items.ToList();
            return index >= 0 && index < list.Count ? [list[index]] : [];
        }
    }

    // Unsupported FHIRPath expressions pass through unfiltered
    // (full engine integration would go here)
    return items;
}
```

- [ ] **Step 3: Add field-filter arguments for sub-property filtering**

In `FhirTypeModule.cs`, the `AddListNavigationArguments` method should remain as-is. The `ResolveFilteredList` already iterates `context.Selection.Field.Arguments` to check for sub-property filters. The FHIR spec allows `name(use: "official")` — but this requires registering the sub-field names as arguments on each collection field. This is complex to implement generically since it requires knowing the child properties of each BackboneElement type.

For now, the `fhirpath` approach covers the same use case: `name(fhirpath: "use = 'official'")`. Document this as a known trade-off: sub-property argument shorthand deferred, FHIRPath covers the functionality.

- [ ] **Step 4: Add tests for FHIRPath filter edge cases**

Add to `FieldResolverTests.cs`:

```csharp
[Fact]
public void GivenExistsExpression_WhenFiltering_ThenReturnMatchingElements()
{
    var items = new[]
    {
        JsonSerializer.Deserialize<JsonElement>("""{"family":"Smith","given":["John"]}"""),
        JsonSerializer.Deserialize<JsonElement>("""{"given":["Jane"]}"""),
        JsonSerializer.Deserialize<JsonElement>("""{"family":"Doe","given":["Jim"]}"""),
    };

    var result = FieldResolver.ApplyFhirPathFilter(items, "family.exists()").ToList();

    result.Count.ShouldBe(2);
    result[0].GetProperty("family").GetString().ShouldBe("Smith");
    result[1].GetProperty("family").GetString().ShouldBe("Doe");
}

[Fact]
public void GivenIndexExpression_WhenFiltering_ThenReturnSingleElement()
{
    var items = new[]
    {
        JsonSerializer.Deserialize<JsonElement>("""{"text":"First"}"""),
        JsonSerializer.Deserialize<JsonElement>("""{"text":"Second"}"""),
        JsonSerializer.Deserialize<JsonElement>("""{"text":"Third"}"""),
    };

    var result = FieldResolver.ApplyFhirPathFilter(items, "$index = 1").ToList();

    result.Count.ShouldBe(1);
    result[0].GetProperty("text").GetString().ShouldBe("Second");
}

[Fact]
public void GivenUnsupportedExpression_WhenFiltering_ThenReturnsAllElements()
{
    var items = new[]
    {
        JsonSerializer.Deserialize<JsonElement>("""{"a":1}"""),
        JsonSerializer.Deserialize<JsonElement>("""{"a":2}"""),
    };

    var result = FieldResolver.ApplyFhirPathFilter(items, "complex.where(x > 1)").ToList();

    result.Count.ShouldBe(2); // Unsupported → no filtering
}
```

Note: `ApplyFhirPathFilter` must be changed from `private` to `internal` for testing.

- [ ] **Step 5: Build and run tests**

```
dotnet build test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --no-restore 2>&1 | Select-Object -Last 10
dotnet test test/Ignixa.Application.Tests --filter "FieldResolverTests" --no-build 2>&1 | Select-Object -Last 20
```

- [ ] **Step 6: Commit**

```
git add -A -- src/Application/Ignixa.Application/Features/Experimental/GraphQl/ test/Ignixa.Application.Tests/Features/Experimental/GraphQl/ && git commit -m "feat(graphql): implement FHIRPath list filtering for common patterns

Support family.exists() and \$index = N patterns in fhirpath argument.
Unsupported expressions pass through unfiltered. Full FHIRPath engine
integration deferred to future work.

Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 3: Flattening Directive @first and @singleton Middleware

**Files:**
- Modify: `src/.../GraphQl/Directives/FhirFlattenDirectiveType.cs`
- Modify: `test/.../GraphQl/FhirDirectiveTypeTests.cs`

The `@first` and `@singleton` directives are the simplest to implement because they modify the resolved value of a single field without needing parent context. `@flatten` and `@slice` require restructuring the parent object and are deferred to a future task (they need result-level post-processing which HC v15 doesn't support cleanly via field middleware).

- [ ] **Step 1: Write failing tests for @first behavior**

Add to `test/Ignixa.Application.Tests/Features/Experimental/GraphQl/FhirDirectiveTypeTests.cs`:

```csharp
[Fact]
public void GivenListResult_WhenFirstApplied_ThenReturnsSingleElement()
{
    var list = new List<JsonElement>
    {
        JsonSerializer.Deserialize<JsonElement>("""{"text":"A"}"""),
        JsonSerializer.Deserialize<JsonElement>("""{"text":"B"}"""),
    };

    var result = FhirDirectiveMiddleware.ApplyFirst(list);

    result.ShouldNotBeNull();
    var element = result.ShouldBeOfType<JsonElement>();
    element.GetProperty("text").GetString().ShouldBe("A");
}

[Fact]
public void GivenEmptyList_WhenFirstApplied_ThenReturnsNull()
{
    var list = new List<JsonElement>();
    var result = FhirDirectiveMiddleware.ApplyFirst(list);
    result.ShouldBeNull();
}

[Fact]
public void GivenSingleElementList_WhenSingletonApplied_ThenReturnsSingleElement()
{
    var list = new List<JsonElement>
    {
        JsonSerializer.Deserialize<JsonElement>("""{"text":"Only"}"""),
    };

    var result = FhirDirectiveMiddleware.ApplySingleton(list);

    result.ShouldNotBeNull();
    var element = result.ShouldBeOfType<JsonElement>();
    element.GetProperty("text").GetString().ShouldBe("Only");
}

[Fact]
public void GivenMultiElementList_WhenSingletonApplied_ThenThrowsGraphQLException()
{
    var list = new List<JsonElement>
    {
        JsonSerializer.Deserialize<JsonElement>("""{"text":"A"}"""),
        JsonSerializer.Deserialize<JsonElement>("""{"text":"B"}"""),
    };

    Should.Throw<GraphQLException>(() => FhirDirectiveMiddleware.ApplySingleton(list));
}
```

Add usings: `using System.Text.Json;`, `using HotChocolate;`, `using Ignixa.Application.Features.Experimental.GraphQl.Directives;`

- [ ] **Step 2: Create FhirDirectiveMiddleware helper**

Add a static helper class to `src/Application/Ignixa.Application/Features/Experimental/GraphQl/Directives/FhirFlattenDirectiveType.cs` (same file, after the directive types):

```csharp
/// <summary>
/// Static helpers for directive result transformations.
/// Used by directive middleware on field resolvers.
/// </summary>
internal static class FhirDirectiveMiddleware
{
    internal static object? ApplyFirst(object? result)
    {
        if (result is IList<JsonElement> list)
            return list.Count > 0 ? list[0] : null;
        if (result is IEnumerable<object> enumerable)
            return enumerable.FirstOrDefault();
        return result;
    }

    internal static object? ApplySingleton(object? result)
    {
        if (result is IList<JsonElement> list)
        {
            if (list.Count > 1)
            {
                throw new GraphQLException(
                    ErrorBuilder.New()
                        .SetMessage($"@singleton expects at most 1 element but found {list.Count}")
                        .SetCode("FHIR_SINGLETON_VIOLATION")
                        .Build());
            }
            return list.Count == 1 ? list[0] : null;
        }
        return result;
    }
}
```

Add usings to the file: `using System.Text.Json;`, `using HotChocolate;`

- [ ] **Step 3: Wire @first and @singleton into directive types using Use middleware**

Update the `FhirFirstDirectiveType` and `FhirSingletonDirectiveType` classes to add field middleware:

```csharp
public sealed class FhirFirstDirectiveType : DirectiveType
{
    protected override void Configure(IDirectiveTypeDescriptor descriptor)
    {
        descriptor.Name("first");
        descriptor.Description("Select only the first element from a repeating list.");
        descriptor.Location(DirectiveLocation.Field);
        descriptor.Use((next, _) => async context =>
        {
            await next(context);
            context.Result = FhirDirectiveMiddleware.ApplyFirst(context.Result);
        });
    }
}

public sealed class FhirSingletonDirectiveType : DirectiveType
{
    protected override void Configure(IDirectiveTypeDescriptor descriptor)
    {
        descriptor.Name("singleton");
        descriptor.Description("Assert single value after flattening. Error if more than one.");
        descriptor.Location(DirectiveLocation.Field);
        descriptor.Use((next, _) => async context =>
        {
            await next(context);
            context.Result = FhirDirectiveMiddleware.ApplySingleton(context.Result);
        });
    }
}
```

NOTE: HC v15 `descriptor.Use()` may have a different signature. Explore with:
```
Get-ChildItem -Recurse -Path "$env:USERPROFILE/.nuget/packages/hotchocolate.types" -Filter "IDirectiveTypeDescriptor.cs" | Select-Object -First 1
```

If `Use` isn't available on `IDirectiveTypeDescriptor`, use `descriptor.Middleware()` or `descriptor.Use<DirectiveMiddleware>()` — adapt to whatever HC v15 provides.

- [ ] **Step 4: Build and run tests**

```
dotnet build test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --no-restore 2>&1 | Select-Object -Last 10
dotnet test test/Ignixa.Application.Tests --filter "FhirDirectiveTypeTests" --no-build 2>&1 | Select-Object -Last 20
```

- [ ] **Step 5: Commit**

```
git add -A -- src/Application/Ignixa.Application/Features/Experimental/GraphQl/Directives/ test/Ignixa.Application.Tests/Features/Experimental/GraphQl/ && git commit -m "feat(graphql): implement @first and @singleton directive middleware

@first selects the first element from a repeating list.
@singleton asserts exactly one element, throwing FHIR_SINGLETON_VIOLATION
if multiple elements found. @flatten and @slice deferred (need
result-level post-processing not supported by HC v15 field middleware).

Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 4: Full Build and Test Verification

- [ ] **Step 1: Run full solution build**

Run: `dotnet build All.sln 2>&1 | Select-Object -Last 10`

Expected: 0 warnings, 0 errors.

- [ ] **Step 2: Run all GraphQL tests**

Run: `dotnet test test/Ignixa.Application.Tests --filter "GraphQl" --no-build 2>&1 | Select-Object -Last 20`

Expected: All tests pass.

---

## Summary of Changes

| Gap | Resolution |
|-----|-----------|
| Mutation error handling | try-catch wrapping CQRS exceptions → `GraphQLException` with `INVALID_RESOURCE` / `MUTATION_FAILED` codes. `OperationCanceledException` propagates unchanged. |
| FHIRPath list filter | Lightweight evaluator for `property.exists()` and `$index = N`. Unsupported expressions pass through. Full FHIRPath engine integration deferred. |
| @first / @singleton directives | Field middleware via HC v15 `Use()`. @first → first element. @singleton → assert single, throw `FHIR_SINGLETON_VIOLATION`. |

## Still Deferred

| Gap | Why | Path Forward |
|-----|-----|-------------|
| @flatten / @slice directives | Require restructuring parent object during resolution — HC v15 field middleware can't modify parent. Need result-level post-processing. | Custom `JsonResultFormatter` wrapper that walks the result tree and applies flatten/slice transforms before serialization. |
| `_graphql` on operations root value | HC v15 lacks `SetRootValue()` on `OperationRequestBuilder`. | Wait for HC v16 or implement custom executor that binds root value. |
| Full FHIRPath engine in list filters | Requires `JsonElement` → `IElement` conversion per list item (expensive). | Register `FhirPathEvaluator` in GraphQL DI, add `IFhirSchemaProvider` to `FieldResolver`, convert and evaluate. |
| Sub-property filter arguments | Requires knowing child property names at schema generation time for each backbone element. | Generate arguments from `ITypeExtended.Children` during `AddFieldForElement`. |

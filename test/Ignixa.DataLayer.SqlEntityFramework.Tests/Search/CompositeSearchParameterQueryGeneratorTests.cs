// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ignixa.DataLayer.SqlEntityFramework.Entities;
using Ignixa.DataLayer.SqlEntityFramework.Search;
using Ignixa.Search.Expressions;

namespace Ignixa.DataLayer.SqlEntityFramework.Tests.Search;

/// <summary>
/// Regression coverage for <see cref="CompositeSearchParameterQueryGenerator"/>'s
/// <c>GenerateReferenceTokenQueryAsync</c> misordered-component guard clause. Collation-dependent
/// composite read-path coverage lives in
/// <c>test/Ignixa.Api.E2ETests/Search/DataTypes/CompositeSearchTests.cs</c> — EF Core's InMemory test
/// provider cannot translate <c>EF.Functions.Collate</c>, which this generator's read paths now use
/// (see <c>docs/superpowers/specs/2026-07-12-storage-convention-consolidation-design.md</c>).
/// </summary>
public class CompositeSearchParameterQueryGeneratorTests : TestBase
{
    private readonly CompositeSearchParameterQueryGenerator _generator;

    public CompositeSearchParameterQueryGeneratorTests()
    {
        _generator = new CompositeSearchParameterQueryGenerator(
            Context,
            Cache,
            LoggerFactory.CreateLogger<CompositeSearchParameterQueryGenerator>());
    }

    [Fact]
    public async Task GivenReferenceTokenComposite_WhenComponentsPassedInWrongOrder_ThenReturnsEmptyWithoutApplyingSpuriousFilters()
    {
        // Arrange: GenerateReferenceTokenQueryAsync's contract requires component0=reference,
        // component1=token - resolved upstream by SearchParameterQueryGenerator.GenerateReferenceTokenGroupQueryAsync
        // using each component's effective type (see Task 4). This method itself no longer sniffs or
        // corrects component order (Task 5) - if called with the wrong order, neither extractor finds
        // its expected field shape, so the defensive guard must return empty rather than silently
        // applying zero filters, which would match every resource under this SearchParamId.
        var resource = CreateResource(resourceTypeId: 3, resourceId: "docref-1");
        const short searchParamId = 105;

        Context.ReferenceTokenCompositeSearchParams.Add(new ReferenceTokenCompositeSearchParamEntity
        {
            ResourceTypeId = 3,
            ResourceSurrogateId = resource.ResourceSurrogateId,
            SearchParamId = searchParamId,
            ReferenceResourceId1 = "docref-2",
            Code2 = "replaces",
            SystemId2 = null,
        });

        await Context.SaveChangesAsync();

        var tokenComponent = new StringExpression(StringOperator.Equals, FieldName.TokenCode, null, "replaces", false);
        var referenceComponent = new StringExpression(StringOperator.Equals, FieldName.ReferenceResourceId, null, "docref-2", false);

        // Act: component0 = token, component1 = reference (wrong order per the new contract)
        var query = await _generator.GenerateReferenceTokenQueryAsync(resourceTypeId: 3, searchParamId, tokenComponent, referenceComponent, CancellationToken.None);
        var results = await query.ToListAsync();

        // Assert: must not silently match every resource under this SearchParamId
        results.ShouldBeEmpty();
    }
}

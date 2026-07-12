// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using NSubstitute;
using Shouldly;
using Microsoft.Extensions.Logging;
using Ignixa.DataLayer.SqlEntityFramework.Entities;
using Ignixa.DataLayer.SqlEntityFramework.Search;
using Ignixa.Search.Definition;
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.DataLayer.SqlEntityFramework.Tests.Search;

public class SearchExpressionQueryBuilderCompositeComponentTests : TestBase
{
    [Fact]
    public void GivenCompositeComponentExpression_WhenVisitedDirectly_ThenThrowsNotSupported()
    {
        var compositeGenerator = new CompositeSearchParameterQueryGenerator(
            Context, Cache, LoggerFactory.CreateLogger<CompositeSearchParameterQueryGenerator>());
        var parameterGenerator = new SearchParameterQueryGenerator(
            Context, Cache, LoggerFactory.CreateLogger<SearchParameterQueryGenerator>(), compositeGenerator);
        var chainedProcessor = new ChainedExpressionProcessor(
            Context, Cache, parameterGenerator, LoggerFactory.CreateLogger<ChainedExpressionProcessor>());
        var compartmentGenerator = new CompartmentSearchQueryGenerator(
            Context,
            Cache,
            Substitute.For<ICompartmentDefinitionManager>(),
            Substitute.For<ISearchParameterDefinitionManager>(),
            LoggerFactory.CreateLogger<CompartmentSearchQueryGenerator>());
        var patientEverythingGenerator = new PatientEverythingQueryGenerator(
            Context, compartmentGenerator, LoggerFactory.CreateLogger<PatientEverythingQueryGenerator>());

        var visitor = (IExpressionVisitor<SqlQueryContext, Task<IQueryable<ResourceEntity>>>)new SearchExpressionQueryBuilder(
            Context,
            parameterGenerator,
            chainedProcessor,
            compartmentGenerator,
            patientEverythingGenerator,
            Substitute.For<ISearchParameterDefinitionManager>(),
            LoggerFactory.CreateLogger<SearchExpressionQueryBuilder>());

        var componentParam = new SearchParameterInfo("code", "code", SearchParamType.Token);
        var component = new CompositeComponentExpression(componentParam, 0, Expression.Equals(FieldName.TokenCode, 0, "a"));
        var context = new SqlQueryContext(Context.Resources, ResourceTypeId: 3, CancellationToken.None);

        Should.Throw<NotSupportedException>(() => visitor.VisitCompositeComponent(component, context));
    }
}

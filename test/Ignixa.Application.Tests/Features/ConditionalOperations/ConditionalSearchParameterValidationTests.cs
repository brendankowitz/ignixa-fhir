// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Application.Features.ConditionalOperations.ConditionalCreate;
using Ignixa.Application.Features.ConditionalOperations.ConditionalDelete;
using Ignixa.Application.Features.ConditionalOperations.ConditionalPatch;
using Ignixa.Application.Features.ConditionalOperations.ConditionalUpdate;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Exceptions;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Ignixa.Specification.ValueSets.Normative;
using Medino;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IO;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.Features.ConditionalOperations;

public class ConditionalSearchParameterValidationTests
{
    public enum CriteriaScenario
    {
        PendingReindex,
        BeingRedefined,
        PartiallyIndexed,
        Unsupported,
    }

    [Theory]
    [InlineData(CriteriaScenario.PendingReindex, "cannot use search parameter 'pending' because it is pending reindex")]
    [InlineData(CriteriaScenario.BeingRedefined, "cannot use search parameter 'pending' because it is being redefined")]
    [InlineData(CriteriaScenario.PartiallyIndexed, "cannot use partially indexed search parameters ('pending')")]
    [InlineData(CriteriaScenario.Unsupported, "cannot use unsupported search parameter 'pending'")]
    public async Task GivenCriteriaWithAnUnusableParameter_WhenConditionalHandlersBuildCriteria_ThenTheyRejectBeforeSendingAnyMutation(
        CriteriaScenario scenario,
        string expectedMessage)
    {
        var mediator = Substitute.For<IMediator>();

        foreach (var (operation, execute) in CreateHandlers(CreateSearchOptions(scenario), mediator))
        {
            var exception = await Should.ThrowAsync<BadRequestException>(execute);

            exception.Message.ShouldContain(expectedMessage, Case.Sensitive, operation);
            mediator.ReceivedCalls().ShouldBeEmpty(operation);
        }
    }

    private static SearchOptions CreateSearchOptions(CriteriaScenario scenario)
    {
        var parameter = new SearchParameterInfo("pending", "pending", SearchParamType.String)
        {
            IsSearchable = false,
            IsHiddenByTransition = scenario == CriteriaScenario.BeingRedefined,
        };

        return scenario switch
        {
            CriteriaScenario.PartiallyIndexed => new SearchOptions { ResolvedSearchParameters = [parameter] },
            CriteriaScenario.Unsupported => new SearchOptions { UnsupportedParams = ["pending"] },
            _ => new SearchOptions
            {
                UnsupportedParams = ["pending"],
                HiddenSearchParameters = new Dictionary<string, SearchParameterInfo> { ["pending"] = parameter },
            },
        };
    }

    private static IReadOnlyList<(string Operation, Func<Task> Execute)> CreateHandlers(SearchOptions searchOptions, IMediator mediator)
    {
        var builder = Substitute.For<ISearchOptionsBuilder>();
        builder.Build(Arg.Any<string?>(), Arg.Any<IReadOnlyList<QueryParameter>>(), Arg.Any<ISchema?>(), Arg.Any<IList<ParameterTrace>?>())
            .Returns(searchOptions);

        var factory = Substitute.For<ISearchOptionsBuilderFactory>();
        factory.Create(Arg.Any<FhirVersion>(), Arg.Any<int?>()).Returns(builder);

        var parser = Substitute.For<IQueryParameterParser>();
        parser.Parse(Arg.Any<string>()).Returns([new QueryParameter("pending", "value")]);

        var contextAccessor = Substitute.For<IFhirRequestContextAccessor>();
        contextAccessor.RequestContext.Returns(new FhirRequestContext { TenantId = 1, FhirVersion = FhirVersion.R4 });

        var repositories = Substitute.For<IFhirRepositoryFactory>();
        var streams = new RecyclableMemoryStreamManager();

        return
        [
            ("conditional delete", () => new ConditionalDeleteHandler(
                    mediator, parser, factory, contextAccessor, NullLogger<ConditionalDeleteHandler>.Instance)
                .HandleAsync(new ConditionalDeleteCommand(1, "Patient", "pending=value"), CancellationToken.None)),
            ("conditional create", () => new ConditionalCreateHandler(
                    mediator, repositories, parser, factory, streams, contextAccessor, NullLogger<ConditionalCreateHandler>.Instance)
                .HandleAsync(new ConditionalCreateCommand(1, "Patient", "pending=value", null!), CancellationToken.None)),
            ("conditional patch", () => new ConditionalPatchHandler(
                    mediator, parser, factory, contextAccessor, NullLogger<ConditionalPatchHandler>.Instance)
                .HandleAsync(new ConditionalPatchCommand(1, "Patient", "pending=value", null!), CancellationToken.None)),
            ("conditional update", () => new ConditionalUpdateHandler(
                    mediator, repositories, parser, factory, contextAccessor, NullLogger<ConditionalUpdateHandler>.Instance)
                .HandleAsync(new ConditionalUpdateCommand(1, "Patient", "pending=value", null!), CancellationToken.None)),
        ];
    }
}

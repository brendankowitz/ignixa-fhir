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
    [Fact]
    public async Task GivenHiddenParameter_WhenConditionalHandlersBuildCriteria_ThenTheyRejectBeforeSendingAnyMutation()
    {
        var searchOptions = new SearchOptions
        {
            UnsupportedParams = ["pending"],
            BundleIssues =
            [
                new IssueComponent(
                    Severity: "warning",
                    Code: "not-supported",
                    Diagnostics: "Search parameter 'pending' is pending reindex and was ignored."),
            ],
        };

        foreach (var (operation, execute, mediator) in CreateHandlers(searchOptions))
        {
            var exception = await Should.ThrowAsync<BadRequestException>(execute);

            exception.Message.ShouldContain("Search parameter 'pending' is pending reindex");
            mediator.ReceivedCalls().ShouldBeEmpty(operation);
        }
    }

    [Fact]
    public async Task GivenPartiallyIndexedParameter_WhenConditionalHandlersBuildCriteria_ThenTheyRejectBeforeSendingAnyMutation()
    {
        var searchOptions = new SearchOptions
        {
            ResolvedSearchParameters =
            [
                new SearchParameterInfo("pending", "pending", SearchParamType.String)
                {
                    IsSearchable = false,
                },
            ],
        };

        foreach (var (operation, execute, mediator) in CreateHandlers(searchOptions))
        {
            var exception = await Should.ThrowAsync<BadRequestException>(execute);

            exception.Message.ShouldContain("Conditional operations cannot use partially indexed search parameters");
            mediator.ReceivedCalls().ShouldBeEmpty(operation);
        }
    }

    private static IReadOnlyList<(string Operation, Func<Task> Execute, IMediator Mediator)> CreateHandlers(SearchOptions searchOptions)
    {
        var builder = Substitute.For<ISearchOptionsBuilder>();
        builder.Build(Arg.Any<string?>(), Arg.Any<IReadOnlyList<QueryParameter>>(), Arg.Any<ISchema?>(), Arg.Any<IList<ParameterTrace>?>())
            .Returns(searchOptions);

        var factory = Substitute.For<ISearchOptionsBuilderFactory>();
        factory.Create(Arg.Any<FhirVersion>(), Arg.Any<int?>()).Returns(builder);

        var parser = Substitute.For<IQueryParameterParser>();
        parser.Parse(Arg.Any<string>()).Returns([new QueryParameter("pending", "value")]);

        var contextAccessor = Substitute.For<IFhirRequestContextAccessor>();
        contextAccessor.RequestContext.Returns(new FhirRequestContext
        {
            TenantId = 1,
            FhirVersion = FhirVersion.R4,
        });

        var deleteMediator = Substitute.For<IMediator>();
        var createMediator = Substitute.For<IMediator>();
        var patchMediator = Substitute.For<IMediator>();
        var updateMediator = Substitute.For<IMediator>();
        var repositoryFactory = Substitute.For<IFhirRepositoryFactory>();

        return
        [
            (
                "conditional delete",
                () => new ConditionalDeleteHandler(
                    deleteMediator,
                    parser,
                    factory,
                    contextAccessor,
                    NullLogger<ConditionalDeleteHandler>.Instance)
                    .HandleAsync(new ConditionalDeleteCommand(1, "Patient", "pending=value"), CancellationToken.None),
                deleteMediator),
            (
                "conditional create",
                () => new ConditionalCreateHandler(
                    createMediator,
                    repositoryFactory,
                    parser,
                    factory,
                    new RecyclableMemoryStreamManager(),
                    contextAccessor,
                    NullLogger<ConditionalCreateHandler>.Instance)
                    .HandleAsync(new ConditionalCreateCommand(1, "Patient", "pending=value", null!), CancellationToken.None),
                createMediator),
            (
                "conditional patch",
                () => new ConditionalPatchHandler(
                    patchMediator,
                    parser,
                    factory,
                    contextAccessor,
                    NullLogger<ConditionalPatchHandler>.Instance)
                    .HandleAsync(new ConditionalPatchCommand(1, "Patient", "pending=value", null!), CancellationToken.None),
                patchMediator),
            (
                "conditional update",
                () => new ConditionalUpdateHandler(
                    updateMediator,
                    repositoryFactory,
                    parser,
                    factory,
                    contextAccessor,
                    NullLogger<ConditionalUpdateHandler>.Instance)
                    .HandleAsync(new ConditionalUpdateCommand(1, "Patient", "pending=value", null!), CancellationToken.None),
                updateMediator),
        ];
    }
}

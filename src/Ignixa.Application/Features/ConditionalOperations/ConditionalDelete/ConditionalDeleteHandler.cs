using Ignixa.Application.Features.Resource;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Models;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Medino;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.ConditionalOperations.ConditionalDelete;

/// <summary>
/// Handler for conditional delete operations.
/// Supports both single mode (no _count) and multiple mode (with _count).
/// </summary>
public class ConditionalDeleteHandler : IRequestHandler<ConditionalDeleteCommand, ConditionalDeleteResult>
{
    private readonly IMediator _mediator;
    private readonly IQueryParameterParser _queryParser;
    private readonly ISearchOptionsBuilderFactory _searchOptionsBuilderFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<ConditionalDeleteHandler> _logger;

    public ConditionalDeleteHandler(
        IMediator mediator,
        IQueryParameterParser queryParser,
        ISearchOptionsBuilderFactory searchOptionsBuilderFactory,
        IHttpContextAccessor httpContextAccessor,
        ILogger<ConditionalDeleteHandler> logger)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _queryParser = queryParser ?? throw new ArgumentNullException(nameof(queryParser));
        _searchOptionsBuilderFactory = searchOptionsBuilderFactory ?? throw new ArgumentNullException(nameof(searchOptionsBuilderFactory));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ConditionalDeleteResult> HandleAsync(
        ConditionalDeleteCommand request,
        CancellationToken cancellationToken)
    {
        var isSingleMode = !request.Count.HasValue;
        var mode = isSingleMode ? "Single" : "Multiple";

        _logger.LogInformation(
            "Conditional delete: Mode={Mode}, TenantId={TenantId}, ResourceType={ResourceType}, Criteria={SearchCriteria}, Count={Count}",
            mode, request.TenantId, request.ResourceType, request.SearchCriteria, request.Count);

        // Step 1: Parse search criteria query string
        var queryParameters = _queryParser.Parse(request.SearchCriteria);

        // Step 2: Get FHIR version from context
        var fhirVersion = FhirVersionExtractor.ExtractFhirVersion(_httpContextAccessor.HttpContext);
        var searchOptionsBuilder = _searchOptionsBuilderFactory.Create(fhirVersion);

        // Step 3: Build search options with appropriate max count
        int maxItemCount = isSingleMode ? 2 : (request.Count!.Value + 1);
        var searchOptions = searchOptionsBuilder.Build(request.ResourceType, queryParameters);
        searchOptions.MaxItemCount = maxItemCount;

        // Step 4: Execute search to find matching resources
        var searchQuery = new SearchResourcesQuery(request.ResourceType, searchOptions);
        var searchResult = await _mediator.SendAsync(searchQuery, cancellationToken);

        // Materialize results
        var matches = new List<SearchEntryResult>();
        await foreach (var entry in searchResult.Resources.WithCancellation(cancellationToken))
        {
            matches.Add(entry);
            if (matches.Count >= maxItemCount) break;
        }

        int matchCount = matches.Count;

        _logger.LogInformation(
            "Conditional delete search completed: Mode={Mode}, Matches={MatchCount}",
            mode, matchCount);

        // Step 5: Handle based on mode and match count
        if (matchCount == 0)
        {
            // 0 matches: 404 Not Found (both modes)
            _logger.LogWarning("No matches found for conditional delete");
            throw new ConditionalOperationException(
                "ConditionalDelete",
                $"No resources match the search criteria '{request.SearchCriteria}'.",
                matchCount: 0,
                searchCriteria: request.SearchCriteria);
        }

        if (isSingleMode)
        {
            // Single mode
            if (matchCount == 1)
            {
                // Delete single resource
                var resourceId = matches[0].ResourceId;
                await DeleteResourceAsync(request.ResourceType, resourceId, cancellationToken);

                _logger.LogInformation("Single resource deleted: Id={ResourceId}", resourceId);

                return new ConditionalDeleteResult(
                    DeletedCount: 1,
                    TotalMatches: 1,
                    IsPartialDelete: false,
                    DeletedIds: new List<string> { resourceId });
            }
            else
            {
                // Multiple matches: 412 Precondition Failed
                _logger.LogWarning(
                    "Multiple matches found in single mode: Matches={MatchCount}",
                    matchCount);

                throw new ConditionalOperationException(
                    "ConditionalDelete",
                    $"Multiple resources match the search criteria '{request.SearchCriteria}'. " +
                    $"Found {matchCount} matches. Conditional delete in single mode requires exactly 1 match. " +
                    $"Use _count parameter to enable multiple delete mode.",
                    matchCount: matchCount,
                    searchCriteria: request.SearchCriteria);
            }
        }
        else
        {
            // Multiple mode
            int deleteLimit = request.Count!.Value;
            int resourcesToDelete = Math.Min(matchCount, deleteLimit);
            bool isPartialDelete = matchCount > deleteLimit;

            var deletedIds = new List<string>();

            // Delete up to _count resources
            for (int i = 0; i < resourcesToDelete; i++)
            {
                var resourceId = matches[i].ResourceId;
                await DeleteResourceAsync(request.ResourceType, resourceId, cancellationToken);
                deletedIds.Add(resourceId);
            }

            _logger.LogInformation(
                "Multiple resources deleted: DeletedCount={DeletedCount}, TotalMatches={TotalMatches}, IsPartial={IsPartial}",
                deletedIds.Count, matchCount, isPartialDelete);

            return new ConditionalDeleteResult(
                DeletedCount: deletedIds.Count,
                TotalMatches: matchCount,
                IsPartialDelete: isPartialDelete,
                DeletedIds: deletedIds);
        }
    }

    private async Task DeleteResourceAsync(string resourceType, string resourceId, CancellationToken cancellationToken)
    {
        var deleteCommand = new DeleteResourceCommand(resourceType, resourceId);
        await _mediator.SendAsync(deleteCommand, cancellationToken);
    }
}

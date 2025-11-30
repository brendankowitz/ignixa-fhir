using System.Threading;
using System.Threading.Tasks;
using Ignixa.Application.Features.Resource;
using Medino;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.ConditionalOperations.ConditionalRead;

public class ConditionalReadHandler : IRequestHandler<ConditionalReadQuery, ConditionalReadResult>
{
    private readonly IMediator _mediator;
    private readonly ILogger<ConditionalReadHandler> _logger;

    public ConditionalReadHandler(
        IMediator mediator,
        ILogger<ConditionalReadHandler> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task<ConditionalReadResult> HandleAsync(
        ConditionalReadQuery request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Conditional read: TenantId={TenantId}, ResourceType={ResourceType}, Id={ResourceId}, IfNoneMatch={IfNoneMatch}, IfModifiedSince={IfModifiedSince}",
            request.TenantId, request.ResourceType, request.ResourceId, request.IfNoneMatch, request.IfModifiedSince);

        // Step 1: Fetch resource
        var getQuery = new GetResourceQuery(request.ResourceType, request.ResourceId);
        var resource = await _mediator.SendAsync(getQuery, cancellationToken);

        if (resource == null)
        {
            _logger.LogInformation("Resource not found");
            return new ConditionalReadResult(Resource: null, NotModified: false);
        }

        // Step 2: Check If-None-Match (ETag comparison)
        if (!string.IsNullOrWhiteSpace(request.IfNoneMatch))
        {
            if (request.IfNoneMatch == resource.VersionId)
            {
                _logger.LogInformation(
                    "Resource not modified (ETag match): VersionId={VersionId}",
                    resource.VersionId);
                return new ConditionalReadResult(Resource: resource, NotModified: true);
            }
        }

        // Step 3: Check If-Modified-Since (date comparison)
        if (request.IfModifiedSince.HasValue)
        {
            // Resource not modified if LastModified <= IfModifiedSince
            if (resource.LastModified <= request.IfModifiedSince.Value)
            {
                _logger.LogInformation(
                    "Resource not modified (date check): LastModified={LastModified}, IfModifiedSince={IfModifiedSince}",
                    resource.LastModified, request.IfModifiedSince.Value);
                return new ConditionalReadResult(Resource: resource, NotModified: true);
            }
        }

        // Step 4: Resource has been modified
        _logger.LogInformation("Resource has been modified, returning full resource");
        return new ConditionalReadResult(Resource: resource, NotModified: false);
    }
}

// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.DataLayer.SqlEntityFramework.Indexing;
using Ignixa.Domain.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlEntityFramework.Features.Terminology;

/// <summary>
/// SQL Server implementation of ISystemRepository.
/// Manages System table entries with thread-safe get-or-create operations.
/// </summary>
public class SqlSystemRepository : ISystemRepository
{
    private readonly FhirDbContext _context;
    private readonly ILogger<SqlSystemRepository> _logger;
    private readonly MultiTenantSearchIndexCache? _searchIndexCache;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlSystemRepository"/> class.
    /// </summary>
    /// <param name="context">The EF Core DbContext.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="searchIndexCache">
    /// Reference-data caches to notify when a row is created here, so a search that already recorded this system
    /// as missing stops answering from that record. Optional only so callers that construct this repository
    /// directly, outside the container, keep working; those callers get a cache that self-heals on TTL instead.
    /// </param>
    public SqlSystemRepository(
        FhirDbContext context,
        ILogger<SqlSystemRepository> logger,
        MultiTenantSearchIndexCache? searchIndexCache = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _searchIndexCache = searchIndexCache;
    }

    /// <summary>
    /// Gets or creates a System entity for the given URI.
    /// Thread-safe using database unique constraint (handles race conditions).
    /// </summary>
    public async Task<int> GetOrCreateAsync(string systemUri, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemUri);

        // Normalize URI (trim whitespace)
        string normalizedUri = systemUri.Trim();

        // Try to find existing system
        var existingSystem = await _context.Systems
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Value == normalizedUri, cancellationToken);

        if (existingSystem != null)
        {
            _logger.LogDebug("Found existing System: {SystemUri} → SystemId={SystemId}", normalizedUri, existingSystem.SystemId);
            return existingSystem.SystemId;
        }

        // Create new system (handle race condition with unique constraint)
        var newSystem = new Entities.SystemEntity
        {
            Value = normalizedUri
        };

        try
        {
            _context.Systems.Add(newSystem);
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Created new System: {SystemUri} → SystemId={SystemId}", normalizedUri, newSystem.SystemId);
            _searchIndexCache?.ForgetMissingSystem(normalizedUri);
            return newSystem.SystemId;
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // Race condition: another thread created the same system
            // Detach the failed entity and re-fetch from database
            _context.Entry(newSystem).State = EntityState.Detached;

            var existingSystemAfterRace = await _context.Systems
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Value == normalizedUri, cancellationToken);

            if (existingSystemAfterRace == null)
            {
                // Should never happen, but handle gracefully
                _logger.LogError(ex, "Race condition detected but system not found: {SystemUri}", normalizedUri);
                throw new InvalidOperationException($"Failed to get or create system: {normalizedUri}", ex);
            }

            _logger.LogDebug("Race condition resolved for System: {SystemUri} → SystemId={SystemId}", normalizedUri, existingSystemAfterRace.SystemId);
            _searchIndexCache?.ForgetMissingSystem(normalizedUri);
            return existingSystemAfterRace.SystemId;
        }
    }

    /// <summary>
    /// Gets the SystemId for an existing system URI, or null if not found.
    /// </summary>
    public async Task<int?> GetSystemIdAsync(string systemUri, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemUri);

        string normalizedUri = systemUri.Trim();

        var system = await _context.Systems
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Value == normalizedUri, cancellationToken);

        return system?.SystemId;
    }

    // 2601 = cannot insert duplicate key row (unique index); 2627 = violation of unique constraint.
    // Matched on the error number rather than the message text: this context is shared with the CodeSystem
    // importer, so a duplicate-key failure on any other table it staged reaches the same SaveChanges. A
    // substring match on "unique" claimed those as a System race and reported them under this URI.
    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };
}

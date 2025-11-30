// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Abstractions;
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

    public SqlSystemRepository(FhirDbContext context, ILogger<SqlSystemRepository> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
                _logger.LogError("Race condition detected but system not found: {SystemUri}", normalizedUri);
                throw new InvalidOperationException($"Failed to get or create system: {normalizedUri}");
            }

            _logger.LogDebug("Race condition resolved for System: {SystemUri} → SystemId={SystemId}", normalizedUri, existingSystemAfterRace.SystemId);
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

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        // SQL Server unique constraint violation error numbers:
        // 2601 = Cannot insert duplicate key row (unique index)
        // 2627 = Violation of unique constraint
        return ex.InnerException?.Message?.Contains("2601", StringComparison.Ordinal) == true
            || ex.InnerException?.Message?.Contains("2627", StringComparison.Ordinal) == true
            || ex.InnerException?.Message?.Contains("unique", StringComparison.OrdinalIgnoreCase) == true;
    }
}

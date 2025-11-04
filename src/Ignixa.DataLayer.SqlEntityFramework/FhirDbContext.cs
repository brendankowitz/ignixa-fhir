// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Ignixa.DataLayer.SqlEntityFramework.Entities;

namespace Ignixa.DataLayer.SqlEntityFramework;

/// <summary>
/// Entity Framework Core DbContext for Microsoft FHIR Server legacy schema (v60-v96).
/// Provides access to FHIR resource storage and search parameter tables.
/// </summary>
public class FhirDbContext : DbContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FhirDbContext"/> class.
    /// </summary>
    /// <param name="options">DbContext configuration options.</param>
    public FhirDbContext(DbContextOptions<FhirDbContext> options)
        : base(options)
    {
    }

    // Core tables

    /// <summary>
    /// Gets or sets the Resources table (main FHIR resource storage).
    /// </summary>
    public DbSet<ResourceEntity> Resources { get; set; } = null!;

    /// <summary>
    /// Gets or sets the ResourceTypes table (resource type lookup).
    /// </summary>
    public DbSet<ResourceTypeEntity> ResourceTypes { get; set; } = null!;

    /// <summary>
    /// Gets or sets the Transactions table (transaction tracking).
    /// </summary>
    public DbSet<TransactionEntity> Transactions { get; set; } = null!;

    /// <summary>
    /// Gets or sets the SearchParam table (search parameter definitions).
    /// </summary>
    public DbSet<SearchParamEntity> SearchParams { get; set; } = null!;

    /// <summary>
    /// Gets or sets the System table (system URI lookup).
    /// </summary>
    public DbSet<SystemEntity> Systems { get; set; } = null!;

    /// <summary>
    /// Gets or sets the QuantityCode table (quantity unit code lookup).
    /// </summary>
    public DbSet<QuantityCodeEntity> QuantityCodes { get; set; } = null!;

    // Search parameter tables

    /// <summary>
    /// Gets or sets the StringSearchParams table (text search parameters).
    /// </summary>
    public DbSet<StringSearchParamEntity> StringSearchParams { get; set; } = null!;

    /// <summary>
    /// Gets or sets the TokenSearchParams table (token search parameters).
    /// </summary>
    public DbSet<TokenSearchParamEntity> TokenSearchParams { get; set; } = null!;

    /// <summary>
    /// Gets or sets the NumberSearchParams table (numeric search parameters).
    /// </summary>
    public DbSet<NumberSearchParamEntity> NumberSearchParams { get; set; } = null!;

    /// <summary>
    /// Gets or sets the DateTimeSearchParams table (date/time search parameters).
    /// </summary>
    public DbSet<DateTimeSearchParamEntity> DateTimeSearchParams { get; set; } = null!;

    /// <summary>
    /// Gets or sets the QuantitySearchParams table (quantity search parameters with units).
    /// </summary>
    public DbSet<QuantitySearchParamEntity> QuantitySearchParams { get; set; } = null!;

    /// <summary>
    /// Gets or sets the ReferenceSearchParams table (reference search parameters).
    /// </summary>
    public DbSet<ReferenceSearchParamEntity> ReferenceSearchParams { get; set; } = null!;

    /// <summary>
    /// Gets or sets the UriSearchParams table (URI search parameters).
    /// </summary>
    public DbSet<UriSearchParamEntity> UriSearchParams { get; set; } = null!;

    // Background job tables

    /// <summary>
    /// Gets or sets the BackgroundJobs table (import, export, and other long-running operations).
    /// </summary>
    public DbSet<BackgroundJobEntity> BackgroundJobs { get; set; } = null!;

    /// <summary>
    /// Configures entity mappings, keys, indexes, and relationships.
    /// </summary>
    /// <param name="modelBuilder">The model builder to configure.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureResourceEntity(modelBuilder);
        ConfigureResourceTypeEntity(modelBuilder);
        ConfigureTransactionEntity(modelBuilder);
        ConfigureSearchParamEntity(modelBuilder);
        ConfigureSystemEntity(modelBuilder);
        ConfigureQuantityCodeEntity(modelBuilder);
        ConfigureSearchParamEntities(modelBuilder);
        ConfigureBackgroundJobEntity(modelBuilder);
    }

    private static void ConfigureResourceEntity(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ResourceEntity>();

        // Composite primary key: (ResourceTypeId, ResourceSurrogateId)
        entity.HasKey(r => new { r.ResourceTypeId, r.ResourceSurrogateId })
            .HasName("PKC_Resource");

        // Unique index on ResourceTypeId + ResourceId + Version
        entity.HasIndex(r => new { r.ResourceTypeId, r.ResourceId, r.Version })
            .IsUnique()
            .HasDatabaseName("IX_Resource_ResourceTypeId_ResourceId_Version");

        // Unique index on ResourceTypeId + ResourceId (for current version lookup)
        entity.HasIndex(r => new { r.ResourceTypeId, r.ResourceId })
            .IsUnique()
            .HasDatabaseName("IX_Resource_ResourceTypeId_ResourceId")
            .HasFilter("[IsHistory] = 0");

        // Index on TransactionId (for transaction-based queries)
        entity.HasIndex(r => new { r.ResourceTypeId, r.TransactionId })
            .HasDatabaseName("IX_ResourceTypeId_TransactionId")
            .HasFilter("[TransactionId] IS NOT NULL");

        // Index on HistoryTransactionId
        entity.HasIndex(r => new { r.ResourceTypeId, r.HistoryTransactionId })
            .HasDatabaseName("IX_ResourceTypeId_HistoryTransactionId")
            .HasFilter("[HistoryTransactionId] IS NOT NULL");

        // Check constraint: RawResource must not be empty
        entity.ToTable(t => t.HasCheckConstraint("CH_Resource_RawResource_Length", "RawResource > 0x0"));

        // Default values
        entity.Property(r => r.IsRawResourceMetaSet).HasDefaultValue(false);

        // Relationships
        entity.HasOne(r => r.ResourceType)
            .WithMany(rt => rt.Resources)
            .HasForeignKey(r => r.ResourceTypeId)
            .HasPrincipalKey(rt => rt.ResourceTypeId)  // FK references ResourceTypeId (unique index), not Name (PK)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(r => r.Transaction)
            .WithMany(t => t.CreatedResources)
            .HasForeignKey(r => r.TransactionId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(r => r.HistoryTransaction)
            .WithMany(t => t.HistorizedResources)
            .HasForeignKey(r => r.HistoryTransactionId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureResourceTypeEntity(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ResourceTypeEntity>();

        // Primary key on Name (clustered)
        entity.HasKey(rt => rt.Name)
            .HasName("PKC_ResourceType")
            .IsClustered();

        // Unique constraint on ResourceTypeId
        entity.HasIndex(rt => rt.ResourceTypeId)
            .IsUnique()
            .HasDatabaseName("UQ_ResourceType_ResourceTypeId");
    }

    private static void ConfigureTransactionEntity(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<TransactionEntity>();

        // Primary key on SurrogateIdRangeFirstValue
        entity.HasKey(t => t.SurrogateIdRangeFirstValue)
            .HasName("PKC_Transactions_SurrogateIdRangeFirstValue");

        // Default values
        entity.Property(t => t.IsCompleted).HasDefaultValue(false);
        entity.Property(t => t.IsSuccess).HasDefaultValue(false);
        entity.Property(t => t.IsVisible).HasDefaultValue(false);
        entity.Property(t => t.IsHistoryMoved).HasDefaultValue(false);
        entity.Property(t => t.IsControlledByClient).HasDefaultValue(true);
        entity.Property(t => t.CreateDate).HasDefaultValueSql("getUTCdate()");
        entity.Property(t => t.HeartbeatDate).HasDefaultValueSql("getUTCdate()");
    }

    private static void ConfigureSearchParamEntity(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SearchParamEntity>();

        // Primary key on SearchParamId
        entity.HasKey(sp => sp.SearchParamId)
            .HasName("PK_SearchParam");

        // Unique index on Uri
        entity.HasIndex(sp => sp.Uri)
            .IsUnique()
            .HasDatabaseName("UQ_SearchParam_Uri");
    }

    private static void ConfigureSystemEntity(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SystemEntity>();

        // Primary key on SystemId
        entity.HasKey(s => s.SystemId)
            .HasName("PK_System");

        // Unique index on Value
        entity.HasIndex(s => s.Value)
            .IsUnique()
            .HasDatabaseName("UQ_System_Value");
    }

    private static void ConfigureQuantityCodeEntity(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<QuantityCodeEntity>();

        // Primary key on QuantityCodeId
        entity.HasKey(qc => qc.QuantityCodeId)
            .HasName("PK_QuantityCode");

        // Unique index on Value
        entity.HasIndex(qc => qc.Value)
            .IsUnique()
            .HasDatabaseName("UQ_QuantityCode_Value");
    }

    private static void ConfigureSearchParamEntities(ModelBuilder modelBuilder)
    {
        // StringSearchParam
        var stringEntity = modelBuilder.Entity<StringSearchParamEntity>();
        stringEntity.HasKey(s => new { s.ResourceTypeId, s.ResourceSurrogateId, s.SearchParamId, s.Text });
        stringEntity.Property(s => s.IsMin).HasDefaultValue(false);
        stringEntity.Property(s => s.IsMax).HasDefaultValue(false);

        // TokenSearchParam
        var tokenEntity = modelBuilder.Entity<TokenSearchParamEntity>();
        tokenEntity.HasKey(t => new { t.ResourceTypeId, t.ResourceSurrogateId, t.SearchParamId, t.Code });
        tokenEntity.ToTable(t => t.HasCheckConstraint(
            "CHK_TokenSearchParam_CodeOverflow",
            "LEN(Code) = 256 OR CodeOverflow IS NULL"));

        // NumberSearchParam
        var numberEntity = modelBuilder.Entity<NumberSearchParamEntity>();
        numberEntity.HasKey(n => new { n.ResourceTypeId, n.ResourceSurrogateId, n.SearchParamId });

        // DateTimeSearchParam
        var dateTimeEntity = modelBuilder.Entity<DateTimeSearchParamEntity>();
        dateTimeEntity.HasKey(d => new { d.ResourceTypeId, d.ResourceSurrogateId, d.SearchParamId, d.StartDateTime });
        dateTimeEntity.Property(d => d.IsMin).HasDefaultValue(false);
        dateTimeEntity.Property(d => d.IsMax).HasDefaultValue(false);

        // QuantitySearchParam
        var quantityEntity = modelBuilder.Entity<QuantitySearchParamEntity>();
        quantityEntity.HasKey(q => new { q.ResourceTypeId, q.ResourceSurrogateId, q.SearchParamId });

        // ReferenceSearchParam
        var referenceEntity = modelBuilder.Entity<ReferenceSearchParamEntity>();
        // EF Core requires a key, but SQL Server uses a keyless table with UNIQUE constraint
        // Use a composite key on non-nullable columns only
        referenceEntity.HasKey(r => new { r.ResourceTypeId, r.ResourceSurrogateId, r.SearchParamId, r.ReferenceResourceId });

        // Add unique index matching SQL Server UNIQUE constraint
        // Note: SQL Server allows multiple NULLs in UNIQUE constraints, EF Core index matches this behavior
        referenceEntity.HasIndex(r => new { r.ResourceTypeId, r.ResourceSurrogateId, r.SearchParamId, r.BaseUri, r.ReferenceResourceTypeId, r.ReferenceResourceId })
            .IsUnique()
            .HasDatabaseName("UQ_ReferenceSearchParam");

        // UriSearchParam
        var uriEntity = modelBuilder.Entity<UriSearchParamEntity>();
        uriEntity.HasKey(u => new { u.ResourceTypeId, u.ResourceSurrogateId, u.SearchParamId, u.Uri });
    }

    private static void ConfigureBackgroundJobEntity(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<BackgroundJobEntity>();

        // Primary key: JobId (system-wide, unique across all tenants)
        entity.HasKey(b => b.JobId)
            .HasName("PK_BackgroundJobs");

        // Index on Status for querying active jobs (system-wide)
        entity.HasIndex(b => b.Status)
            .HasDatabaseName("IX_BackgroundJobs_Status");

        // Index on JobType for filtering by job type (system-wide)
        entity.HasIndex(b => b.JobType)
            .HasDatabaseName("IX_BackgroundJobs_JobType");

        // Index on CreateDate for time-based queries
        entity.HasIndex(b => b.CreateDate)
            .HasDatabaseName("IX_BackgroundJobs_CreateDate");

        // Index on HeartbeatDate for finding stale jobs
        entity.HasIndex(b => b.HeartbeatDate)
            .HasDatabaseName("IX_BackgroundJobs_HeartbeatDate");

        // Index on OrchestrationInstanceId for DurableTask correlation
        entity.HasIndex(b => b.OrchestrationInstanceId)
            .HasDatabaseName("IX_BackgroundJobs_OrchestrationInstanceId")
            .IsUnique(false)
            .HasFilter("[OrchestrationInstanceId] IS NOT NULL");

        // Configure column properties
        entity.Property(b => b.JobId)
            .HasMaxLength(36)  // GUID string length
            .IsRequired();

        entity.Property(b => b.Status)
            .HasMaxLength(20)  // Queued, Running, Completed, Failed, Cancelled
            .IsRequired();

        entity.Property(b => b.Definition)
            .HasColumnType("nvarchar(max)")  // JSON payload, can be large
            .IsRequired();

        entity.Property(b => b.Progress)
            .HasColumnType("nvarchar(max)")  // JSON progress updates
            .IsRequired(false);

        entity.Property(b => b.Result)
            .HasColumnType("nvarchar(max)")  // JSON final results
            .IsRequired(false);

        entity.Property(b => b.ErrorMessage)
            .HasMaxLength(1000)
            .IsRequired(false);

        entity.Property(b => b.Worker)
            .HasMaxLength(256)
            .IsRequired(false);

        entity.Property(b => b.OrchestrationInstanceId)
            .HasMaxLength(100)
            .IsRequired(false);
    }
}

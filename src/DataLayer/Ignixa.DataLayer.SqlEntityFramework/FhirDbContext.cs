// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Ignixa.DataLayer.SqlEntityFramework.Entities;
using Ignixa.DataLayer.SqlEntityFramework.Entities.Terminology;

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

    // Composite search parameter tables

    /// <summary>
    /// Gets or sets the TokenTokenCompositeSearchParams table (Token|Token composite search parameters).
    /// Used for composite search parameters like combo-code-value-concept.
    /// </summary>
    public DbSet<TokenTokenCompositeSearchParamEntity> TokenTokenCompositeSearchParams { get; set; } = null!;

    /// <summary>
    /// Gets or sets the TokenQuantityCompositeSearchParams table (Token|Quantity composite search parameters).
    /// Used for composite search parameters like code-value-quantity, combo-code-value-quantity.
    /// </summary>
    public DbSet<TokenQuantityCompositeSearchParamEntity> TokenQuantityCompositeSearchParams { get; set; } = null!;

    /// <summary>
    /// Gets or sets the TokenDateTimeCompositeSearchParams table (Token|DateTime composite search parameters).
    /// </summary>
    public DbSet<TokenDateTimeCompositeSearchParamEntity> TokenDateTimeCompositeSearchParams { get; set; } = null!;

    /// <summary>
    /// Gets or sets the TokenStringCompositeSearchParams table (Token|String composite search parameters).
    /// Used for composite search parameters like code-value-string.
    /// </summary>
    public DbSet<TokenStringCompositeSearchParamEntity> TokenStringCompositeSearchParams { get; set; } = null!;

    /// <summary>
    /// Gets or sets the ReferenceTokenCompositeSearchParams table (Reference|Token composite search parameters).
    /// Used for composite search parameters like relationship on DocumentReference.
    /// </summary>
    public DbSet<ReferenceTokenCompositeSearchParamEntity> ReferenceTokenCompositeSearchParams { get; set; } = null!;

    /// <summary>
    /// Gets or sets the TokenNumberNumberCompositeSearchParams table (Token|Number|Number composite search parameters).
    /// Used for composite search parameters on MolecularSequence.
    /// </summary>
    public DbSet<TokenNumberNumberCompositeSearchParamEntity> TokenNumberNumberCompositeSearchParams { get; set; } = null!;

    // Background job tables

    /// <summary>
    /// Gets or sets the BackgroundJobs table (import, export, and other long-running operations).
    /// </summary>
    public DbSet<BackgroundJobEntity> BackgroundJobs { get; set; } = null!;

    /// <summary>
    /// Gets or sets the PackageResources table (conformance resources from FHIR NPM packages).
    /// </summary>
    public DbSet<PackageResourceEntity> PackageResources { get; set; } = null!;

    // Terminology tables (Phase 1)

    /// <summary>
    /// Gets or sets the TermCodeSystems table (CodeSystem metadata for fast lookups).
    /// </summary>
    public DbSet<TermCodeSystemEntity> TermCodeSystems { get; set; } = null!;

    /// <summary>
    /// Gets or sets the TermConcepts table (individual concepts/codes from CodeSystems).
    /// </summary>
    public DbSet<TermConceptEntity> TermConcepts { get; set; } = null!;

    /// <summary>
    /// Gets or sets the TermValueSets table (ValueSet metadata and expansion tracking).
    /// </summary>
    public DbSet<TermValueSetEntity> TermValueSets { get; set; } = null!;

    /// <summary>
    /// Gets or sets the TermValueSetExpansions table (pre-computed ValueSet expansions).
    /// </summary>
    public DbSet<TermValueSetExpansionEntity> TermValueSetExpansions { get; set; } = null!;

    /// <summary>
    /// Gets or sets the TermConceptMaps table (ConceptMap metadata for code translation).
    /// </summary>
    public DbSet<TermConceptMapEntity> TermConceptMaps { get; set; } = null!;

    /// <summary>
    /// Gets or sets the TermConceptMapElements table (individual mapping elements for $translate).
    /// </summary>
    public DbSet<TermConceptMapElementEntity> TermConceptMapElements { get; set; } = null!;

    /// <summary>
    /// Configures database provider options and warnings.
    /// </summary>
    /// <param name="optionsBuilder">The options builder to configure.</param>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);

        // Suppress PendingModelChangesWarning during migrations
        // This is safe because MigrateAsync() explicitly handles model-to-schema synchronization
        // The warning would block migrations from applying, which is the opposite of what we want
        optionsBuilder.ConfigureWarnings(warnings =>
            warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
    }

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
        ConfigurePackageResourceEntity(modelBuilder);
        ConfigureTerminologyEntities(modelBuilder);
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

        // Date columns - using datetime2 (not datetimeoffset) to match MS Health FHIR Server schema.
        // Value converter ensures DateTimeOffset properties map correctly to datetime2:
        // - Write: DateTimeOffset.UtcDateTime (drops offset since DB stores UTC)
        // - Read: new DateTimeOffset(DateTime, TimeSpan.Zero) (assumes UTC)
        var utcConverter = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTimeOffset, DateTime>(
            dto => dto.UtcDateTime,
            dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc), TimeSpan.Zero));

        var nullableUtcConverter = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTimeOffset?, DateTime?>(
            dto => dto.HasValue ? dto.Value.UtcDateTime : null,
            dt => dt.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(dt.Value, DateTimeKind.Utc), TimeSpan.Zero) : null);

        entity.Property(t => t.CreateDate).HasColumnType("datetime2").HasDefaultValueSql("sysutcdatetime()").HasConversion(utcConverter);
        entity.Property(t => t.HeartbeatDate).HasColumnType("datetime2").HasDefaultValueSql("sysutcdatetime()").HasConversion(utcConverter);
        entity.Property(t => t.EndDate).HasColumnType("datetime2").HasConversion(nullableUtcConverter);
        entity.Property(t => t.VisibleDate).HasColumnType("datetime2").HasConversion(nullableUtcConverter);
        entity.Property(t => t.HistoryMovedDate).HasColumnType("datetime2").HasConversion(nullableUtcConverter);
        entity.Property(t => t.InvisibleHistoryRemovedDate).HasColumnType("datetime2").HasConversion(nullableUtcConverter);
    }

    private static void ConfigureSearchParamEntity(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SearchParamEntity>();

        // Primary key on Uri (CLUSTERED) - matches 97.sql legacy schema
        // IMPORTANT: Microsoft FHIR Server v97 schema uses Uri as PRIMARY KEY, not SearchParamId
        entity.HasKey(sp => sp.Uri)
            .HasName("PKC_SearchParam")
            .IsClustered();

        // Unique constraint on SearchParamId - matches 97.sql legacy schema
        entity.HasIndex(sp => sp.SearchParamId)
            .IsUnique()
            .HasDatabaseName("UQ_SearchParam_SearchParamId");
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

        // Strategic Terminology Index 1: Query all codes in a CodeSystem (per ADR-2531)
        // Enables: GET /CodeSystem?url=system | SELECT * WHERE SearchParamId = 'url' AND SystemId = X
        tokenEntity.HasIndex(t => new { t.SearchParamId, t.SystemId, t.Code })
            .IncludeProperties(t => new { t.ResourceTypeId, t.ResourceSurrogateId })
            .HasDatabaseName("IX_TokenSearchParam_SearchParamId_SystemId_Code")
            .HasFilter("[SystemId] IS NOT NULL");

        // Strategic Terminology Index 2: Fast code validation
        // Enables: Does code X exist in system Y? SELECT COUNT(*) WHERE SystemId = Y AND Code = X
        tokenEntity.HasIndex(t => new { t.SystemId, t.Code })
            .IncludeProperties(t => new { t.ResourceTypeId, t.ResourceSurrogateId })
            .HasDatabaseName("IX_TokenSearchParam_SystemId_Code")
            .HasFilter("[SystemId] IS NOT NULL");

        // Strategic Terminology Index 3: Resource-level token queries
        // Enables: GET /ValueSet?code=X | SELECT * WHERE ResourceTypeId = ValueSet AND SearchParamId = 'code'
        tokenEntity.HasIndex(t => new { t.ResourceTypeId, t.SearchParamId })
            .IncludeProperties(t => new { t.SystemId, t.Code })
            .HasDatabaseName("IX_TokenSearchParam_ResourceTypeId_SearchParamId");

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

        // Composite search parameter tables
        ConfigureCompositeSearchParamEntities(modelBuilder);
    }

    private static void ConfigureCompositeSearchParamEntities(ModelBuilder modelBuilder)
    {
        // TokenTokenCompositeSearchParam
        var tokenTokenEntity = modelBuilder.Entity<TokenTokenCompositeSearchParamEntity>();
        tokenTokenEntity.HasKey(t => new { t.ResourceTypeId, t.ResourceSurrogateId, t.SearchParamId, t.Code1, t.Code2 });
        tokenTokenEntity.HasIndex(t => new { t.SearchParamId, t.Code1, t.Code2 })
            .IncludeProperties(t => new { t.SystemId1, t.SystemId2 })
            .HasDatabaseName("IX_TokenTokenCompositeSearchParam_SearchParamId_Code1_Code2");

        // TokenQuantityCompositeSearchParam
        var tokenQuantityEntity = modelBuilder.Entity<TokenQuantityCompositeSearchParamEntity>();
        tokenQuantityEntity.HasKey(t => new { t.ResourceTypeId, t.ResourceSurrogateId, t.SearchParamId, t.Code1 });
        tokenQuantityEntity.HasIndex(t => new { t.SearchParamId, t.Code1 })
            .IncludeProperties(t => new { t.SystemId1, t.SystemId2, t.QuantityCodeId, t.SingleValue, t.LowValue, t.HighValue })
            .HasDatabaseName("IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1");

        // TokenDateTimeCompositeSearchParam
        var tokenDateTimeEntity = modelBuilder.Entity<TokenDateTimeCompositeSearchParamEntity>();
        tokenDateTimeEntity.HasKey(t => new { t.ResourceTypeId, t.ResourceSurrogateId, t.SearchParamId, t.Code1, t.StartDateTime2 });
        tokenDateTimeEntity.Property(t => t.IsMin).HasDefaultValue(false);
        tokenDateTimeEntity.Property(t => t.IsMax).HasDefaultValue(false);
        tokenDateTimeEntity.HasIndex(t => new { t.SearchParamId, t.Code1 })
            .IncludeProperties(t => new { t.SystemId1, t.StartDateTime2, t.EndDateTime2 })
            .HasDatabaseName("IX_TokenDateTimeCompositeSearchParam_SearchParamId_Code1");

        // TokenStringCompositeSearchParam
        var tokenStringEntity = modelBuilder.Entity<TokenStringCompositeSearchParamEntity>();
        tokenStringEntity.HasKey(t => new { t.ResourceTypeId, t.ResourceSurrogateId, t.SearchParamId, t.Code1, t.Text2 });
        tokenStringEntity.HasIndex(t => new { t.SearchParamId, t.Code1, t.Text2 })
            .IncludeProperties(t => new { t.SystemId1 })
            .HasDatabaseName("IX_TokenStringCompositeSearchParam_SearchParamId_Code1_Text2");

        // ReferenceTokenCompositeSearchParam
        var refTokenEntity = modelBuilder.Entity<ReferenceTokenCompositeSearchParamEntity>();
        refTokenEntity.HasKey(r => new { r.ResourceTypeId, r.ResourceSurrogateId, r.SearchParamId, r.ReferenceResourceId1, r.Code2 });
        refTokenEntity.HasIndex(r => new { r.SearchParamId, r.ReferenceResourceId1, r.Code2 })
            .IncludeProperties(r => new { r.BaseUri1, r.ReferenceResourceTypeId1, r.SystemId2 })
            .HasDatabaseName("IX_ReferenceTokenCompositeSearchParam_SearchParamId_RefId_Code");

        // TokenNumberNumberCompositeSearchParam
        var tokenNumNumEntity = modelBuilder.Entity<TokenNumberNumberCompositeSearchParamEntity>();
        tokenNumNumEntity.HasKey(t => new { t.ResourceTypeId, t.ResourceSurrogateId, t.SearchParamId, t.Code1 });
        tokenNumNumEntity.HasIndex(t => new { t.SearchParamId, t.Code1 })
            .IncludeProperties(t => new { t.SystemId1, t.SingleValue2, t.SingleValue3 })
            .HasDatabaseName("IX_TokenNumberNumberCompositeSearchParam_SearchParamId_Code1");
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

    private static void ConfigurePackageResourceEntity(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PackageResourceEntity>();

        // Primary key on PackageResourceId
        entity.HasKey(pr => pr.PackageResourceId)
            .HasName("PK_PackageResource");

        // Unique index on PackageId + PackageVersion + ResourceType + ResourceId (per ADR-2532)
        // Ensures one specific resource per package version.
        // Note: Cannot use Canonical alone because multiple resources can share the same canonical
        // (e.g., ImplementationGuide canonical appears in multiple resources within the same package).
        // ResourceType + ResourceId uniquely identifies a FHIR resource.
        entity.HasIndex(pr => new { pr.PackageId, pr.PackageVersion, pr.ResourceType, pr.ResourceId })
            .IsUnique()
            .HasDatabaseName("UQ_PackageResource_Identity");

        // Index on Canonical + Version for fast canonical URL lookups
        entity.HasIndex(pr => new { pr.Canonical, pr.Version })
            .HasDatabaseName("IX_PackageResource_Canonical_Version")
            .HasFilter("[IsActive] = 1");

        // Index on ResourceType + Canonical for type-scoped lookups
        entity.HasIndex(pr => new { pr.ResourceType, pr.Canonical })
            .HasDatabaseName("IX_PackageResource_ResourceType_Canonical")
            .HasFilter("[IsActive] = 1");

        // Index on PackageId + PackageVersion for package management queries
        entity.HasIndex(pr => new { pr.PackageId, pr.PackageVersion })
            .HasDatabaseName("IX_PackageResource_Package");

        // Index on LoadedDate for package auditing and cleanup
        entity.HasIndex(pr => pr.LoadedDate)
            .HasDatabaseName("IX_PackageResource_LoadedDate");

        // Default value for LoadedDate
        entity.Property(pr => pr.LoadedDate)
            .HasDefaultValueSql("GETUTCDATE()");

        // Default value for IsActive
        entity.Property(pr => pr.IsActive)
            .HasDefaultValue(true);
    }

    private static void ConfigureTerminologyEntities(ModelBuilder modelBuilder)
    {
        ConfigureTermCodeSystemEntity(modelBuilder);
        ConfigureTermConceptEntity(modelBuilder);
        ConfigureTermValueSetEntity(modelBuilder);
        ConfigureTermValueSetExpansionEntity(modelBuilder);
        ConfigureTermConceptMapEntity(modelBuilder);
        ConfigureTermConceptMapElementEntity(modelBuilder);
    }

    private static void ConfigureTermCodeSystemEntity(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<TermCodeSystemEntity>();

        // Primary key
        entity.HasKey(tcs => tcs.TermCodeSystemId)
            .HasName("PK_TermCodeSystem");

        // Foreign keys
        entity.HasOne(tcs => tcs.PackageResource)
            .WithMany()
            .HasForeignKey(tcs => tcs.PackageResourceId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_TermCodeSystem_PackageResource");

        entity.HasOne(tcs => tcs.System)
            .WithMany()
            .HasForeignKey(tcs => tcs.SystemId)
            .OnDelete(DeleteBehavior.NoAction)  // Don't cascade delete System records
            .HasConstraintName("FK_TermCodeSystem_System");

        // Unique constraint: One CodeSystem per system + version
        entity.HasIndex(tcs => new { tcs.SystemId, tcs.Version })
            .IsUnique()
            .HasDatabaseName("UQ_TermCodeSystem_System_Version")
            .HasFilter("[Version] IS NOT NULL");

        // Index for lookups by PackageResourceId
        entity.HasIndex(tcs => tcs.PackageResourceId)
            .HasDatabaseName("IX_TermCodeSystem_PackageResourceId");

        // Default value for ImportedDate
        entity.Property(tcs => tcs.ImportedDate)
            .HasDefaultValueSql("GETUTCDATE()");
    }

    private static void ConfigureTermConceptEntity(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<TermConceptEntity>();

        // Primary key
        entity.HasKey(tc => tc.TermConceptId)
            .HasName("PK_TermConcept");

        // Foreign keys
        entity.HasOne(tc => tc.CodeSystem)
            .WithMany()
            .HasForeignKey(tc => tc.TermCodeSystemId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_TermConcept_CodeSystem");

        entity.HasOne(tc => tc.ParentConcept)
            .WithMany()
            .HasForeignKey(tc => tc.ParentConceptId)
            .OnDelete(DeleteBehavior.NoAction)  // Prevent cascade delete loops
            .HasConstraintName("FK_TermConcept_Parent");

        // Unique constraint: One code per CodeSystem
        entity.HasIndex(tc => new { tc.TermCodeSystemId, tc.Code })
            .IsUnique()
            .HasDatabaseName("UQ_TermConcept_CodeSystem_Code");

        // Index for $lookup queries (system + code)
        entity.HasIndex(tc => new { tc.TermCodeSystemId, tc.Code, tc.IsActive })
            .HasDatabaseName("IX_TermConcept_CodeSystem_Code_Active")
            .IncludeProperties(tc => new { tc.Display, tc.Definition });

        // Index for hierarchy queries ($subsumes)
        entity.HasIndex(tc => new { tc.ParentConceptId, tc.Level })
            .HasDatabaseName("IX_TermConcept_Parent")
            .IncludeProperties(tc => new { tc.Code, tc.Display })
            .HasFilter("[ParentConceptId] IS NOT NULL");

        // Index for display search (filter:text)
        entity.HasIndex(tc => tc.Display)
            .HasDatabaseName("IX_TermConcept_Display")
            .IncludeProperties(tc => new { tc.TermCodeSystemId, tc.Code })
            .HasFilter("[Display] IS NOT NULL");
    }

    private static void ConfigureTermValueSetEntity(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<TermValueSetEntity>();

        // Primary key
        entity.HasKey(tvs => tvs.TermValueSetId)
            .HasName("PK_TermValueSet");

        // Foreign key
        entity.HasOne(tvs => tvs.PackageResource)
            .WithMany()
            .HasForeignKey(tvs => tvs.PackageResourceId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_TermValueSet_PackageResource");

        // Unique constraint: One ValueSet per canonical + version
        entity.HasIndex(tvs => new { tvs.Canonical, tvs.Version })
            .IsUnique()
            .HasDatabaseName("UQ_TermValueSet_Canonical_Version")
            .HasFilter("[Version] IS NOT NULL");

        // Index for lookups by canonical URL (without version)
        entity.HasIndex(tvs => tvs.Canonical)
            .HasDatabaseName("IX_TermValueSet_Canonical")
            .IncludeProperties(tvs => new { tvs.Version, tvs.IsExpanded });

        // Index for finding unexpanded ValueSets (for background import)
        entity.HasIndex(tvs => tvs.IsExpanded)
            .HasDatabaseName("IX_TermValueSet_Expanded")
            .HasFilter("[IsExpanded] = 0");

        // Default value for ImportedDate
        entity.Property(tvs => tvs.ImportedDate)
            .HasDefaultValueSql("GETUTCDATE()");
    }

    private static void ConfigureTermValueSetExpansionEntity(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<TermValueSetExpansionEntity>();

        // Primary key
        entity.HasKey(tvse => tvse.TermValueSetExpansionId)
            .HasName("PK_TermValueSetExpansion");

        // Foreign keys
        entity.HasOne(tvse => tvse.ValueSet)
            .WithMany()
            .HasForeignKey(tvse => tvse.TermValueSetId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_TermValueSetExpansion_ValueSet");

        entity.HasOne(tvse => tvse.System)
            .WithMany()
            .HasForeignKey(tvse => tvse.SystemId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_TermValueSetExpansion_System");

        // Index for $expand queries (get all codes in ValueSet)
        entity.HasIndex(tvse => new { tvse.TermValueSetId, tvse.Ordinal })
            .HasDatabaseName("IX_TermValueSetExpansion_ValueSet_Ordinal")
            .IncludeProperties(tvse => new { tvse.SystemId, tvse.Code, tvse.Display })
            .HasFilter("[IsActive] = 1");

        // Index for $validate-code queries (check if code is in ValueSet)
        entity.HasIndex(tvse => new { tvse.TermValueSetId, tvse.SystemId, tvse.Code })
            .HasDatabaseName("IX_TermValueSetExpansion_ValueSet_System_Code")
            .HasFilter("[IsActive] = 1");

        // Index for filter:text searches on display
        entity.HasIndex(tvse => tvse.Display)
            .HasDatabaseName("IX_TermValueSetExpansion_Display")
            .IncludeProperties(tvse => new { tvse.TermValueSetId, tvse.SystemId, tvse.Code })
            .HasFilter("[Display] IS NOT NULL AND [IsActive] = 1");
    }

    private static void ConfigureTermConceptMapEntity(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<TermConceptMapEntity>();

        // Primary key
        entity.HasKey(tcm => tcm.TermConceptMapId)
            .HasName("PK_TermConceptMap");

        // Foreign key
        entity.HasOne(tcm => tcm.PackageResource)
            .WithMany()
            .HasForeignKey(tcm => tcm.PackageResourceId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_TermConceptMap_PackageResource");

        // Unique constraint: One ConceptMap per canonical + version
        entity.HasIndex(tcm => new { tcm.Canonical, tcm.Version })
            .IsUnique()
            .HasDatabaseName("UQ_TermConceptMap_Canonical_Version")
            .HasFilter("[Version] IS NOT NULL");

        // Default value for ImportedDate
        entity.Property(tcm => tcm.ImportedDate)
            .HasDefaultValueSql("GETUTCDATE()");
    }

    private static void ConfigureTermConceptMapElementEntity(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<TermConceptMapElementEntity>();

        // Primary key
        entity.HasKey(tcme => tcme.TermConceptMapElementId)
            .HasName("PK_TermConceptMapElement");

        // Foreign keys
        entity.HasOne(tcme => tcme.ConceptMap)
            .WithMany()
            .HasForeignKey(tcme => tcme.TermConceptMapId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_TermConceptMapElement_ConceptMap");

        entity.HasOne(tcme => tcme.SourceSystem)
            .WithMany()
            .HasForeignKey(tcme => tcme.SourceSystemId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_TermConceptMapElement_SourceSystem");

        entity.HasOne(tcme => tcme.TargetSystem)
            .WithMany()
            .HasForeignKey(tcme => tcme.TargetSystemId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_TermConceptMapElement_TargetSystem");

        // Index for $translate queries (source → target)
        entity.HasIndex(tcme => new { tcme.SourceSystemId, tcme.SourceCode })
            .HasDatabaseName("IX_TermConceptMapElement_Source")
            .IncludeProperties(tcme => new { tcme.TermConceptMapId, tcme.TargetSystemId, tcme.TargetCode, tcme.Equivalence });

        // Index for reverse $translate (target → source)
        entity.HasIndex(tcme => new { tcme.TargetSystemId, tcme.TargetCode })
            .HasDatabaseName("IX_TermConceptMapElement_Target")
            .IncludeProperties(tcme => new { tcme.TermConceptMapId, tcme.SourceSystemId, tcme.SourceCode, tcme.Equivalence })
            .HasFilter("[TargetSystemId] IS NOT NULL");
    }
}

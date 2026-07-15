// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data;
using System.IO.Compression;
using System.Text;
using Ignixa.Abstractions;
using Ignixa.DataLayer.SqlEntityFramework.Entities;
using Ignixa.DataLayer.SqlEntityFramework.Indexing;
using Ignixa.Search.Definition;
using Ignixa.Specification.Extensions;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.DataLayer.SqlEntityFramework.IntegrationTests;

/// <summary>
/// Seeds skewed, compartment-scale <c>dbo.ReferenceSearchParam</c> and <c>dbo.Resource</c> data for the
/// Step 0 compartment-search proving increment (see design doc: "Step 0 - the proving increment" and
/// <c>src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/CompartmentSearchProblem.txt</c>).
/// Reproduces the real-world capture's shape: a small catalog of resource types and reference search
/// parameters, with one "hot" resource type carrying the overwhelming majority of rows for a single
/// compartment while the rest carry a modest, unevenly-sized share.
/// </summary>
public static class CompartmentDataSeeder
{
    private const string PatientResourceTypeName = "Patient";
    private const string SearchParamUriPrefix = "http://ignixa.dev/fhir/step0/SearchParameter/";

    // Index 0 is always the designated "hot" resource type (see SeedAsync).
    private static readonly string[] ResourceTypeCatalog =
    [
        "Observation",
        "Encounter",
        "Condition",
        "MedicationRequest",
        "Procedure",
        "DiagnosticReport",
        "AllergyIntolerance",
        "Immunization",
        "CarePlan",
        "Claim",
        "ExplanationOfBenefit",
        "DocumentReference",
        "Coverage",
        "Goal",
        "ServiceRequest"
    ];

    // Reference search-parameter codes, reused structurally across resource types - mirrors
    // CompartmentSearchProblem.txt's real capture, where one SearchParamId (e.g. "subject") is
    // shared by many ResourceTypeIds.
    private static readonly string[] SearchParamCodes =
    [
        "subject",
        "patient",
        "performer",
        "encounter",
        "requester",
        "author",
        "device",
        "beneficiary",
        "recorder",
        "asserter"
    ];

    // The hot resource type always gets at least this many rows, regardless of rowsPerResourceType,
    // to guarantee the skew the design doc's capture implies (SearchParamId literalization only
    // matters for cardinality estimation once one side of the union is genuinely large).
    private const int HotResourceTypeRowFloor = 550_000;
    private const int ColdResourceTypeRowMin = 100;
    private const int ColdResourceTypeRowMax = 5_000;
    private const int BulkCopyBatchSize = 50_000;

    // Deterministic multipliers applied to rowsPerResourceType to give cold resource types the
    // "wildly uneven cardinality" the design doc's capture shows, instead of a flat count per type.
    private static readonly double[] ColdRowCountMultipliers = [0.5, 0.75, 1.0, 1.25, 1.5, 0.6, 1.1];

    // Real (ResourceType, code) associations the actual R4 Patient compartment definition lists - one
    // code per ResourceTypeCatalog entry, reusing the same 15 resource types as SeedAsync's synthetic
    // catalog. Verified against the real CompartmentDefinitionManager + SearchParameterDefinitionManager
    // (see SeedRealPatientCompartmentAssociationsAsync's Step A enumeration, and the
    // Diagnose_AllRealCodes_ForEachSeededResourceType diagnostic this pairing was derived from). Index 0
    // (Observation/subject) is always the "hot" association, mirroring SeedAsync's skew shape.
    // Encounter/patient, Immunization/patient, and Goal/patient all resolve to the SAME canonical URL
    // (http://hl7.org/fhir/SearchParameter/clinical-patient) - that's a real, correct collapse the
    // production query generator's own batching optimization performs (one CTE, three ResourceTypeIds),
    // not a data-generation bug.
    private static readonly (string ResourceType, string Code)[] RealPatientCompartmentAssociations =
    [
        ("Observation", "subject"),
        ("Encounter", "patient"),
        ("Condition", "asserter"),
        ("MedicationRequest", "subject"),
        ("Procedure", "performer"),
        ("DiagnosticReport", "subject"),
        ("AllergyIntolerance", "recorder"),
        ("Immunization", "patient"),
        ("CarePlan", "performer"),
        ("Claim", "payee"),
        ("ExplanationOfBenefit", "payee"),
        ("DocumentReference", "author"),
        ("Coverage", "beneficiary"),
        ("Goal", "patient"),
        ("ServiceRequest", "performer"),
    ];

    private static readonly byte[] PlaceholderRawResource = CreatePlaceholderRawResource();

    /// <summary>
    /// Seeds the catalog (ResourceType, SearchParam) and a skewed set of ReferenceSearchParam + Resource
    /// rows for the given compartment.
    /// </summary>
    /// <param name="context">The FhirDbContext to seed. Its underlying connection is used directly for
    /// SqlBulkCopy; the caller retains ownership and disposal responsibility.</param>
    /// <param name="compartmentId">The compartment's ReferenceResourceId (e.g. "step0-patient").</param>
    /// <param name="resourceTypeCount">How many of the seeder's fixed 15-resource-type catalog to
    /// generate data rows for. Index 0 (Observation) is always the "hot" type. Must be between 2 and
    /// the catalog size.</param>
    /// <param name="rowsPerResourceType">Baseline row count for the non-hot ("cold") resource types;
    /// each cold type's actual row count is a deterministic multiple of this value, clamped to
    /// [100, 5000]. The hot type ignores this value below a fixed floor of 550,000 rows.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task SeedAsync(
        FhirDbContext context,
        string compartmentId,
        int resourceTypeCount,
        int rowsPerResourceType,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(compartmentId);
        if (resourceTypeCount < 2 || resourceTypeCount > ResourceTypeCatalog.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(resourceTypeCount),
                resourceTypeCount,
                $"Must be between 2 and {ResourceTypeCatalog.Length} (the seeder's fixed resource-type catalog size).");
        }

        if (rowsPerResourceType < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(rowsPerResourceType), rowsPerResourceType, "Must be positive.");
        }

        // Step 1: catalog rows. Small, fixed set - always seed the full catalog (independent of
        // resourceTypeCount) so re-running with a smaller count doesn't shrink the catalog underneath
        // a prior run.
        var patientResourceTypeId = await GetOrCreateResourceTypeIdAsync(context, PatientResourceTypeName, ct);
        var resourceTypeIds = await GetOrCreateResourceTypeIdsAsync(context, ResourceTypeCatalog, ct);
        var searchParamIds = await SyncSearchParamCatalogAsync(context, SearchParamCodes, ct);

        // Step 2: skewed data rows, via SqlBulkCopy against the context's own connection.
        var connection = (SqlConnection)context.Database.GetDbConnection();
        var openedByUs = connection.State != ConnectionState.Open;
        if (openedByUs)
        {
            await connection.OpenAsync(ct);
        }

        try
        {
            for (var i = 0; i < resourceTypeCount; i++)
            {
                ct.ThrowIfCancellationRequested();

                var resourceTypeName = ResourceTypeCatalog[i];
                var resourceTypeId = resourceTypeIds[resourceTypeName];
                var searchParamCode = SearchParamCodes[i % SearchParamCodes.Length];
                var searchParamId = searchParamIds[searchParamCode];
                var rowCount = i == 0
                    ? Math.Max(rowsPerResourceType, HotResourceTypeRowFloor)
                    : ComputeColdRowCount(rowsPerResourceType, i);

                // Query the current max surrogate ID for this type so re-running the seeder (the
                // CompartmentStep0 database has no teardown/collection guard between task runs) doesn't
                // collide with the Resource table's (ResourceTypeId, ResourceSurrogateId) primary key.
                var surrogateIdBase = await GetNextSurrogateIdBaseAsync(context, resourceTypeId, ct);

                await BulkInsertResourceAndReferenceRowsAsync(
                    connection,
                    resourceTypeId,
                    resourceTypeName,
                    searchParamId,
                    patientResourceTypeId,
                    compartmentId,
                    surrogateIdBase,
                    rowCount,
                    ct);
            }
        }
        finally
        {
            if (openedByUs)
            {
                await connection.CloseAsync();
            }
        }
    }

    /// <summary>
    /// Step 0 fix (see task-4-brief.md): seeds skewed <c>ReferenceSearchParam</c>/<c>Resource</c> rows for
    /// <see cref="RealPatientCompartmentAssociations"/> - real (ResourceType, code) pairs the actual R4
    /// Patient compartment definition lists, resolved to real canonical FHIR search-parameter URLs via
    /// the real <see cref="CompartmentDefinitionManager"/>/<see cref="SearchParameterDefinitionManager"/>
    /// (constructed exactly as production DI does - see <c>SqlEntityFrameworkRepositoryFactory.cs</c>'s
    /// <c>GetOrCreateDefinitionManagers</c>), NOT the synthetic <c>ignixa.dev</c> catalog <see cref="SeedAsync"/>
    /// uses. This is additive: it does not delete or modify <see cref="SeedAsync"/>'s existing synthetic rows.
    /// </summary>
    /// <param name="context">The FhirDbContext to seed. Its underlying connection is used directly for
    /// SqlBulkCopy; the caller retains ownership and disposal responsibility.</param>
    /// <param name="compartmentId">The compartment's ReferenceResourceId (e.g. "step0-patient").</param>
    /// <param name="rowsPerResourceType">Baseline row count for the non-hot ("cold") associations; each
    /// cold association's actual row count is a deterministic multiple of this value, clamped to
    /// [100, 5000]. The hot (Observation/subject) association ignores this value below a fixed floor of
    /// 550,000 rows.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Row count seeded per "ResourceType/code" association, in catalog order.</returns>
    public static async Task<IReadOnlyDictionary<string, int>> SeedRealPatientCompartmentAssociationsAsync(
        FhirDbContext context,
        string compartmentId,
        int rowsPerResourceType,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(compartmentId);
        if (rowsPerResourceType < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(rowsPerResourceType), rowsPerResourceType, "Must be positive.");
        }

        var compartmentManager = new CompartmentDefinitionManager(FhirVersion.R4);
        var schemaProvider = FhirVersion.R4.GetSchemaProvider();
        var parameterManager = new SearchParameterDefinitionManager(schemaProvider, NullLogger<SearchParameterDefinitionManager>.Instance);

        // Step A: enumerate EVERY real (resourceType, code) -> canonical URL pair the actual R4 Patient
        // compartment definition lists (all resource types in the compartment, not just this seeder's 15
        // curated types) and sync all of them into dbo.SearchParam. This is what CompartmentSearchQueryGenerator
        // (Arm A) needs: it resolves against ALL resource types in the compartment (resourceTypesToSearch=null),
        // so Arm A's own query breadth - not just our curated subset - must be resolvable.
        if (!compartmentManager.TryGetResourceTypes(CompartmentType.Patient, out var allResourceTypes))
        {
            throw new InvalidOperationException("Real Patient compartment definition returned no resource types - cannot proceed.");
        }

        var urlLookup = new Dictionary<(string ResourceType, string Code), string>();
        var allUrls = new List<string>();
        foreach (var resourceType in allResourceTypes)
        {
            if (!compartmentManager.TryGetSearchParams(resourceType, CompartmentType.Patient, out var codes))
            {
                continue;
            }

            foreach (var code in codes)
            {
                try
                {
                    var info = parameterManager.GetSearchParameter(resourceType, code);
                    if (info.Url == null)
                    {
                        continue;
                    }

                    var url = info.Url.ToString();
                    urlLookup[(resourceType, code)] = url;
                    allUrls.Add(url);
                }
                catch (Exception)
                {
                    // Mirrors CompartmentSearchQueryGenerator's own catch-and-skip for pairs that aren't
                    // real, resolvable search parameters (CompartmentSearchQueryGenerator.cs:147-155).
                }
            }
        }

        var distinctUrls = allUrls.Distinct(StringComparer.Ordinal).ToList();

        // CA2000 suppressed: see SyncSearchParamCatalogAsync's identical justification - Dispose() would
        // dispose the caller's FhirDbContext, which the caller still owns and needs afterward.
#pragma warning disable CA2000
        var cache = new SearchIndexReferenceDataCache(context, NullLogger<SearchIndexReferenceDataCache>.Instance);
#pragma warning restore CA2000
        await cache.SyncSearchParametersToDatabase(distinctUrls, parameterManager);

        // Step B: resolve our curated associations' SearchParamIds now that their URLs are synced.
        var patientResourceTypeId = await GetOrCreateResourceTypeIdAsync(context, PatientResourceTypeName, ct);
        var curatedResourceTypeNames = RealPatientCompartmentAssociations.Select(a => a.ResourceType).ToList();
        var resourceTypeIds = await GetOrCreateResourceTypeIdsAsync(context, curatedResourceTypeNames, ct);

        var curatedUrls = RealPatientCompartmentAssociations
            .Select(a => urlLookup[(a.ResourceType, a.Code)])
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var searchParamIdsByUrl = await context.SearchParams
            .AsNoTracking()
            .Where(sp => curatedUrls.Contains(sp.Uri))
            .ToDictionaryAsync(sp => sp.Uri, sp => sp.SearchParamId, ct);

        // Step C: skewed bulk insert for the curated associations, reusing SeedAsync's existing SqlBulkCopy
        // machinery rather than duplicating it.
        var connection = (SqlConnection)context.Database.GetDbConnection();
        var openedByUs = connection.State != ConnectionState.Open;
        if (openedByUs)
        {
            await connection.OpenAsync(ct);
        }

        var rowCountsByAssociation = new Dictionary<string, int>(StringComparer.Ordinal);
        try
        {
            for (var i = 0; i < RealPatientCompartmentAssociations.Length; i++)
            {
                ct.ThrowIfCancellationRequested();

                var (resourceType, code) = RealPatientCompartmentAssociations[i];
                var resourceTypeId = resourceTypeIds[resourceType];
                var url = urlLookup[(resourceType, code)];
                var searchParamId = searchParamIdsByUrl[url];
                var rowCount = i == 0
                    ? Math.Max(rowsPerResourceType, HotResourceTypeRowFloor)
                    : ComputeColdRowCount(rowsPerResourceType, i);

                var surrogateIdBase = await GetNextSurrogateIdBaseAsync(context, resourceTypeId, ct);

                await BulkInsertResourceAndReferenceRowsAsync(
                    connection,
                    resourceTypeId,
                    resourceType,
                    searchParamId,
                    patientResourceTypeId,
                    compartmentId,
                    surrogateIdBase,
                    rowCount,
                    ct);

                rowCountsByAssociation[$"{resourceType}/{code}"] = rowCount;
            }
        }
        finally
        {
            if (openedByUs)
            {
                await connection.CloseAsync();
            }
        }

        return rowCountsByAssociation;
    }

    private static int ComputeColdRowCount(int rowsPerResourceType, int resourceTypeIndex)
    {
        var multiplier = ColdRowCountMultipliers[resourceTypeIndex % ColdRowCountMultipliers.Length];
        var scaled = (int)(rowsPerResourceType * multiplier);
        return Math.Clamp(scaled, ColdResourceTypeRowMin, ColdResourceTypeRowMax);
    }

    private static async Task<long> GetNextSurrogateIdBaseAsync(FhirDbContext context, short resourceTypeId, CancellationToken ct)
    {
        var max = await context.Resources
            .Where(r => r.ResourceTypeId == resourceTypeId)
            .Select(r => (long?)r.ResourceSurrogateId)
            .MaxAsync(ct);

        return (max ?? 0L) + 1;
    }

    private static async Task<short> GetOrCreateResourceTypeIdAsync(FhirDbContext context, string name, CancellationToken ct)
    {
        var existing = await context.ResourceTypes.AsNoTracking().FirstOrDefaultAsync(rt => rt.Name == name, ct);
        if (existing != null)
        {
            return existing.ResourceTypeId;
        }

        var entity = new ResourceTypeEntity { Name = name };
        context.ResourceTypes.Add(entity);
        await context.SaveChangesAsync(ct);
        return entity.ResourceTypeId;
    }

    private static async Task<Dictionary<string, short>> GetOrCreateResourceTypeIdsAsync(
        FhirDbContext context,
        IReadOnlyList<string> names,
        CancellationToken ct)
    {
        var map = new Dictionary<string, short>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            map[name] = await GetOrCreateResourceTypeIdAsync(context, name, ct);
        }

        return map;
    }

    /// <summary>
    /// Seeds the SearchParam catalog rows using the same mechanism <c>IgnixaApiFixture.cs</c> uses
    /// for base FHIR search parameters (<see cref="SearchIndexReferenceDataCache.SyncSearchParametersToDatabase"/>),
    /// rather than hand-rolling upsert logic. The <see cref="ISearchParameterDefinitionManager"/>
    /// parameter is only consulted for OverridesUrl aliasing, which this synthetic catalog doesn't use,
    /// so it's safely passed as null.
    /// </summary>
    private static async Task<Dictionary<string, short>> SyncSearchParamCatalogAsync(
        FhirDbContext context,
        IReadOnlyList<string> codes,
        CancellationToken ct)
    {
        var urls = codes.Select(code => $"{SearchParamUriPrefix}{code}").ToList();

        // CA2000 suppressed: SearchIndexReferenceDataCache.Dispose() disposes the FhirDbContext it was
        // constructed with, which the caller of SeedAsync still owns and needs afterward - calling
        // Dispose() here would be the actual bug.
#pragma warning disable CA2000
        var cache = new SearchIndexReferenceDataCache(context, NullLogger<SearchIndexReferenceDataCache>.Instance);
#pragma warning restore CA2000
        await cache.SyncSearchParametersToDatabase(urls, null!);

        var idsByUri = await context.SearchParams
            .AsNoTracking()
            .Where(sp => urls.Contains(sp.Uri))
            .ToDictionaryAsync(sp => sp.Uri, sp => sp.SearchParamId, ct);

        var idsByCode = new Dictionary<string, short>(StringComparer.Ordinal);
        for (var i = 0; i < codes.Count; i++)
        {
            idsByCode[codes[i]] = idsByUri[urls[i]];
        }

        return idsByCode;
    }

    private static async Task BulkInsertResourceAndReferenceRowsAsync(
        SqlConnection connection,
        short resourceTypeId,
        string resourceTypeName,
        short searchParamId,
        short patientResourceTypeId,
        string compartmentId,
        long surrogateIdBase,
        int rowCount,
        CancellationToken ct)
    {
        for (var offset = 0; offset < rowCount; offset += BulkCopyBatchSize)
        {
            ct.ThrowIfCancellationRequested();

            var batchSize = Math.Min(BulkCopyBatchSize, rowCount - offset);
            using var resourceTable = CreateResourceDataTable();
            using var referenceTable = CreateReferenceSearchParamDataTable();

            for (var i = 0; i < batchSize; i++)
            {
                var surrogateId = surrogateIdBase + offset + i;

                var resourceRow = resourceTable.NewRow();
                resourceRow["ResourceTypeId"] = resourceTypeId;
                resourceRow["ResourceId"] = $"{resourceTypeName}-{surrogateId}";
                resourceRow["Version"] = 1;
                resourceRow["IsHistory"] = false;
                resourceRow["ResourceSurrogateId"] = surrogateId;
                resourceRow["IsDeleted"] = false;
                resourceRow["RequestMethod"] = DBNull.Value;
                resourceRow["RawResource"] = PlaceholderRawResource;
                resourceRow["IsRawResourceMetaSet"] = false;
                resourceRow["SearchParamHash"] = DBNull.Value;
                resourceRow["TransactionId"] = DBNull.Value;
                resourceRow["HistoryTransactionId"] = DBNull.Value;
                resourceTable.Rows.Add(resourceRow);

                var referenceRow = referenceTable.NewRow();
                referenceRow["ResourceTypeId"] = resourceTypeId;
                referenceRow["ResourceSurrogateId"] = surrogateId;
                referenceRow["SearchParamId"] = searchParamId;
                referenceRow["BaseUri"] = DBNull.Value;
                referenceRow["ReferenceResourceTypeId"] = patientResourceTypeId;
                referenceRow["ReferenceResourceId"] = compartmentId;
                referenceRow["ReferenceResourceVersion"] = DBNull.Value;
                referenceTable.Rows.Add(referenceRow);
            }

            await BulkCopyAsync(connection, "dbo.Resource", resourceTable, ct);
            await BulkCopyAsync(connection, "dbo.ReferenceSearchParam", referenceTable, ct);
        }
    }

    private static async Task BulkCopyAsync(SqlConnection connection, string destinationTableName, DataTable table, CancellationToken ct)
    {
        using var bulkCopy = new SqlBulkCopy(connection)
        {
            DestinationTableName = destinationTableName,
            BatchSize = BulkCopyBatchSize,
            BulkCopyTimeout = 120
        };

        foreach (DataColumn column in table.Columns)
        {
            bulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
        }

        await bulkCopy.WriteToServerAsync(table, ct);
    }

    // Column shapes below mirror ResourceEntity.cs and ReferenceSearchParamEntity.cs exactly
    // (read in full before writing this seeder) - not guessed.

    private static DataTable CreateResourceDataTable()
    {
        var table = new DataTable();
        table.Columns.Add("ResourceTypeId", typeof(short));
        table.Columns.Add("ResourceId", typeof(string));
        table.Columns.Add("Version", typeof(int));
        table.Columns.Add("IsHistory", typeof(bool));
        table.Columns.Add("ResourceSurrogateId", typeof(long));
        table.Columns.Add("IsDeleted", typeof(bool));
        table.Columns.Add("RequestMethod", typeof(string));
        table.Columns.Add("RawResource", typeof(byte[]));
        table.Columns.Add("IsRawResourceMetaSet", typeof(bool));
        table.Columns.Add("SearchParamHash", typeof(string));
        table.Columns.Add("TransactionId", typeof(long));
        table.Columns.Add("HistoryTransactionId", typeof(long));
        return table;
    }

    private static DataTable CreateReferenceSearchParamDataTable()
    {
        var table = new DataTable();
        table.Columns.Add("ResourceTypeId", typeof(short));
        table.Columns.Add("ResourceSurrogateId", typeof(long));
        table.Columns.Add("SearchParamId", typeof(short));
        table.Columns.Add("BaseUri", typeof(string));
        table.Columns.Add("ReferenceResourceTypeId", typeof(short));
        table.Columns.Add("ReferenceResourceId", typeof(string));
        table.Columns.Add("ReferenceResourceVersion", typeof(int));
        return table;
    }

    private static byte[] CreatePlaceholderRawResource()
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            var json = Encoding.UTF8.GetBytes("{}");
            gzip.Write(json, 0, json.Length);
        }

        return output.ToArray();
    }
}

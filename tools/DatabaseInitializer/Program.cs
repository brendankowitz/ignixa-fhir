// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace DatabaseInitializer;

/// <summary>
/// Command-line tool to initialize a SQL Server database with Microsoft FHIR Server schema (v60-v96).
/// Supports creating new databases and seeding initial data.
/// </summary>
internal class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            // Build configuration
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: true)
                .AddCommandLine(args)
                .Build();

            // Parse arguments
            var connectionString = configuration["connection"] ?? configuration["ConnectionStrings:FhirDatabase"];
            var databaseName = configuration["database"] ?? "FhirDatabase";
            var schemaVersion = int.Parse(configuration["schema-version"] ?? "97"); // v97 is latest
            var seedData = bool.Parse(configuration["seed-data"] ?? "true");

            if (string.IsNullOrEmpty(connectionString))
            {
                Console.Error.WriteLine("ERROR: Connection string is required.");
                Console.WriteLine("Usage: DatabaseInitializer --connection \"Server=...\" --database FhirDatabase --schema-version 97 --seed-data true");
                return 1;
            }

            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  FHIR Server Database Initializer                            ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine();
            Console.WriteLine($"Database Name:    {databaseName}");
            Console.WriteLine($"Schema Version:   {schemaVersion}");
            Console.WriteLine($"Seed Data:        {seedData}");
            Console.WriteLine();

            var service = new DatabaseInitializerService(connectionString, databaseName);

            // Step 1: Create database if not exists
            Console.WriteLine("[1/3] Creating database (if not exists)...");
            await service.CreateDatabaseIfNotExistsAsync();
            Console.WriteLine("✓ Database ready");
            Console.WriteLine();

            // Step 2: Deploy schema
            Console.WriteLine($"[2/3] Deploying schema version {schemaVersion}...");
            await service.DeploySchemaAsync(schemaVersion);
            Console.WriteLine("✓ Schema deployed");
            Console.WriteLine();

            // Step 3: Seed initial data
            if (seedData)
            {
                Console.WriteLine("[3/3] Seeding initial data...");
                await service.SeedInitialDataAsync();
                Console.WriteLine("✓ Initial data seeded");
                Console.WriteLine();
            }

            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  Database initialization completed successfully!             ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }
}

/// <summary>
/// Service for database initialization operations.
/// </summary>
internal class DatabaseInitializerService
{
    private readonly string _masterConnectionString;
    private readonly string _databaseConnectionString;
    private readonly string _databaseName;

    public DatabaseInitializerService(string connectionString, string databaseName)
    {
        _databaseName = databaseName;

        // Build master connection string (for creating database)
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = "master"
        };
        _masterConnectionString = builder.ConnectionString;

        // Build database connection string
        builder.InitialCatalog = databaseName;
        _databaseConnectionString = builder.ConnectionString;
    }

    /// <summary>
    /// Creates the database if it does not exist.
    /// </summary>
    public async Task CreateDatabaseIfNotExistsAsync()
    {
        await using var connection = new SqlConnection(_masterConnectionString);
        await connection.OpenAsync();

        // Check if database exists
        var sql = $@"
            IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = N'{_databaseName}')
            BEGIN
                CREATE DATABASE [{_databaseName}];
                SELECT 1 AS Created;
            END
            ELSE
            BEGIN
                SELECT 0 AS Created;
            END
        ";

        await using var command = new SqlCommand(sql, connection);
        var result = (int)(await command.ExecuteScalarAsync() ?? 0);

        if (result == 1)
        {
            Console.WriteLine($"  → Created new database '{_databaseName}'");
        }
        else
        {
            Console.WriteLine($"  → Database '{_databaseName}' already exists");
        }
    }

    /// <summary>
    /// Deploys the FHIR schema from the embedded SQL script.
    /// </summary>
    public async Task DeploySchemaAsync(int schemaVersion)
    {
        var scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Schemas", $"{schemaVersion}.sql");

        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException($"Schema script not found: {scriptPath}");
        }

        Console.WriteLine($"  → Reading schema script: {scriptPath}");

        var scriptContent = await File.ReadAllTextAsync(scriptPath);

        // Split script into batches (by GO statements)
        var batches = SplitSqlBatches(scriptContent);

        Console.WriteLine($"  → Executing {batches.Count} SQL batches...");

        await using var connection = new SqlConnection(_databaseConnectionString);
        await connection.OpenAsync();

        int batchNumber = 1;
        foreach (var batch in batches)
        {
            if (string.IsNullOrWhiteSpace(batch))
            {
                continue;
            }

            Console.WriteLine($"    Batch {batchNumber}/{batches.Count}");

            await using var command = new SqlCommand(batch, connection)
            {
                CommandTimeout = 300 // 5 minutes
            };

            await command.ExecuteNonQueryAsync();
            batchNumber++;
        }
    }

    /// <summary>
    /// Seeds initial ResourceType data for FHIR R4 resource types.
    /// </summary>
    public async Task SeedInitialDataAsync()
    {
        await using var connection = new SqlConnection(_databaseConnectionString);
        await connection.OpenAsync();

        // Check if ResourceType table already has data
        var checkSql = "SELECT COUNT(*) FROM dbo.ResourceType";
        await using var checkCommand = new SqlCommand(checkSql, connection);
        var count = (int)(await checkCommand.ExecuteScalarAsync() ?? 0);

        if (count > 0)
        {
            Console.WriteLine($"  → ResourceType table already has {count} entries, skipping seed");
            return;
        }

        Console.WriteLine("  → Seeding ResourceType table with R4 resource types...");

        // FHIR R4 resource types (145 types)
        var resourceTypes = new[]
        {
            "Account", "ActivityDefinition", "AdverseEvent", "AllergyIntolerance", "Appointment",
            "AppointmentResponse", "AuditEvent", "Basic", "Binary", "BiologicallyDerivedProduct",
            "BodyStructure", "Bundle", "CapabilityStatement", "CarePlan", "CareTeam",
            "CatalogEntry", "ChargeItem", "ChargeItemDefinition", "Claim", "ClaimResponse",
            "ClinicalImpression", "CodeSystem", "Communication", "CommunicationRequest", "CompartmentDefinition",
            "Composition", "ConceptMap", "Condition", "Consent", "Contract",
            "Coverage", "CoverageEligibilityRequest", "CoverageEligibilityResponse", "DetectedIssue", "Device",
            "DeviceDefinition", "DeviceMetric", "DeviceRequest", "DeviceUseStatement", "DiagnosticReport",
            "DocumentManifest", "DocumentReference", "EffectEvidenceSynthesis", "Encounter", "Endpoint",
            "EnrollmentRequest", "EnrollmentResponse", "EpisodeOfCare", "EventDefinition", "Evidence",
            "EvidenceVariable", "ExampleScenario", "ExplanationOfBenefit", "FamilyMemberHistory", "Flag",
            "Goal", "GraphDefinition", "Group", "GuidanceResponse", "HealthcareService",
            "ImagingStudy", "Immunization", "ImmunizationEvaluation", "ImmunizationRecommendation", "ImplementationGuide",
            "InsurancePlan", "Invoice", "Library", "Linkage", "List",
            "Location", "Measure", "MeasureReport", "Media", "Medication",
            "MedicationAdministration", "MedicationDispense", "MedicationKnowledge", "MedicationRequest", "MedicationStatement",
            "MedicinalProduct", "MedicinalProductAuthorization", "MedicinalProductContraindication", "MedicinalProductIndication", "MedicinalProductIngredient",
            "MedicinalProductInteraction", "MedicinalProductManufactured", "MedicinalProductPackaged", "MedicinalProductPharmaceutical", "MedicinalProductUndesirableEffect",
            "MessageDefinition", "MessageHeader", "MolecularSequence", "NamingSystem", "NutritionOrder",
            "Observation", "ObservationDefinition", "OperationDefinition", "OperationOutcome", "Organization",
            "OrganizationAffiliation", "Parameters", "Patient", "PaymentNotice", "PaymentReconciliation",
            "Person", "PlanDefinition", "Practitioner", "PractitionerRole", "Procedure",
            "Provenance", "Questionnaire", "QuestionnaireResponse", "RelatedPerson", "RequestGroup",
            "ResearchDefinition", "ResearchElementDefinition", "ResearchStudy", "ResearchSubject", "RiskAssessment",
            "RiskEvidenceSynthesis", "Schedule", "SearchParameter", "ServiceRequest", "Slot",
            "Specimen", "SpecimenDefinition", "StructureDefinition", "StructureMap", "Subscription",
            "Substance", "SubstanceNucleicAcid", "SubstancePolymer", "SubstanceProtein", "SubstanceReferenceInformation",
            "SubstanceSourceMaterial", "SubstanceSpecification", "SupplyDelivery", "SupplyRequest", "Task",
            "TerminologyCapabilities", "TestReport", "TestScript", "ValueSet", "VerificationResult",
            "VisionPrescription"
        };

        // Insert resource types
        foreach (var resourceType in resourceTypes)
        {
            var insertSql = "INSERT INTO dbo.ResourceType (Name) VALUES (@Name)";
            await using var insertCommand = new SqlCommand(insertSql, connection);
            insertCommand.Parameters.AddWithValue("@Name", resourceType);
            await insertCommand.ExecuteNonQueryAsync();
        }

        Console.WriteLine($"  → Seeded {resourceTypes.Length} resource types");
    }

    /// <summary>
    /// Splits a SQL script into batches using GO statements.
    /// </summary>
    private static List<string> SplitSqlBatches(string sql)
    {
        // Split by GO statements (case-insensitive, whole word)
        var regex = new Regex(@"^\s*GO\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        var batches = regex.Split(sql)
            .Select(b => b.Trim())
            .Where(b => !string.IsNullOrWhiteSpace(b))
            .ToList();

        return batches;
    }
}

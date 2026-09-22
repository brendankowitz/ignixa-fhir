using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Experimental.Ips.Generator;
using Ignixa.Application.Features.Experimental.Ips.Strategy;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Ignixa.NarrativeGenerator;
using Ignixa.Search.Definition;
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Search.Sql;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Experimental.Ips;

public class IpsSearchAnchoringTests
{
    private const string PatientId = "ips-patient";
    private const short ObservationTypeId = 102;
    private const short ConditionTypeId = 103;

    private static readonly SearchEntryResult Patient = Entry("Patient", PatientId,
        """{"resourceType":"Patient","id":"ips-patient","name":[{"family":"Summary"}]}""");
    private static readonly SearchEntryResult Condition = Entry("Condition", "ips-condition",
        """{"resourceType":"Condition","id":"ips-condition","clinicalStatus":{"coding":[{"system":"http://terminology.hl7.org/CodeSystem/condition-clinical","code":"active"}]},"code":{"text":"Active condition"},"subject":{"reference":"Patient/ips-patient"}}""");
    private static readonly SearchEntryResult Observation = Entry("Observation", "ips-observation",
        """{"resourceType":"Observation","id":"ips-observation","status":"final","code":{"text":"Clinical observation"},"subject":{"reference":"Patient/ips-patient"}}""");

    [Theory]
    [InlineData(1, FhirVersion.R4)]
    [InlineData(7, FhirVersion.R5)]
    public async Task GivenTenantPatient_WhenGeneratingIps_ThenRealCompilerAcceptsThePatientAnchorAndClinicalTypes(
        int tenantId, FhirVersion version)
    {
        var schema = version.GetSchemaProvider();
        var compiler = CreateCompiler(version);
        var context = new FhirRequestContext
        {
            TenantId = tenantId,
            FhirVersion = version,
            TenantConfiguration = new TenantConfiguration
            {
                TenantId = tenantId,
                DisplayName = "IPS contract tenant",
                FhirVersion = version == FhirVersion.R4 ? "4.0" : "5.0",
            },
        };
        var accessor = Substitute.For<IFhirRequestContextAccessor>();
        accessor.RequestContext.Returns(context);
        var repository = Substitute.For<IFhirRepository>();
        repository.GetAsync(new ResourceKey("Patient", PatientId), Arg.Any<CancellationToken>()).Returns(Patient);
        var repositoryFactory = Substitute.For<IFhirRepositoryFactory>();
        repositoryFactory.GetRepositoryAsync(tenantId, Arg.Any<CancellationToken>()).Returns(repository);
        var execution = Substitute.For<IQueryExecutionStrategy>();
        execution.SearchStreamAsync(
            Arg.Any<RequestPartition>(), Arg.Any<SearchOptions>(), Arg.Any<CancellationToken>())
            .Returns(call => CompileThenReadAsync(
                compiler, call.Arg<SearchOptions>(), call.Arg<RequestPartition>(),
                tenantId, call.Arg<CancellationToken>()));
        var narratives = Substitute.For<INarrativeGenerator>();
        narratives.GenerateNarrativeAsync(
            Arg.Any<IElement>(), Arg.Any<string>(), Arg.Any<CultureInfo>(),
            TemplateFormat.Html, Arg.Any<CancellationToken>())
            .Returns("""<div xmlns="http://www.w3.org/1999/xhtml">Clinical resource</div>""");
        var generator = new IpsGeneratorService(
            [new DefaultIpsGenerationStrategy()], execution, repositoryFactory,
            new IsolatedModePartitionStrategy(NullLogger<IsolatedModePartitionStrategy>.Instance),
            accessor, narratives, schema, NullLogger<IpsGeneratorService>.Instance);

        var bundle = await generator.GenerateIpsAsync(PatientId, cancellationToken: CancellationToken.None);

        bundle.GetTypeRaw().ShouldBe("document");
        bundle.Entry[0].Resource!.ResourceType.ShouldBe("Composition");
        bundle.Entry.Single(e => e.Resource?.ResourceType == "Patient").Resource!.Id.ShouldBe(PatientId);
        bundle.Entry.Where(e => e.Resource?.ResourceType == "Condition" || e.Resource?.ResourceType == "Observation")
            .Select(e => $"{e.Resource!.ResourceType}/{e.Resource.Id}").Order()
            .ShouldBe(["Condition/ips-condition", "Observation/ips-observation"]);
        context.TenantId.ShouldBe(tenantId);
        context.FhirVersion.ShouldBe(version);
    }

    [Theory]
    [InlineData(FhirVersion.R4)]
    [InlineData(FhirVersion.R5)]
    public async Task GivenEverythingWithoutAnchor_WhenCompiled_ThenSystemSearchIsStillRejected(FhirVersion version)
    {
        var options = new SearchOptions
        {
            ResourceType = null,
            Expression = new PatientEverythingExpression(
                PatientId, filteredResourceTypes: new HashSet<string> { "Condition", "Observation" }),
        };

        var result = await CreateCompiler(version).TryCreatePlanFromOptionsAsync(
            options, options.ResourceType, cancellationToken: CancellationToken.None);

        result.Succeeded.ShouldBeFalse();
        result.Failure.Message.ShouldContain("system-level search");
    }

    // The generator, partition strategy, version-specific definitions and compiler are real.
    // Only catalog I/O, persisted resource reads and narrative templates are controlled.
    private static async IAsyncEnumerable<SearchEntryResult> CompileThenReadAsync(
        SearchSqlCompiler compiler,
        SearchOptions options,
        RequestPartition partition,
        int tenantId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        partition.PartitionIds.ShouldBe([tenantId]);
        partition.Mode.ShouldBe(PartitionMode.Isolated);
        var expression = options.Expression.ShouldBeOfType<PatientEverythingExpression>();
        expression.PatientIds.ShouldBe([PatientId]);
        expression.FilteredResourceTypes.ShouldContain("Condition");
        expression.FilteredResourceTypes.ShouldContain("Observation");
        var planned = await compiler.TryCreatePlanFromOptionsAsync(
            options, options.ResourceType,
            new SearchPlanOptions
            {
                Shape = new ResultShape.Matches(new SearchPaging.Offset(new OffsetSpec(0, options.MaxItemCount))),
            },
            cancellationToken);
        if (!planned.Succeeded)
        {
            throw new RequestNotValidException(planned.Failure.Message);
        }

        var compiled = planned.Plan.Compile();
        var memberTypes = compiled.Query.Ctes.OfType<CteDefinition.CompartmentSource>()
            .SelectMany(c => c.ResourceTypeIds).ToHashSet();
        memberTypes.ShouldContain(ConditionTypeId);
        memberTypes.ShouldContain(ObservationTypeId);
        compiled.Parameters.ShouldContain(p => Equals(p.Value, PatientId));
        yield return Patient;
        yield return Condition;
        yield return Observation;
    }

    private static SearchSqlCompiler CreateCompiler(FhirVersion version)
    {
        var definitions = new SearchParameterDefinitionManager(
            version.GetSchemaProvider(), NullLogger<SearchParameterDefinitionManager>.Instance);
        var resourceIds = new Dictionary<string, short>(StringComparer.Ordinal)
        {
            ["Patient"] = 101,
            ["Observation"] = ObservationTypeId,
            ["Condition"] = ConditionTypeId,
        };
        foreach (string type in definitions.ResourceTypeNames.Order(StringComparer.Ordinal))
        {
            if (!resourceIds.ContainsKey(type))
            {
                resourceIds.Add(type, checked((short)(resourceIds.Count + 101)));
            }
        }
        var parameterIds = definitions.AllSearchParameters.Where(p => p.Url is not null)
            .Select(p => p.Url!.AbsoluteUri).Distinct().Order(StringComparer.Ordinal)
            .Select((url, index) => (Url: url, Id: checked((short)(index + 1))))
            .ToDictionary(p => p.Url, p => p.Id, StringComparer.Ordinal);
        var symbols = Substitute.For<ISymbolResolver>();
        symbols.GetResourceTypeIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => resourceIds.TryGetValue(call.Arg<string>(), out short id) ? (short?)id : null);
        symbols.GetSearchParamIdAsync(Arg.Any<SearchParameterInfo>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var url = call.Arg<SearchParameterInfo>().Url;
                return url is not null && parameterIds.TryGetValue(url.AbsoluteUri, out short id) ? (short?)id : null;
            });
        return new SearchSqlCompiler(
            symbols, compartmentDefinitionManager: new CompartmentDefinitionManager(version),
            searchParameterDefinitionManager: definitions);
    }

    private static SearchEntryResult Entry(string type, string id, string json) =>
        new(type, id, "1", DateTimeOffset.UnixEpoch, Encoding.UTF8.GetBytes(json));
}

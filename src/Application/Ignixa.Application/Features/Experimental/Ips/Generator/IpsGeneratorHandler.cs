// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using Ignixa.Application.Features.Experimental.Ips.Api;
using Ignixa.Domain.Exceptions;
using Medino;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Experimental.Ips.Generator;

/// <summary>
/// Handler for IPS generation queries.
/// </summary>
public class IpsGeneratorHandler(
    IIpsGeneratorService generatorService,
    ILogger<IpsGeneratorHandler> logger) : IRequestHandler<IpsGeneratorQuery, IpsGeneratorResult>
{
    public async Task<IpsGeneratorResult> HandleAsync(
        IpsGeneratorQuery request,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();

        if (request.PatientId is null && request.PatientIdentifier is null)
        {
            throw new BadRequestException(
                "Either PatientId or PatientIdentifier must be provided");
        }

        logger.LogInformation(
            "Generating IPS for patient {PatientId} with profile {Profile}",
            request.PatientId ?? request.PatientIdentifier,
            request.Profile ?? "default");

        var ipsBundle = request.PatientId is not null
            ? await generatorService.GenerateIpsAsync(
                request.PatientId,
                request.Profile,
                cancellationToken)
            : await generatorService.GenerateIpsByIdentifierAsync(
                ExtractIdentifierSystem(request.PatientIdentifier!),
                ExtractIdentifierValue(request.PatientIdentifier!),
                request.Profile,
                cancellationToken);

        sw.Stop();

        var metrics = new IpsGenerationMetrics(
            TotalResources: ipsBundle.Entry.Count,
            SectionsIncluded: CountSections(ipsBundle, withResources: true),
            SectionsEmpty: CountSections(ipsBundle, withResources: false),
            TotalDuration: sw.Elapsed);

        logger.LogInformation(
            "Generated IPS with {ResourceCount} resources in {Duration}ms",
            metrics.TotalResources,
            metrics.TotalDuration.TotalMilliseconds);

        return new IpsGeneratorResult(ipsBundle, metrics);
    }

    private static string? ExtractIdentifierSystem(string identifier)
    {
        var parts = identifier.Split('|', 2);
        return parts.Length > 1 ? parts[0] : null;
    }

    private static string ExtractIdentifierValue(string identifier)
    {
        var parts = identifier.Split('|', 2);
        return parts.Length > 1 ? parts[1] : parts[0];
    }

    private static int CountSections(Serialization.Models.BundleJsonNode bundle, bool withResources)
    {
        var composition = bundle.Entry.FirstOrDefault()?.Resource;
        if (composition is null)
        {
            return 0;
        }

        var sections = composition.MutableNode["section"] as System.Text.Json.Nodes.JsonArray;
        if (sections is null)
        {
            return 0;
        }

        var count = 0;
        foreach (var section in sections)
        {
            var entries = section?["entry"] as System.Text.Json.Nodes.JsonArray;
            var hasResources = entries is not null && entries.Count > 0;

            if (withResources == hasResources)
            {
                count++;
            }
        }

        return count;
    }
}

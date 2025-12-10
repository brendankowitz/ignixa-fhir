// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ignixa.Application.Features.Authorization;
using Ignixa.Application.Features.Metadata.Models;
using ExtensionJsonNode = Ignixa.Serialization.Models.ExtensionJsonNode;

namespace Ignixa.Application.Features.Metadata.Segments;

/// <summary>
/// Security capability segment that adds SMART on FHIR oauth-uris extension
/// to the CapabilityStatement when authorization is enabled.
/// Priority 15 ensures it runs after static (10) but before resource interactions (20).
/// </summary>
public class SecurityCapabilitySegment(
    IOptions<AuthorizationOptions> authOptions,
    ILogger<SecurityCapabilitySegment> logger) : ICapabilitySegment
{
    private readonly AuthorizationOptions _authOptions = authOptions?.Value ?? throw new ArgumentNullException(nameof(authOptions));
    private readonly ILogger<SecurityCapabilitySegment> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public string SegmentKey => "security";

    public int Priority => 15; // After static (10), before interactions (20)

    public ValueTask ApplyAsync(
        CapabilityStatementJsonNode statement,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Applying security capability segment for {FhirVersion}", context.FhirVersion);

        // Only add security configuration if authorization is enabled
        if (!_authOptions.Enabled)
        {
            _logger.LogDebug("Authorization disabled - skipping security configuration");
            return ValueTask.CompletedTask;
        }

        // Ensure REST component exists
        if (statement.Rest.Count == 0)
        {
            statement.Rest.Add(new RestComponentJsonNode
            {
                FhirVersion = context.FhirVersion,
                Mode = RestComponentJsonNode.RestfulCapabilityMode.Server,
            });
        }

        var restComponent = statement.Rest[0];

        // Create or update security component
        if (restComponent.Security is null)
        {
            restComponent.Security = new SecurityComponentJsonNode
            {
                FhirVersion = context.FhirVersion,
            };
        }

        var security = restComponent.Security;

        // Set CORS if needed (SMART apps typically require CORS)
        security.Cors = true;

        // Add SMART-on-FHIR service coding
        security.Service.Clear();
        var smartService = new CodeableConceptJsonNode { FhirVersion = context.FhirVersion };
        smartService.Coding.Add(new CodingJsonNode
        {
            FhirVersion = context.FhirVersion,
            System = "http://terminology.hl7.org/CodeSystem/restful-security-service",
            Code = "SMART-on-FHIR",
            Display = "SMART-on-FHIR",
        });
        security.Service.Add(smartService);

        // Add oauth-uris extension if OAuth URLs are configured
        var smartOptions = _authOptions.SmartOnFhir;
        if (!string.IsNullOrEmpty(smartOptions.AuthorizeUrl) && !string.IsNullOrEmpty(smartOptions.TokenUrl))
        {
            _logger.LogDebug("Adding SMART oauth-uris extension to security component");

            var oauthExtension = new ExtensionJsonNode
            {
                FhirVersion = context.FhirVersion,
                Url = "http://fhir-registry.smarthealthit.org/StructureDefinition/oauth-uris",
            };

            // Add authorize endpoint
            oauthExtension.Extension.Add(new ExtensionJsonNode
            {
                FhirVersion = context.FhirVersion,
                Url = "authorize",
                ValueUri = smartOptions.AuthorizeUrl,
            });

            // Add token endpoint
            oauthExtension.Extension.Add(new ExtensionJsonNode
            {
                FhirVersion = context.FhirVersion,
                Url = "token",
                ValueUri = smartOptions.TokenUrl,
            });

            // Add introspect endpoint if configured
            if (!string.IsNullOrEmpty(smartOptions.IntrospectUrl))
            {
                oauthExtension.Extension.Add(new ExtensionJsonNode
                {
                    FhirVersion = context.FhirVersion,
                    Url = "introspect",
                    ValueUri = smartOptions.IntrospectUrl,
                });
            }

            // Add revoke endpoint if configured
            if (!string.IsNullOrEmpty(smartOptions.RevokeUrl))
            {
                oauthExtension.Extension.Add(new ExtensionJsonNode
                {
                    FhirVersion = context.FhirVersion,
                    Url = "revoke",
                    ValueUri = smartOptions.RevokeUrl,
                });
            }

            security.Extension.Add(oauthExtension);

            _logger.LogInformation(
                "Added SMART oauth-uris extension with authorize={AuthorizeUrl}, token={TokenUrl}",
                smartOptions.AuthorizeUrl,
                smartOptions.TokenUrl);
        }
        else
        {
            _logger.LogWarning(
                "SMART OAuth URLs not configured - oauth-uris extension will not be added. " +
                "Set Authorization:SmartOnFhir:AuthorizeUrl and Authorization:SmartOnFhir:TokenUrl in configuration.");
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<string> GetVersionHashAsync(
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        // Security segment version hash includes:
        // - Whether authorization is enabled
        // - OAuth URLs (changes when endpoints change)
        var smartOptions = _authOptions.SmartOnFhir;
        var hash = $"auth={_authOptions.Enabled}|authorize={smartOptions.AuthorizeUrl}|token={smartOptions.TokenUrl}|introspect={smartOptions.IntrospectUrl}|revoke={smartOptions.RevokeUrl}";
        return ValueTask.FromResult(hash);
    }
}

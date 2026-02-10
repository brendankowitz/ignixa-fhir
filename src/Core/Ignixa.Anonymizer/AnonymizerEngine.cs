// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation + Ignixa Contributors
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Reflection;
using System.Text.Json;
using EnsureThat;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Extensions;
using Microsoft.Extensions.Logging;
using Ignixa.Anonymizer.AnonymizerConfigurations;
using Ignixa.Anonymizer.Exceptions;
using Ignixa.Anonymizer.Extensions;
using Ignixa.Anonymizer.Models;
using Ignixa.Anonymizer.Processors;

namespace Ignixa.Anonymizer;

public class AnonymizerEngine
{
    private readonly AnonymizerConfigurationManager _configurationManager;
    private readonly Dictionary<string, IAnonymizerProcessor> _processors;
    private readonly AnonymizationFhirPathRule[] _rules;
    private readonly IFhirSchemaProvider _schema;
    private readonly ILogger _logger = AnonymizerLogging.CreateLogger<AnonymizerEngine>();
    private readonly IAnonymizerProcessorFactory? _customProcessorFactory;

    public AnonymizerEngine(string configFilePath, IFhirSchemaProvider schema, IAnonymizerProcessorFactory? customProcessorFactory = null)
        : this(AnonymizerConfigurationManager.CreateFromConfigurationFile(configFilePath), schema, customProcessorFactory)
    {
    }

    public AnonymizerEngine(AnonymizerConfigurationManager configurationManager, IFhirSchemaProvider schema, IAnonymizerProcessorFactory? customProcessorFactory = null)
    {
        _configurationManager = configurationManager;
        _schema = schema;
        _processors = [];
        _customProcessorFactory = customProcessorFactory;

        InitializeProcessors(_configurationManager);

        _rules = _configurationManager.FhirPathRules;
        _logger.LogDebug("AnonymizerEngine initialized successfully for FHIR version {FhirVersion}", _schema.Version);
    }

    // Convenience overloads accepting ISchema for backward compatibility
    public AnonymizerEngine(string configFilePath, ISchema schema, IAnonymizerProcessorFactory? customProcessorFactory = null, FhirVersion fhirVersion = FhirVersion.R4)
        : this(configFilePath, ResolveSchemaProvider(schema, fhirVersion), customProcessorFactory)
    {
    }

    public AnonymizerEngine(AnonymizerConfigurationManager configurationManager, ISchema schema, IAnonymizerProcessorFactory? customProcessorFactory = null, FhirVersion fhirVersion = FhirVersion.R4)
        : this(configurationManager, ResolveSchemaProvider(schema, fhirVersion), customProcessorFactory)
    {
    }

    public static AnonymizerEngine CreateFromVersion(string configFilePath, FhirVersion fhirVersion, IAnonymizerProcessorFactory? customProcessorFactory = null)
    {
        var schema = fhirVersion.GetSchemaProvider();
        return new AnonymizerEngine(configFilePath, schema, customProcessorFactory);
    }

    public static AnonymizerEngine CreateWithFileContext(string configFilePath, ISchema schema, string fileName, string inputFolderName, IAnonymizerProcessorFactory? customProcessorFactory = null, FhirVersion fhirVersion = FhirVersion.R4)
    {
        var configurationManager = AnonymizerConfigurationManager.CreateFromConfigurationFile(configFilePath);
        var dateShiftScope = configurationManager.GetParameterConfiguration().DateShiftScope;
        var dateShiftKeyPrefix = dateShiftScope switch
        {
            DateShiftScope.File => Path.GetFileName(fileName),
            DateShiftScope.Folder => Path.GetFileName(inputFolderName.TrimEnd('\\', '/')),
            _ => string.Empty
        };

        configurationManager.SetDateShiftKeyPrefix(dateShiftKeyPrefix);
        return new AnonymizerEngine(configurationManager, ResolveSchemaProvider(schema, fhirVersion), customProcessorFactory);
    }

    public static AnonymizerEngine CreateWithFileContext(string configFilePath, FhirVersion fhirVersion, string fileName, string inputFolderName, IAnonymizerProcessorFactory? customProcessorFactory = null)
    {
        var schema = fhirVersion.GetSchemaProvider();
        return CreateWithFileContext(configFilePath, schema, fileName, inputFolderName, customProcessorFactory);
    }

    public ResourceJsonNode AnonymizeElement(ResourceJsonNode resource)
    {
        EnsureArg.IsNotNull(resource, nameof(resource));
        try
        {
            var element = resource.ToElement(_schema);
            return resource.Anonymize(element, _rules, _processors);
        }
        catch (AnonymizerProcessingException)
        {
            if (_configurationManager.Configuration.processingErrors == ProcessingErrorsOption.Skip)
            {
                return EmptyElement.Create(resource.ResourceType);
            }

            throw;
        }
    }

    public string AnonymizeJson(string json, AnonymizerSettings? settings = null)
    {
        EnsureArg.IsNotNullOrEmpty(json, nameof(json));

        var resource = ParseJsonToResourceNode(json);
        var anonymizedResource = AnonymizeElement(resource);

        var serializerOptions = new JsonSerializerOptions
        {
            WriteIndented = settings is { IsPrettyOutput: true }
        };

        return anonymizedResource.MutableNode.ToJsonString(serializerOptions);
    }

    private void InitializeProcessors(AnonymizerConfigurationManager configurationManager)
    {
        _processors[AnonymizerMethod.DateShift.ToString().ToUpperInvariant()] = DateShiftProcessor.Create(configurationManager);
        _processors[AnonymizerMethod.Redact.ToString().ToUpperInvariant()] = RedactProcessor.Create(configurationManager);
        _processors[AnonymizerMethod.CryptoHash.ToString().ToUpperInvariant()] = new CryptoHashProcessor(configurationManager.GetParameterConfiguration().CryptoHashKey, _schema);
        _processors[AnonymizerMethod.Encrypt.ToString().ToUpperInvariant()] = new EncryptProcessor(configurationManager.GetParameterConfiguration().EncryptKey);
        _processors[AnonymizerMethod.Substitute.ToString().ToUpperInvariant()] = new SubstituteProcessor();
        _processors[AnonymizerMethod.Perturb.ToString().ToUpperInvariant()] = new PerturbProcessor(_schema);
        _processors[AnonymizerMethod.Keep.ToString().ToUpperInvariant()] = new KeepProcessor();
        _processors[AnonymizerMethod.Generalize.ToString().ToUpperInvariant()] = new GeneralizeProcessor();
        if (_customProcessorFactory is not null)
        {
            InitializeCustomProcessors(configurationManager);
        }
    }

    private void InitializeCustomProcessors(AnonymizerConfigurationManager configurationManager)
    {
        var processorsField = _customProcessorFactory!.GetType()
            .GetField("_customProcessors", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (processorsField?.GetValue(_customProcessorFactory) is Dictionary<string, Type> processors)
        {
            foreach (var processor in processors)
            {
                _processors[processor.Key.ToUpperInvariant()] = _customProcessorFactory.CreateProcessor(
                    processor.Key, configurationManager.GetParameterConfiguration().CustomSettings);
            }
        }
    }

    private ResourceJsonNode ParseJsonToResourceNode(string json)
    {
        try
        {
            return ResourceJsonNode.Parse(json);
        }
        catch (Exception ex)
        {
            throw new InvalidInputException("The input FHIR resource JSON is invalid.", ex);
        }
    }

    private static IFhirSchemaProvider ResolveSchemaProvider(ISchema schema, FhirVersion fhirVersion = FhirVersion.R4)
    {
        if (schema is IFhirSchemaProvider provider)
        {
            return provider;
        }

        // Fallback: if someone passes a raw ISchema, get the proper provider for the version
        return fhirVersion.GetSchemaProvider();
    }
}

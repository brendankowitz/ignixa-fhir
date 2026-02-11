// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using EnsureThat;
using Ignixa.Anonymizer.Exceptions;

namespace Ignixa.Anonymizer.Processors;

public class CustomProcessorFactory : IAnonymizerProcessorFactory
{
    private readonly Dictionary<string, Type> _customProcessors = new(StringComparer.OrdinalIgnoreCase);

    public IAnonymizerProcessor CreateProcessor(string method, JsonObject? settingObject = null)
    {
        EnsureArg.IsNotNullOrEmpty(method, nameof(method));

        if (_customProcessors.TryGetValue(method, out var processorType))
        {
            return (IAnonymizerProcessor)Activator.CreateInstance(
                processorType,
                [settingObject])!;
        }

        return null!;
    }

    public void RegisterProcessors(params Type[] processors)
    {
        if (processors is not null)
        {
            RegisterProcessors(processors.AsEnumerable());
        }
    }

    public void RegisterProcessors(IEnumerable<Type> processors)
    {
        foreach (var processor in processors)
        {
            var method = GetMethodName(processor.Name);
            if (Constants.BuiltInMethods.Contains(method))
            {
                throw new CustomProcessorException($"Anonymization method {method} is a built-in method. Please add custom processor with unique method name.");
            }

            _customProcessors.Add(method, processor);
        }
    }

    private static string GetMethodName(string processor)
    {
        return processor.Replace("Processor", string.Empty);
    }
}

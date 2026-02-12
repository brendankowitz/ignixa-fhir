// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Collections.Generic;

namespace Ignixa.Anonymizer.Configuration
{
    /// <summary>
    /// Base class representing an anonymization rule with a path, method, type, and optional settings.
    /// </summary>
    public class AnonymizerRule
    {
        public string Path { get; set; }
        
        public string Method { get; set; }

        public AnonymizerRuleType Type { get; set; }

        public string Source { get; set; }

        public Dictionary<string, object> RuleSettings { get; set; }

        public AnonymizerRule(string path, string method, AnonymizerRuleType type, string source)
        {
            Path = path;
            Method = method;
            Type = type;
            Source = source;
        }
    }
}

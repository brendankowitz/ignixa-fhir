// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Collections.Generic;

namespace Ignixa.DeId.Configuration
{
    /// <summary>
    /// Base class representing an de-identification rule with a path, method, type, and optional settings.
    /// </summary>
    public class DeIdRule
    {
        public string Path { get; set; }
        
        public string Method { get; set; }

        public DeIdRuleType Type { get; set; }

        public string Source { get; set; }

        public Dictionary<string, object> RuleSettings { get; set; }

        public DeIdRule(string path, string method, DeIdRuleType type, string source)
        {
            Path = path;
            Method = method;
            Type = type;
            Source = source;
        }
    }
}

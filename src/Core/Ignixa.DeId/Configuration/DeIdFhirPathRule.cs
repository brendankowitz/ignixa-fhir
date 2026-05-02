// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using EnsureThat;

namespace Ignixa.DeId.Configuration
{
    /// <summary>
    /// Represents a FHIRPath-based de-identification rule with parsed expression and resource type filter.
    /// </summary>
    public class DeIdFhirPathRule : DeIdRule
    {
        private static Regex PathRegex = new Regex(@"^(?<resourceType>[A-Z][a-zA-Z]*)?(\.)?(?<expression>.*?)$");

        public string Expression { get; set; }

        public string ResourceType { get; }

        public bool IsResourceTypeRule => Path.Equals(ResourceType);

        public static DeIdFhirPathRule CreateDeIdFhirPathRule(Dictionary<string, object> config)
        {
            EnsureArg.IsNotNull(config);

            if (!config.ContainsKey(Constants.PathKey))
            {
                throw new ArgumentException("Missing path in rule config");
            }

            if (!config.ContainsKey(Constants.MethodKey))
            {
                throw new ArgumentException("Missing method in rule config");
            }

            string path = config[Constants.PathKey].ToString();
            string method = config[Constants.MethodKey].ToString();

            // Parse expression and resource type from path
            string resourceType = null;
            string expression = null;
            var match = PathRegex.Match(path);
            if (match.Success)
            {
                resourceType = match.Groups["resourceType"].Value;
                expression = match.Groups["expression"].Value;
            }

            if (string.IsNullOrEmpty(expression))
            {
                // For case: Path == "Resource"
                expression = path;
            }

            return new DeIdFhirPathRule(path, expression, resourceType, 
                method, DeIdRuleType.FhirPathRule, path, config);
        }

        public DeIdFhirPathRule(string path, string expression, string resourceType, string method,
            DeIdRuleType type, string source, Dictionary<string, object> settings = null)
            : base(path, method, type, source)
        {
            EnsureArg.IsNotNull(expression);

            Expression = expression;
            ResourceType = resourceType;
            RuleSettings = settings;
        }
    }
}

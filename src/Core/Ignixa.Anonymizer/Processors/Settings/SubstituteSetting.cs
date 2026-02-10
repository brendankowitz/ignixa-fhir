// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Collections.Generic;
using EnsureThat;
using Ignixa.Anonymizer.Exceptions;

namespace Ignixa.Anonymizer.Processors.Settings
{
    public class SubstituteSetting
    {
        public string ReplaceWith { get; set; }

        public static SubstituteSetting CreateFromRuleSettings(Dictionary<string, object> ruleSettings)
        {
            EnsureArg.IsNotNull(ruleSettings);

            string replaceWith = ruleSettings.GetValueOrDefault(RuleKeys.ReplaceWith)?.ToString();
            return new SubstituteSetting
            {
                ReplaceWith = replaceWith
            };
        }

        public static void ValidateRuleSettings(Dictionary<string, object> ruleSettings)
        {
            if (ruleSettings == null)
            {
                throw new AnonymizerConfigurationException("Substitute rule should not be null.");
            }

            if (!ruleSettings.ContainsKey(Constants.PathKey))
            {
                throw new AnonymizerConfigurationException("Missing path in FHIR path rule config.");
            }

            if (!ruleSettings.ContainsKey(RuleKeys.ReplaceWith))
            {
                throw new AnonymizerConfigurationException($"Missing replaceWith value in substitution rule at {ruleSettings[Constants.PathKey]}.");
            }

            return;
        }
    }
}

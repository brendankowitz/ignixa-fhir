// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
namespace Ignixa.Anonymizer.Configuration.ProcessorSettings
{
    /// <summary>
    /// Constant keys used to look up processor-specific settings in rule configuration dictionaries.
    /// </summary>
    internal static class RuleKeys
    {
        //perturb
        internal const string ReplaceWith = "replaceWith";
        internal const string RangeType = "rangeType";
        internal const string RoundTo = "roundTo";
        internal const string Span = "span";

        //generalize
        internal const string Cases = "cases";
        internal const string OtherValues = "otherValues";
    }
}
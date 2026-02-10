// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;

namespace Ignixa.Anonymizer.Exceptions
{
    public class AnonymizerRuleNotApplicableException : AnonymizerConfigurationException
    {
        public AnonymizerRuleNotApplicableException(string message) : base(message)
        {
        }

        public AnonymizerRuleNotApplicableException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}

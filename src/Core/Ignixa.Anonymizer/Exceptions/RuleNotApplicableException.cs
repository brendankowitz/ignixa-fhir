// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Anonymizer.Exceptions;

public class RuleNotApplicableException : Exception
{
    public RuleNotApplicableException()
    {
    }

    public RuleNotApplicableException(string message)
        : base(message)
    {
    }

    public RuleNotApplicableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

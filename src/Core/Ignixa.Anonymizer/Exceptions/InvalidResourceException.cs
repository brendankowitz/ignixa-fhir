// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Anonymizer.Exceptions;

public class InvalidResourceException : Exception
{
    public InvalidResourceException()
    {
    }

    public InvalidResourceException(string message)
        : base(message)
    {
    }

    public InvalidResourceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

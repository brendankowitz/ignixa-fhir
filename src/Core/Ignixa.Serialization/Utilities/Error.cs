/*
 * Copyright (c) 2016, Firely (info@fire.ly) and contributors
 * See the file CONTRIBUTORS for details.
 *
 * This file is licensed under the BSD 3-Clause license
 * available at https://raw.githubusercontent.com/FirelyTeam/firely-net-sdk/master/LICENSE
 */

namespace Ignixa.Serialization.Utilities;

/// <summary>
/// Utility class for creating and unwrapping <see cref="Exception"/> instances.
/// </summary>
// Class name from Firely SDK, intentionally kept for compatibility
#pragma warning disable CA1716 // Identifiers should not match keywords
public static class Error
{
    /// <summary>
    /// Creates a <see cref="NotSupportedException"/>.
    /// </summary>
    public static NotSupportedException NotSupported(string message)
    {
        return new NotSupportedException(message);
    }
}
#pragma warning restore CA1716 // Identifiers should not match keywords

// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Globalization;

namespace Ignixa.SourceNodeSerialization.Utilities;

/// <summary>
/// Simple FHIR primitive type converter.
/// Replaces Hl7.Fhir.Serialization.PrimitiveTypeConverter from Firely SDK.
/// </summary>
public static class PrimitiveTypeConverter
{
    /// <summary>
    /// Converts a value to the specified type.
    /// </summary>
    public static T ConvertTo<T>(object value)
    {
        if (value == null)
        {
            return default;
        }

        // If already the target type, return as-is
        if (value is T result)
        {
            return result;
        }

        var targetType = typeof(T);

        // String conversion
        if (targetType == typeof(string))
        {
            return (T)(object)value.ToString();
        }

        // DateTimeOffset conversion from FHIR datetime string
        if (targetType == typeof(DateTimeOffset) || targetType == typeof(DateTimeOffset?))
        {
            if (value is string str)
            {
                // FHIR dateTime format: YYYY-MM-DDThh:mm:ss+zz:zz
                if (DateTimeOffset.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
                {
                    return (T)(object)dto;
                }
            }
        }

        // Fallback to Convert.ChangeType
        try
        {
            return (T)Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
        }
        catch
        {
            return default;
        }
    }

    /// <summary>
    /// Converts a DateTimeOffset to FHIR dateTime format string.
    /// </summary>
    public static string ToFhirDateTime(this DateTimeOffset? value)
    {
        if (value == null)
        {
            return null;
        }

        // FHIR instant format: YYYY-MM-DDThh:mm:ss.ffffff+zz:zz (with fractional seconds)
        return value.Value.ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFFzzz", CultureInfo.InvariantCulture);
    }
}

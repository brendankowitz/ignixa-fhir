/*
  Copyright (c) 2011-2013, HL7, Inc.
  All rights reserved.
  
  Redistribution and use in source and binary forms, with or without modification, 
  are permitted provided that the following conditions are met:
  
   * Redistributions of source code must retain the above copyright notice, this 
     list of conditions and the following disclaimer.
   * Redistributions in binary form must reproduce the above copyright notice, 
     this list of conditions and the following disclaimer in the documentation 
     and/or other materials provided with the distribution.
   * Neither the name of HL7 nor the names of its contributors may be used to 
     endorse or promote products derived from this software without specific 
     prior written permission.
  
  THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND 
  ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED 
  WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
  IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, 
  INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT 
  NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR 
  PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, 
  WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) 
  ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
  POSSIBILITY OF SUCH DAMAGE.
  

*/

namespace Ignixa.Serialization;

/// <summary>
/// Attribute to specify the literal representation of an enum value for serialization.
/// This is a custom implementation to replace the removed Hl7.Fhir.Introspection.EnumLiteralAttribute from SDK 6.0.
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
public sealed class EnumLiteralAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EnumLiteralAttribute"/> class.
    /// </summary>
    /// <param name="literal">The literal string representation of the enum value.</param>
    public EnumLiteralAttribute(string literal)
    {
        Literal = literal;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EnumLiteralAttribute"/> class.
    /// </summary>
    /// <param name="literal">The literal string representation of the enum value.</param>
    /// <param name="system">The system URL for the enum value.</param>
    public EnumLiteralAttribute(string literal, string system)
    {
        Literal = literal;
        System = system;
    }

    /// <summary>
    /// Gets the literal string representation of the enum value.
    /// </summary>
    public string Literal { get; }

    /// <summary>
    /// Gets the system URL for the enum value (optional).
    /// </summary>
    public string? System { get; }
}

/// <summary>
/// Extension methods for enum types with EnumLiteralAttribute.
/// </summary>
public static class EnumLiteralExtensions
{
    /// <summary>
    /// Gets the literal string value from an enum decorated with EnumLiteralAttribute.
    /// </summary>
    /// <param name="value">The enum value.</param>
    /// <returns>The literal string, or the enum's ToString() if no attribute found.</returns>
    public static string GetLiteral(this Enum value)
    {
        var field = value.GetType().GetField(value.ToString());
        if (field == null) return value.ToString();

        var attribute = (EnumLiteralAttribute?)Attribute.GetCustomAttribute(field, typeof(EnumLiteralAttribute));
        return attribute?.Literal ?? value.ToString();
    }
}

/// <summary>
/// Utility methods for working with enums decorated with EnumLiteralAttribute.
/// </summary>
public static class EnumUtility
{
    /// <summary>
    /// Parses a literal string to an enum value by matching against EnumLiteralAttribute.
    /// </summary>
    /// <typeparam name="T">The enum type.</typeparam>
    /// <param name="literal">The literal string to parse.</param>
    /// <returns>The matching enum value, or null if no match found.</returns>
    public static T? ParseLiteral<T>(string? literal)
        where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(literal))
            return null;

        var enumType = typeof(T);
        foreach (var field in enumType.GetFields())
        {
            if (field.IsSpecialName) continue; // Skip value__ field

            var attribute = (EnumLiteralAttribute?)Attribute.GetCustomAttribute(field, typeof(EnumLiteralAttribute));
            if (attribute?.Literal == literal)
            {
                return (T?)field.GetValue(null);
            }
        }

        // Fallback: try parsing as regular enum name
        if (Enum.TryParse<T>(literal, ignoreCase: true, out var result))
        {
            return result;
        }

        return null;
    }
}

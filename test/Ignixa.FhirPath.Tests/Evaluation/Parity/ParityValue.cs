using System.Globalization;
using Ignixa.Abstractions;
using P = Hl7.Fhir.ElementModel.Types;

namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

internal static class ParityValue
{
    public static string Render(object? value, string? instanceType) =>
        $"{Carrier(value, instanceType)}|{RenderText(value)}";

    public static string RenderText(object? value) => value switch
    {
        null => "<null>",
        bool flag => flag ? "true" : "false",
        P.Boolean boolean => boolean.Value ? "true" : "false",
        FhirTemporal temporal => temporal.Literal,
        P.Date or P.DateTime or P.Time or P.Quantity => value.ToString() ?? "<null>",
        P.Decimal number => number.Value.ToString(CultureInfo.InvariantCulture),
        P.Integer integer => integer.Value.ToString(CultureInfo.InvariantCulture),
        P.Long duration => duration.Value.ToString(CultureInfo.InvariantCulture),
        P.String text => text.Value,
        P.Code code => code.Value ?? "<null>",
        string text => text,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "<null>"
    };

    private static string Carrier(object? value, string? instanceType) => value switch
    {
        null => "null",
        bool or P.Boolean => "boolean",
        byte or short or int or P.Integer => "integer",
        long or P.Long => "integer64",
        float or double or decimal or P.Decimal => "decimal",
        FhirTemporal or P.Date or P.DateTime or P.Time => $"temporal:{instanceType ?? "unknown"}",
        FhirQuantity or P.Quantity => "quantity",
        string or P.String or P.Code => "string",
        _ => value.GetType().FullName ?? value.GetType().Name
    };

}

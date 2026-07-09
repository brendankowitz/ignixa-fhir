// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Hl7v2.LogicalModel;
using NHapi.Base;
using NHapi.Base.Parser;
using NHapi.Base.Util;

namespace Ignixa.Hl7v2.Adapters;

public sealed class NhapiAdtA01LogicalModelAdapter
{
    public Hl7v2Element Parse(string er7)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(er7);

        var parser = new PipeParser();
        var message = parser.Parse(NormalizeEr7(er7));
        var terser = new Terser(message);

        var pid = new Hl7v2Element(
            "PID",
            "PID",
            children:
            [
                .. ReadPatientIdentifiers(terser),
                .. ReadPatientNames(terser),
                Value("dateTimeOfBirth", "TS", GetOptional(terser, "/PID-7")),
                Value("administrativeSex", "IS", GetOptional(terser, "/PID-8"))
            ],
            location: "msg.PID");

        return new Hl7v2Element(
            "msg",
            "Hl7v2AdtA01",
            children: [pid],
            location: "msg");
    }

    private static IEnumerable<IElement> ReadPatientIdentifiers(Terser terser)
    {
        for (var repetition = 0; ; repetition++)
        {
            var idNumber = GetOptional(terser, $"/PID-3({repetition})-1");
            if (string.IsNullOrEmpty(idNumber))
            {
                yield break;
            }

            var assigningAuthority = new Hl7v2Element(
                "assigningAuthority",
                "HD",
                children:
                [
                    Value("namespaceId", "IS", GetOptional(terser, $"/PID-3({repetition})-4-1"))
                ],
                location: $"msg.PID.patientIdentifierList[{repetition}].assigningAuthority");

            yield return new Hl7v2Element(
                "patientIdentifierList",
                "CX",
                children:
                [
                    Value("idNumber", "ST", idNumber),
                    assigningAuthority
                ],
                location: $"msg.PID.patientIdentifierList[{repetition}]");
        }
    }

    private static IEnumerable<IElement> ReadPatientNames(Terser terser)
    {
        for (var repetition = 0; ; repetition++)
        {
            var familyName = GetOptional(terser, $"/PID-5({repetition})-1-1");
            var givenName = GetOptional(terser, $"/PID-5({repetition})-2");
            if (string.IsNullOrEmpty(familyName) && string.IsNullOrEmpty(givenName))
            {
                yield break;
            }

            var family = new Hl7v2Element(
                "familyName",
                "FN",
                children:
                [
                    Value("surname", "ST", familyName)
                ],
                location: $"msg.PID.patientName[{repetition}].familyName");

            yield return new Hl7v2Element(
                "patientName",
                "XPN",
                children:
                [
                    family,
                    Value("givenName", "ST", givenName)
                ],
                location: $"msg.PID.patientName[{repetition}]");
        }
    }

    private static Hl7v2Element Value(string name, string instanceType, string? value)
    {
        return new Hl7v2Element(name, instanceType, value);
    }

    private static string? GetOptional(Terser terser, string path)
    {
        try
        {
            return terser.Get(path);
        }
        catch (HL7Exception)
        {
            return null;
        }
    }

    private static string NormalizeEr7(string er7)
    {
        return er7
            .Replace("\r\n", "\r", StringComparison.Ordinal)
            .Replace('\n', '\r')
            .Trim();
    }
}

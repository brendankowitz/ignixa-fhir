/*
 * Copyright (c) 2026, Ignixa Contributors
 *
 * Prototype tests for projecting NHapi-parsed HL7v2 messages into an Ignixa logical tree.
 */

using Shouldly;
using Ignixa.Hl7v2.Adapters;
using Ignixa.Hl7v2.LogicalModel;
using Xunit;

namespace Ignixa.Hl7v2.Tests;

public class NhapiAdtA01LogicalModelAdapterTests
{
    private const string AdtA01 = """
        MSH|^~\&|SendingApp|SendingFacility|ReceivingApp|ReceivingFacility|202607091200||ADT^A01|MSG00001|P|2.5
        EVN|A01|202607091200
        PID|1||12345^^^MRN^MR||Doe^Jane^A||19800101|F
        PV1|1|I
        """;

    [Fact]
    public void GivenAdtA01Er7_WhenProjectingToLogicalTree_ThenExposesPatientPathsUsedByFml()
    {
        // Arrange
        var adapter = new NhapiAdtA01LogicalModelAdapter();

        // Act
        var message = adapter.Parse(AdtA01);

        // Assert
        message.Name.ShouldBe("msg");
        message.InstanceType.ShouldBe("Hl7v2AdtA01");
        Hl7v2LogicalPath.SelectSingle(message, "msg.PID.patientIdentifierList[0].idNumber")?.Value.ShouldBe("12345");
        Hl7v2LogicalPath.SelectSingle(message, "msg.PID.patientIdentifierList[0].assigningAuthority.namespaceId")?.Value.ShouldBe("MRN");
        Hl7v2LogicalPath.SelectSingle(message, "msg.PID.patientName[0].familyName.surname")?.Value.ShouldBe("Doe");
        Hl7v2LogicalPath.SelectSingle(message, "msg.PID.patientName[0].givenName")?.Value.ShouldBe("Jane");
        Hl7v2LogicalPath.SelectSingle(message, "msg.PID.dateTimeOfBirth")?.Value.ShouldBe("19800101");
        Hl7v2LogicalPath.SelectSingle(message, "msg.PID.administrativeSex")?.Value.ShouldBe("F");
    }

    [Fact]
    public void GivenAdtA01Er7_WhenProjectingToLogicalTree_ThenPreservesRepeatedIdentifiers()
    {
        // Arrange
        var adapter = new NhapiAdtA01LogicalModelAdapter();
        var messageText = AdtA01.Replace(
            "12345^^^MRN^MR",
            "12345^^^MRN^MR~98765^^^SSN^SS",
            StringComparison.Ordinal);

        // Act
        var message = adapter.Parse(messageText);
        var patientIdentifierList = Hl7v2LogicalPath.SelectSingle(message, "msg.PID")!
            .Children("patientIdentifierList");

        // Assert
        patientIdentifierList.Count.ShouldBe(2);
        Hl7v2LogicalPath.SelectSingle(message, "msg.PID.patientIdentifierList[0].idNumber")?.Value.ShouldBe("12345");
        Hl7v2LogicalPath.SelectSingle(message, "msg.PID.patientIdentifierList[1].idNumber")?.Value.ShouldBe("98765");
    }
}

using Hl7.Fhir.ElementModel;
using Hl7.Fhir.FhirPath;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Hl7.FhirPath;
using Ignixa.Abstractions;
using Ignixa.Extensions.FirelySdk;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Search.Indexing.Converters;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Specification.Extensions;

namespace Ignixa.FhirPath.Phase3.R5.Tests;

public class R5NativeFirelyPhase3SliceTests
{
    private const string AppointmentJson = """
        {
          "resourceType": "Appointment",
          "status": "booked",
          "start": "2024-06-15T08:00:00Z",
          "requestedPeriod": [
            { "start": "2024-06-15T08:00:00Z" }
          ]
        }
        """;

    [Fact]
    public void GivenNativeFirelyAppointment_WhenFirstDateSelected_ThenKnownCarrierDivergenceIsVisible()
    {
        // Arrange
        const string expression = "(start | requestedPeriod.start).first()";
        ITypedElement input = new FhirJsonParser()
            .Parse<Resource>(AppointmentJson)
            .ToTypedElement();

        // Act
        ITypedElement firely = input
            .Select(expression, new Hl7.Fhir.FhirPath.FhirEvaluationContext())
            .ShouldHaveSingleItem();
        IElement ignixaInput = input.ToIgnixaElement();
        var context = new Ignixa.FhirPath.Evaluation.FhirEvaluationContext
        {
            Schema = FhirVersion.R5.GetSchemaProvider(),
            Resource = ignixaInput,
            RootResource = ignixaInput,
        };
        ITypedElement ignixa = Ignixa.FhirPath.Evaluation.TypedElementExtensions.Select(ignixaInput, expression, context)
            .Select(result => (ITypedElement)new TypedElementAdapter(result))
            .ShouldHaveSingleItem();
        DateTimeSearchValue firelyIndex = Convert(firely);
        DateTimeSearchValue ignixaIndex = Convert(ignixa);

        // Assert
        var instant = new DateTimeOffset(2024, 6, 15, 8, 0, 0, TimeSpan.Zero);
        firely.InstanceType.ShouldBe("System.DateTime");
        ignixa.InstanceType.ShouldBe("instant");
        firelyIndex.Start.ShouldBe(instant);
        firelyIndex.End.ShouldBe(instant.AddTicks(TimeSpan.TicksPerSecond - 1));
        ignixaIndex.Start.ShouldBe(instant);
        ignixaIndex.End.ShouldBe(instant);
    }

    private static DateTimeSearchValue Convert(ITypedElement result)
    {
        IElement element = new IgnixaElementAdapter(result);
        IElementToSearchValueConverter converter = result.InstanceType == "instant"
            ? new InstantToDateTimeSearchValueConverter()
            : new DateToDateTimeSearchValueConverter();

        return converter.ConvertTo(element).Cast<DateTimeSearchValue>().ShouldHaveSingleItem();
    }
}

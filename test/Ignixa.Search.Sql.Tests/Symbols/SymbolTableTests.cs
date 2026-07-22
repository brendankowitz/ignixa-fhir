using Ignixa.Search.Models;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Tests.Symbols;

public class SymbolTableTests
{
    [Fact]
    public void GivenAResolvedParameter_WhenLookedUp_ThenReturnsItsSearchParamId()
    {
        // Arrange
        var parameter = new SearchParameterInfo(
            "name",
            "name",
            SearchParamType.String,
            new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));

        var searchParamIds = new Dictionary<string, short>
        {
            ["http://hl7.org/fhir/SearchParameter/Patient-name"] = 202,
        };
        var resourceTypeIds = new Dictionary<string, short>
        {
            ["Patient"] = 103,
        };
        var symbolTable = new SymbolTable(searchParamIds, resourceTypeIds);

        // Act
        var searchParamId = symbolTable.SearchParamId(parameter);
        var resourceTypeId = symbolTable.ResourceTypeId("Patient");

        // Assert
        searchParamId.ShouldBe((short)202);
        resourceTypeId.ShouldBe((short)103);
    }

    [Fact]
    public void GivenAnUnresolvedParameter_WhenLookedUp_ThenThrows()
    {
        // Arrange
        var parameter = new SearchParameterInfo(
            "subject",
            "subject",
            SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));

        var symbolTable = new SymbolTable(
            new Dictionary<string, short>(),
            new Dictionary<string, short>());

        // Act / Assert
        var exception = Should.Throw<KeyNotFoundException>(() => symbolTable.SearchParamId(parameter));
        exception.Message.ShouldContain("http://hl7.org/fhir/SearchParameter/Observation-subject");
        exception.Message.ShouldContain("Resolve should have resolved every parameter Lower will need");
    }

    [Fact]
    public void GivenAnUnresolvedResourceType_WhenLookedUp_ThenThrows()
    {
        // Arrange
        var symbolTable = new SymbolTable(
            new Dictionary<string, short>(),
            new Dictionary<string, short>());

        // Act / Assert
        var exception = Should.Throw<KeyNotFoundException>(() => symbolTable.ResourceTypeId("Patient"));
        exception.Message.ShouldContain("Patient");
    }

    [Fact]
    public void GivenTwoResourceTypesSharingASearchParamCode_WhenLookedUpByUrl_ThenEachResolvesToItsOwnSearchParamId()
    {
        // Arrange -- same code "name" on two different resource types has two different canonical
        // URLs and two different SearchParamIds; Url is the disambiguating key, not (type, code).
        var patientName = new SearchParameterInfo(
            "name",
            "name",
            SearchParamType.String,
            new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var practitionerName = new SearchParameterInfo(
            "name",
            "name",
            SearchParamType.String,
            new Uri("http://hl7.org/fhir/SearchParameter/Practitioner-name"));

        var symbolTable = new SymbolTable(
            new Dictionary<string, short>
            {
                ["http://hl7.org/fhir/SearchParameter/Patient-name"] = 202,
                ["http://hl7.org/fhir/SearchParameter/Practitioner-name"] = 305,
            },
            new Dictionary<string, short>());

        // Act
        var patientNameId = symbolTable.SearchParamId(patientName);
        var practitionerNameId = symbolTable.SearchParamId(practitionerName);

        // Assert
        patientNameId.ShouldBe((short)202);
        practitionerNameId.ShouldBe((short)305);
    }

    [Fact]
    public void GivenAParameterWithNoUrl_WhenLookedUp_ThenThrowsWithInformativeMessage()
    {
        // Arrange -- SearchParameterInfo's (name, code) constructor leaves Url null.
        var parameter = new SearchParameterInfo("name", "name");

        var symbolTable = new SymbolTable(
            new Dictionary<string, short>(),
            new Dictionary<string, short>());

        // Act / Assert
        var exception = Should.Throw<KeyNotFoundException>(() => symbolTable.SearchParamId(parameter));
        exception.Message.ShouldContain("name");
        exception.Message.ShouldContain("Url is null");
    }

    [Fact]
    public void GivenACompartmentMembershipMap_WhenLookedUp_ThenReturnsTheStoredGroups()
    {
        // Arrange
        var subjectParam = new SearchParameterInfo("subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));
        var membership = new Dictionary<string, IReadOnlyList<(SearchParameterInfo, IReadOnlyList<string>)>>
        {
            ["Patient"] = [(subjectParam, ["Observation", "Condition"])],
        };
        var symbolTable = new SymbolTable(
            new Dictionary<string, short>(),
            new Dictionary<string, short>(),
            membership);

        // Act
        var result = symbolTable.CompartmentMembership("Patient");

        // Assert
        result.Count.ShouldBe(1);
        result[0].Parameter.ShouldBe(subjectParam);
        result[0].ResourceTypes.ShouldBe(["Observation", "Condition"]);
    }

    [Fact]
    public void GivenNoCompartmentMembershipWasResolved_WhenLookedUp_ThenThrowsKeyNotFoundException()
    {
        // Arrange
        var symbolTable = new SymbolTable(new Dictionary<string, short>(), new Dictionary<string, short>());

        // Act & Assert
        Should.Throw<KeyNotFoundException>(() => symbolTable.CompartmentMembership("Patient"));
    }

    // ── SystemId three-state contract ─────────────────────────────────────────────────────────────

    [Fact]
    public void GivenAResolvedSystemId_WhenLookedUp_ThenReturnsItsValue()
    {
        // Arrange
        var symbolTable = new SymbolTable(
            new Dictionary<string, short>(),
            new Dictionary<string, short>(),
            systemIds: new Dictionary<string, int?> { ["http://loinc.org"] = 7 });

        // Act / Assert
        symbolTable.SystemId("http://loinc.org").ShouldBe(7);
    }

    [Fact]
    public void GivenACollectedButMissingSystemId_WhenLookedUp_ThenReturnsNull()
    {
        // Arrange -- resolver returned null; entry is present in the map with a null value (known miss)
        var symbolTable = new SymbolTable(
            new Dictionary<string, short>(),
            new Dictionary<string, short>(),
            systemIds: new Dictionary<string, int?> { ["http://known-but-missing.example"] = null });

        // Act / Assert
        symbolTable.SystemId("http://known-but-missing.example").ShouldBeNull();
    }

    [Fact]
    public void GivenAnUncollectedSystemId_WhenLookedUp_ThenThrowsKeyNotFoundException()
    {
        // Arrange -- the system was never collected; an empty map is used.
        var symbolTable = new SymbolTable(
            new Dictionary<string, short>(),
            new Dictionary<string, short>());

        // Act / Assert
        Should.Throw<KeyNotFoundException>(() => symbolTable.SystemId("http://never-collected.example"));
    }

    // ── QuantityCodeId three-state contract ───────────────────────────────────────────────────────

    [Fact]
    public void GivenAResolvedQuantityCodeId_WhenLookedUp_ThenReturnsItsValue()
    {
        // Arrange
        var symbolTable = new SymbolTable(
            new Dictionary<string, short>(),
            new Dictionary<string, short>(),
            quantityCodeIds: new Dictionary<string, int?> { ["mg"] = 42 });

        // Act / Assert
        symbolTable.QuantityCodeId("mg").ShouldBe(42);
    }

    [Fact]
    public void GivenACollectedButMissingQuantityCodeId_WhenLookedUp_ThenReturnsNull()
    {
        // Arrange -- resolver returned null; entry is present in the map with a null value (known miss)
        var symbolTable = new SymbolTable(
            new Dictionary<string, short>(),
            new Dictionary<string, short>(),
            quantityCodeIds: new Dictionary<string, int?> { ["unknown-code"] = null });

        // Act / Assert
        symbolTable.QuantityCodeId("unknown-code").ShouldBeNull();
    }

    [Fact]
    public void GivenAnUncollectedQuantityCodeId_WhenLookedUp_ThenThrowsKeyNotFoundException()
    {
        // Arrange -- the code was never collected; an empty map is used.
        var symbolTable = new SymbolTable(
            new Dictionary<string, short>(),
            new Dictionary<string, short>());

        // Act / Assert
        Should.Throw<KeyNotFoundException>(() => symbolTable.QuantityCodeId("never-collected"));
    }
}

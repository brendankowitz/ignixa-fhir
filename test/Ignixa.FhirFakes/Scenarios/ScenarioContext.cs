// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization.SourceNodes;

namespace Ignixa.FhirFakes.Scenarios;

/// <summary>
/// Holds the state of a scenario during and after generation.
/// Contains the patient, all generated resources, timeline, and attributes.
/// </summary>
public sealed class ScenarioContext
{
    private readonly List<ResourceJsonNode> _encounters = [];
    private readonly List<ResourceJsonNode> _conditions = [];
    private readonly List<ResourceJsonNode> _observations = [];
    private readonly List<ResourceJsonNode> _medications = [];
    private readonly List<ResourceJsonNode> _procedures = [];
    private readonly List<ResourceJsonNode> _diagnosticReports = [];
    private readonly List<ResourceJsonNode> _immunizations = [];
    private readonly List<ResourceJsonNode> _allergies = [];
    private readonly List<ResourceJsonNode> _allResources = [];
    private readonly List<ScenarioEvent> _timeline = [];
    private readonly Dictionary<string, object> _attributes = [];

    /// <summary>
    /// Gets or sets the scenario name.
    /// </summary>
    public string ScenarioName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the scenario description.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the patient resource.
    /// </summary>
    public ResourceJsonNode? Patient { get; set; }

    /// <summary>
    /// Gets or sets the current simulation time.
    /// Used for temporal sequencing of resources.
    /// </summary>
    public DateTime CurrentTime { get; set; } = DateTime.UtcNow.AddYears(-1);

    /// <summary>
    /// Gets or sets the patient's birth date.
    /// Used for age calculations.
    /// </summary>
    public DateTime BirthDate { get; set; }

    /// <summary>
    /// Gets the current encounter context (most recent encounter).
    /// Resources like Observations and Conditions can reference this.
    /// </summary>
    public ResourceJsonNode? CurrentEncounter { get; private set; }

    /// <summary>
    /// Gets all encounter resources generated in this scenario.
    /// </summary>
    public IReadOnlyList<ResourceJsonNode> Encounters => _encounters;

    /// <summary>
    /// Gets all condition resources generated in this scenario.
    /// </summary>
    public IReadOnlyList<ResourceJsonNode> Conditions => _conditions;

    /// <summary>
    /// Gets all observation resources generated in this scenario.
    /// </summary>
    public IReadOnlyList<ResourceJsonNode> Observations => _observations;

    /// <summary>
    /// Gets all medication request resources generated in this scenario.
    /// </summary>
    public IReadOnlyList<ResourceJsonNode> Medications => _medications;

    /// <summary>
    /// Gets all procedure resources generated in this scenario.
    /// </summary>
    public IReadOnlyList<ResourceJsonNode> Procedures => _procedures;

    /// <summary>
    /// Gets all diagnostic report resources generated in this scenario.
    /// </summary>
    public IReadOnlyList<ResourceJsonNode> DiagnosticReports => _diagnosticReports;

    /// <summary>
    /// Gets all immunization resources generated in this scenario.
    /// </summary>
    public IReadOnlyList<ResourceJsonNode> Immunizations => _immunizations;

    /// <summary>
    /// Gets all allergy intolerance resources generated in this scenario.
    /// </summary>
    public IReadOnlyList<ResourceJsonNode> Allergies => _allergies;

    /// <summary>
    /// Gets all resources generated in this scenario (in generation order).
    /// </summary>
    public IReadOnlyList<ResourceJsonNode> AllResources => _allResources;

    /// <summary>
    /// Gets the timeline of events in chronological order.
    /// </summary>
    public IReadOnlyList<ScenarioEvent> Timeline => _timeline;

    /// <summary>
    /// Gets the scenario attributes (key-value store for custom state).
    /// Examples: "diabetes_severity", "blood_pressure_controlled", etc.
    /// </summary>
    public IReadOnlyDictionary<string, object> Attributes => _attributes;

    /// <summary>
    /// Gets the current age of the patient in years.
    /// </summary>
    public int CurrentAge => (int)((CurrentTime - BirthDate).TotalDays / 365.25);

    /// <summary>
    /// Adds an encounter resource to the scenario.
    /// </summary>
    public void AddEncounter(ResourceJsonNode encounter, string description)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        _encounters.Add(encounter);
        _allResources.Add(encounter);
        CurrentEncounter = encounter;
        AddTimelineEvent("Encounter", encounter.Id, "Encounter", description);
    }

    /// <summary>
    /// Adds a condition resource to the scenario.
    /// </summary>
    public void AddCondition(ResourceJsonNode condition, string description)
    {
        ArgumentNullException.ThrowIfNull(condition);
        _conditions.Add(condition);
        _allResources.Add(condition);
        AddTimelineEvent("ConditionOnset", condition.Id, "Condition", description);
    }

    /// <summary>
    /// Adds an observation resource to the scenario.
    /// </summary>
    public void AddObservation(ResourceJsonNode observation, string description)
    {
        ArgumentNullException.ThrowIfNull(observation);
        _observations.Add(observation);
        _allResources.Add(observation);
        AddTimelineEvent("Observation", observation.Id, "Observation", description);
    }

    /// <summary>
    /// Adds a medication request resource to the scenario.
    /// </summary>
    public void AddMedication(ResourceJsonNode medication, string description)
    {
        ArgumentNullException.ThrowIfNull(medication);
        _medications.Add(medication);
        _allResources.Add(medication);
        AddTimelineEvent("MedicationOrder", medication.Id, "MedicationRequest", description);
    }

    /// <summary>
    /// Adds a procedure resource to the scenario.
    /// </summary>
    public void AddProcedure(ResourceJsonNode procedure, string description)
    {
        ArgumentNullException.ThrowIfNull(procedure);
        _procedures.Add(procedure);
        _allResources.Add(procedure);
        AddTimelineEvent("Procedure", procedure.Id, "Procedure", description);
    }

    /// <summary>
    /// Adds a diagnostic report resource to the scenario.
    /// </summary>
    public void AddDiagnosticReport(ResourceJsonNode diagnosticReport, string description)
    {
        ArgumentNullException.ThrowIfNull(diagnosticReport);
        _diagnosticReports.Add(diagnosticReport);
        _allResources.Add(diagnosticReport);
        AddTimelineEvent("DiagnosticReport", diagnosticReport.Id, "DiagnosticReport", description);
    }

    /// <summary>
    /// Adds an immunization resource to the scenario.
    /// </summary>
    public void AddImmunization(ResourceJsonNode immunization, string description)
    {
        ArgumentNullException.ThrowIfNull(immunization);
        _immunizations.Add(immunization);
        _allResources.Add(immunization);
        AddTimelineEvent("Immunization", immunization.Id, "Immunization", description);
    }

    /// <summary>
    /// Adds an allergy intolerance resource to the scenario.
    /// </summary>
    public void AddAllergy(ResourceJsonNode allergy, string description)
    {
        ArgumentNullException.ThrowIfNull(allergy);
        _allergies.Add(allergy);
        _allResources.Add(allergy);
        AddTimelineEvent("AllergyIntolerance", allergy.Id, "AllergyIntolerance", description);
    }

    /// <summary>
    /// Sets an attribute value.
    /// </summary>
    public void SetAttribute(string name, object value)
    {
        ArgumentNullException.ThrowIfNull(name);
        _attributes[name] = value;
    }

    /// <summary>
    /// Gets an attribute value, returning default if not found.
    /// </summary>
    public T GetAttribute<T>(string name, T defaultValue = default!)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_attributes.TryGetValue(name, out var value) && value is T typedValue)
        {
            return typedValue;
        }
        return defaultValue;
    }

    /// <summary>
    /// Checks if an attribute exists.
    /// </summary>
    public bool HasAttribute(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _attributes.ContainsKey(name);
    }

    /// <summary>
    /// Advances the current simulation time by the specified duration.
    /// </summary>
    public void AdvanceTime(TimeSpan duration)
    {
        CurrentTime = CurrentTime.Add(duration);
    }

    /// <summary>
    /// Adds an event to the timeline.
    /// </summary>
    private void AddTimelineEvent(string eventType, string resourceId, string resourceType, string description)
    {
        _timeline.Add(new ScenarioEvent(CurrentTime, eventType, resourceId, resourceType, description));
    }
}

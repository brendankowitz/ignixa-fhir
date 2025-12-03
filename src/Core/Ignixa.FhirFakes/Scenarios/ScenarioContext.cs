// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Serialization.Models;
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
    private readonly List<ResourceJsonNode> _practitioners = [];
    private readonly List<ResourceJsonNode> _organizations = [];
    private readonly List<ResourceJsonNode> _coverages = [];
    private readonly List<ResourceJsonNode> _serviceRequests = [];
    private readonly List<ResourceJsonNode> _goals = [];
    private readonly List<ResourceJsonNode> _carePlans = [];
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
    /// Gets all practitioner resources generated in this scenario.
    /// </summary>
    public IReadOnlyList<ResourceJsonNode> Practitioners => _practitioners;

    /// <summary>
    /// Gets the current practitioner context (most recently added practitioner).
    /// Resources like Encounters, Procedures, and MedicationRequests can reference this.
    /// </summary>
    public ResourceJsonNode? CurrentPractitioner { get; private set; }

    /// <summary>
    /// Gets all organization resources generated in this scenario.
    /// </summary>
    public IReadOnlyList<ResourceJsonNode> Organizations => _organizations;

    /// <summary>
    /// Gets the current organization context (most recently added organization).
    /// Resources like Encounters, Practitioners, and Coverage can reference this.
    /// </summary>
    public ResourceJsonNode? CurrentOrganization { get; private set; }

    /// <summary>
    /// Gets all coverage resources generated in this scenario.
    /// </summary>
    public IReadOnlyList<ResourceJsonNode> Coverages => _coverages;

    /// <summary>
    /// Gets the current coverage context (most recent coverage).
    /// </summary>
    public ResourceJsonNode? CurrentCoverage { get; private set; }

    /// <summary>
    /// Gets all service request resources generated in this scenario.
    /// </summary>
    public IReadOnlyList<ResourceJsonNode> ServiceRequests => _serviceRequests;

    /// <summary>
    /// Gets the current service request context (most recent service request).
    /// Resources like Tasks and DiagnosticReports can reference this.
    /// </summary>
    public ResourceJsonNode? CurrentServiceRequest { get; private set; }

    /// <summary>
    /// Gets all goal resources generated in this scenario.
    /// Goals define desired health outcomes that care plans aim to achieve.
    /// </summary>
    public IReadOnlyList<ResourceJsonNode> Goals => _goals;

    /// <summary>
    /// Gets the current goal context (most recently added goal).
    /// CarePlans can reference goals for care coordination.
    /// </summary>
    public ResourceJsonNode? CurrentGoal { get; private set; }

    /// <summary>
    /// Gets all care plan resources generated in this scenario.
    /// CarePlans define activities and interventions to achieve goals.
    /// </summary>
    public IReadOnlyList<ResourceJsonNode> CarePlans => _carePlans;

    /// <summary>
    /// Gets the current care plan context (most recently added care plan).
    /// </summary>
    public ResourceJsonNode? CurrentCarePlan { get; private set; }

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
    /// Records a condition end event in the timeline.
    /// Used by ConditionEndState when a condition is resolved/ended.
    /// </summary>
    /// <param name="conditionId">The condition resource ID.</param>
    /// <param name="description">Description of the end event.</param>
    public void RecordConditionEnd(string conditionId, string description)
    {
        ArgumentNullException.ThrowIfNull(conditionId);
        AddTimelineEvent("ConditionEnd", conditionId, "Condition", description);
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
    /// Adds a practitioner resource to the scenario.
    /// </summary>
    /// <param name="practitioner">The practitioner resource to add.</param>
    /// <param name="specialty">The practitioner's specialty description for timeline event.</param>
    public void AddPractitioner(ResourceJsonNode practitioner, string specialty)
    {
        ArgumentNullException.ThrowIfNull(practitioner);
        _practitioners.Add(practitioner);
        _allResources.Add(practitioner);
        AddTimelineEvent("Practitioner", practitioner.Id, "Practitioner", specialty);
    }

    /// <summary>
    /// Sets the current practitioner context.
    /// Subsequent resources (Encounters, Procedures, etc.) can reference this practitioner.
    /// </summary>
    /// <param name="practitioner">The practitioner to set as current.</param>
    public void SetCurrentPractitioner(ResourceJsonNode practitioner)
    {
        ArgumentNullException.ThrowIfNull(practitioner);
        CurrentPractitioner = practitioner;
    }

    /// <summary>
    /// Adds an organization resource to the scenario.
    /// </summary>
    /// <param name="organization">The organization resource to add.</param>
    /// <param name="name">The organization's name for timeline event.</param>
    /// <param name="setAsCurrent">If true, sets this organization as the current context.</param>
    public void AddOrganization(ResourceJsonNode organization, string name, bool setAsCurrent = true)
    {
        ArgumentNullException.ThrowIfNull(organization);
        _organizations.Add(organization);
        _allResources.Add(organization);
        if (setAsCurrent)
        {
            CurrentOrganization = organization;
        }
        AddTimelineEvent("Organization", organization.Id, "Organization", name);
    }

    /// <summary>
    /// Sets the current organization context.
    /// Subsequent resources (Encounters, Practitioners, Coverage, etc.) can reference this organization.
    /// </summary>
    /// <param name="organization">The organization to set as current.</param>
    public void SetCurrentOrganization(ResourceJsonNode organization)
    {
        ArgumentNullException.ThrowIfNull(organization);
        CurrentOrganization = organization;
    }

    /// <summary>
    /// Adds a coverage resource to the scenario.
    /// </summary>
    public void AddCoverage(ResourceJsonNode coverage, string description)
    {
        ArgumentNullException.ThrowIfNull(coverage);
        _coverages.Add(coverage);
        _allResources.Add(coverage);
        CurrentCoverage = coverage;
        AddTimelineEvent("Coverage", coverage.Id, "Coverage", description);
    }

    /// <summary>
    /// Adds a service request resource to the scenario.
    /// </summary>
    /// <param name="serviceRequest">The service request resource to add.</param>
    /// <param name="description">Description for the timeline event (typically the service name).</param>
    public void AddServiceRequest(ResourceJsonNode serviceRequest, string description)
    {
        ArgumentNullException.ThrowIfNull(serviceRequest);
        _serviceRequests.Add(serviceRequest);
        _allResources.Add(serviceRequest);
        CurrentServiceRequest = serviceRequest;
        AddTimelineEvent("ServiceRequest", serviceRequest.Id, "ServiceRequest", description);
    }

    /// <summary>
    /// Adds a goal resource to the scenario.
    /// Goals define desired health outcomes that care plans and interventions aim to achieve.
    /// </summary>
    /// <param name="goal">The goal resource to add.</param>
    /// <param name="description">Description for the timeline event (typically the goal description).</param>
    public void AddGoal(ResourceJsonNode goal, string description)
    {
        ArgumentNullException.ThrowIfNull(goal);
        _goals.Add(goal);
        _allResources.Add(goal);
        CurrentGoal = goal;
        AddTimelineEvent("Goal", goal.Id, "Goal", description);
    }

    /// <summary>
    /// Adds a care plan resource to the scenario.
    /// CarePlans define activities and interventions to achieve goals and coordinate care.
    /// </summary>
    /// <param name="carePlan">The care plan resource to add.</param>
    /// <param name="title">Title for the timeline event (the care plan title).</param>
    public void AddCarePlan(ResourceJsonNode carePlan, string title)
    {
        ArgumentNullException.ThrowIfNull(carePlan);
        _carePlans.Add(carePlan);
        _allResources.Add(carePlan);
        CurrentCarePlan = carePlan;
        AddTimelineEvent("CarePlan", carePlan.Id, "CarePlan", title);
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

    /// <summary>
    /// Creates a transaction Bundle containing all resources from this scenario.
    /// The Patient resource is added first, followed by all other resources in generation order.
    /// Each entry uses urn:uuid references for client-assigned IDs.
    /// </summary>
    /// <returns>A BundleJsonNode representing a FHIR transaction bundle.</returns>
    public BundleJsonNode ToBundle()
    {
        var entries = new JsonArray();

        // Add Patient first (if present)
        if (Patient is not null)
        {
            entries.Add(CreateBundleEntry(Patient));
        }

        // Add all other resources in generation order
        foreach (var resource in _allResources.Where(r => r.ResourceType != "Patient"))
        {
            entries.Add(CreateBundleEntry(resource));
        }

        // Create the bundle
        var bundleNode = new JsonObject
        {
            ["resourceType"] = "Bundle",
            ["id"] = Guid.NewGuid().ToString(),
            ["type"] = "transaction",
            ["entry"] = entries
        };

        return new BundleJsonNode(bundleNode);
    }

    /// <summary>
    /// Creates a bundle entry for a resource with POST request.
    /// </summary>
    private static JsonObject CreateBundleEntry(ResourceJsonNode resource)
    {
        return new JsonObject
        {
            ["fullUrl"] = $"urn:uuid:{resource.Id}",
            ["resource"] = resource.MutableNode.DeepClone(),
            ["request"] = new JsonObject
            {
                ["method"] = "POST",
                ["url"] = resource.ResourceType
            }
        };
    }
}

// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Scenarios.Codes;
using Ignixa.FhirFakes.Scenarios.States;
using Ignixa.Specification;

namespace Ignixa.FhirFakes.Scenarios.Predefined.Server;

/// <summary>
/// Provides test scenarios specifically designed for testing Microsoft Health-style chained search functionality.
/// Each scenario creates minimal resources with clear, searchable patterns to validate forward chains,
/// reverse chains (_has), and multi-hop chains.
/// </summary>
/// <remarks>
/// These scenarios are designed for integration testing of FHIR chained search capabilities:
/// <list type="bullet">
///   <item><description>Forward chains: Patient?organization.name=X</description></item>
///   <item><description>Reverse chains: Organization?_has:Patient:organization:family=Smith</description></item>
///   <item><description>Multi-hop chains: Observation?subject:Patient.organization.name=X</description></item>
/// </list>
///
/// Each scenario uses distinct, searchable values (names, identifiers, dates) to ensure
/// unambiguous test assertions.
/// </remarks>
public static class ChainedSearchTestScenario
{
    #region Scenario 1: Patient-Organization Chain Test

    /// <summary>
    /// Creates a scenario for testing Patient-Organization chained searches.
    ///
    /// Tests the following search patterns:
    /// <list type="bullet">
    ///   <item><description>Patient?organization.name=Boston Medical Center</description></item>
    ///   <item><description>Patient?organization.address-city=Seattle</description></item>
    ///   <item><description>Patient?organization.address-state=FL</description></item>
    ///   <item><description>Organization?_has:Patient:organization:family=Smith</description></item>
    ///   <item><description>Organization?_has:Patient:organization:gender=female</description></item>
    /// </list>
    ///
    /// Generated Resources:
    /// <list type="bullet">
    ///   <item><description>3 Organizations (Boston MA, Seattle WA, Miami FL)</description></item>
    ///   <item><description>9 Patients (3 per organization with distinct family names)</description></item>
    /// </list>
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <returns>A scenario context with 3 organizations and 9 patients for chained search testing.</returns>
    public static ScenarioContext GetPatientOrganizationChainTest(
        this IFhirSchemaProvider schemaProvider)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);

        return new ScenarioBuilder(schemaProvider)
            .WithName("Patient-Organization Chain Test")
            .WithDescription("Tests Patient->Organization forward chains and Organization<-Patient reverse chains (_has)")

            // Organization 1: Boston Medical Center
            .AddOrganization(new OrganizationState
            {
                Name = "Organization_BostonMedicalCenter",
                OrganizationName = "Boston Medical Center",
                Type = OrganizationState.OrganizationTypes.HealthcareProvider,
                Address = new OrganizationAddress(
                    Line: "725 Albany Street",
                    City: "Boston",
                    State: "MA",
                    PostalCode: "02118")
            })

            // Patients at Boston (3 patients)
            .WithPatient(age: 45, gender: "male", givenName: "John", familyName: "Smith")
            .WithPatient(age: 32, gender: "female", givenName: "Emily", familyName: "Johnson")
            .WithPatient(age: 67, gender: "male", givenName: "Robert", familyName: "Williams")

            // Organization 2: Seattle General Hospital
            .AddOrganization(new OrganizationState
            {
                Name = "Organization_SeattleGeneralHospital",
                OrganizationName = "Seattle General Hospital",
                Type = OrganizationState.OrganizationTypes.HealthcareProvider,
                Address = new OrganizationAddress(
                    Line: "1959 NE Pacific Street",
                    City: "Seattle",
                    State: "WA",
                    PostalCode: "98195")
            })

            // Patients at Seattle (3 patients)
            .WithPatient(age: 28, gender: "female", givenName: "Sarah", familyName: "Brown")
            .WithPatient(age: 51, gender: "male", givenName: "Michael", familyName: "Davis")
            .WithPatient(age: 39, gender: "female", givenName: "Jennifer", familyName: "Miller")

            // Organization 3: Miami Community Clinic
            .AddOrganization(new OrganizationState
            {
                Name = "Organization_MiamiCommunityClinc",
                OrganizationName = "Miami Community Clinic",
                Type = OrganizationState.OrganizationTypes.HealthcareProvider,
                Address = new OrganizationAddress(
                    Line: "1611 NW 12th Avenue",
                    City: "Miami",
                    State: "FL",
                    PostalCode: "33136")
            })

            // Patients at Miami (3 patients)
            .WithPatient(age: 42, gender: "male", givenName: "David", familyName: "Anderson")
            .WithPatient(age: 35, gender: "female", givenName: "Lisa", familyName: "Taylor")
            .WithPatient(age: 58, gender: "male", givenName: "James", familyName: "Thomas")

            .Build();
    }

    #endregion

    #region Scenario 2: Observation-Patient Chain Test

    /// <summary>
    /// Creates a scenario for testing Observation-Patient chained searches with clinical observations.
    ///
    /// Tests the following search patterns:
    /// <list type="bullet">
    ///   <item><description>Observation?subject:Patient.name=Alice</description></item>
    ///   <item><description>Observation?subject:Patient.family=Smith</description></item>
    ///   <item><description>Observation?subject:Patient.birthdate=1980-03-15</description></item>
    ///   <item><description>Observation?code=2339-0 (Glucose)</description></item>
    ///   <item><description>Patient?_has:Observation:patient:code=2339-0</description></item>
    ///   <item><description>Patient?_has:Observation:patient:value-quantity=gt100</description></item>
    /// </list>
    ///
    /// Generated Resources:
    /// <list type="bullet">
    ///   <item><description>1 Organization (Central Medical Group)</description></item>
    ///   <item><description>1 Practitioner (Dr. Primary)</description></item>
    ///   <item><description>3 Patients (Alice Smith 1980, Bob Johnson 1990, Carol Williams 2000)</description></item>
    ///   <item><description>9 Observations (3 per patient: Glucose, BP, Cholesterol)</description></item>
    /// </list>
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <returns>A scenario context for Observation-Patient chain testing.</returns>
    public static ScenarioContext GetObservationPatientChainTest(
        this IFhirSchemaProvider schemaProvider)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);

        return new ScenarioBuilder(schemaProvider)
            .WithName("Observation-Patient Chain Test")
            .WithDescription("Tests Observation->Patient forward chains and Patient<-Observation reverse chains with clinical data")

            // Setup organization and practitioner
            .AddOrganization(new OrganizationState
            {
                Name = "Organization_CentralMedicalGroup",
                OrganizationName = "Central Medical Group",
                Type = OrganizationState.OrganizationTypes.HealthcareProvider,
                Address = new OrganizationAddress(
                    Line: "100 Medical Plaza",
                    City: "Chicago",
                    State: "IL",
                    PostalCode: "60601")
            })
            .AddPractitioner(new PractitionerState
            {
                Specialty = Specialties.FamilyMedicine,
                GivenName = "Primary",
                FamilyName = "Care",
                Gender = "male"
            })

            // Patient 1: Alice Smith (born 1980-03-15)
            .WithPatient(age: 44, gender: "female", givenName: "Alice", familyName: "Smith",
                startDate: new DateTime(2024, 3, 15))
            .AddEncounter("Annual Physical - Alice Smith")
            .AddObservation(new ObservationState
            {
                Name = "Observation_Glucose_Alice",
                Code = LabObservations.Glucose,
                Value = 105m,
                Unit = "mg/dL",
                UnitCode = "mg/dL"
            })
            .AddObservation(ObservationState.BloodPressure(systolic: 125, diastolic: 82))
            .AddObservation(new ObservationState
            {
                Name = "Observation_Cholesterol_Alice",
                Code = LabObservations.TotalCholesterol,
                Value = 195m,
                Unit = "mg/dL",
                UnitCode = "mg/dL"
            })

            // Patient 2: Bob Johnson (born 1990-07-22)
            .WithPatient(age: 34, gender: "male", givenName: "Bob", familyName: "Johnson",
                startDate: new DateTime(2024, 7, 22))
            .AddEncounter("Annual Physical - Bob Johnson")
            .AddObservation(new ObservationState
            {
                Name = "Observation_Glucose_Bob",
                Code = LabObservations.Glucose,
                Value = 92m,
                Unit = "mg/dL",
                UnitCode = "mg/dL"
            })
            .AddObservation(ObservationState.BloodPressure(systolic: 118, diastolic: 76))
            .AddObservation(new ObservationState
            {
                Name = "Observation_Cholesterol_Bob",
                Code = LabObservations.TotalCholesterol,
                Value = 178m,
                Unit = "mg/dL",
                UnitCode = "mg/dL"
            })

            // Patient 3: Carol Williams (born 2000-11-05)
            .WithPatient(age: 24, gender: "female", givenName: "Carol", familyName: "Williams",
                startDate: new DateTime(2024, 11, 5))
            .AddEncounter("Annual Physical - Carol Williams")
            .AddObservation(new ObservationState
            {
                Name = "Observation_Glucose_Carol",
                Code = LabObservations.Glucose,
                Value = 88m,
                Unit = "mg/dL",
                UnitCode = "mg/dL"
            })
            .AddObservation(ObservationState.BloodPressure(systolic: 112, diastolic: 70))
            .AddObservation(new ObservationState
            {
                Name = "Observation_Cholesterol_Carol",
                Code = LabObservations.TotalCholesterol,
                Value = 165m,
                Unit = "mg/dL",
                UnitCode = "mg/dL"
            })

            .Build();
    }

    #endregion

    #region Scenario 3: Three-Hop Observation Chain Test

    /// <summary>
    /// Creates a scenario for testing three-hop chained searches (Observation->Patient->Organization).
    ///
    /// Tests the following search patterns:
    /// <list type="bullet">
    ///   <item><description>Observation?subject:Patient.organization.name=Boston Medical Center</description></item>
    ///   <item><description>Observation?subject:Patient.organization.address-state=MA</description></item>
    ///   <item><description>Observation?subject:Patient.organization.address-city=Seattle</description></item>
    ///   <item><description>Observation?code=2339-0&amp;subject:Patient.organization.name=Boston</description></item>
    /// </list>
    ///
    /// Generated Resources:
    /// <list type="bullet">
    ///   <item><description>2 Organizations (Boston MA, Seattle WA)</description></item>
    ///   <item><description>4 Patients (2 per organization)</description></item>
    ///   <item><description>12 Observations (3 per patient: Glucose, BP, HbA1c)</description></item>
    /// </list>
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <returns>A scenario context for three-hop chain testing.</returns>
    public static ScenarioContext GetThreeHopObservationChainTest(
        this IFhirSchemaProvider schemaProvider)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);

        return new ScenarioBuilder(schemaProvider)
            .WithName("Three-Hop Observation Chain Test")
            .WithDescription("Tests Observation->Patient->Organization three-hop forward chains")

            // Organization 1: Boston Medical Center
            .AddOrganization(new OrganizationState
            {
                Name = "Organization_BostonMedical",
                OrganizationName = "Boston Medical Center",
                Type = OrganizationState.OrganizationTypes.HealthcareProvider,
                Address = new OrganizationAddress(
                    Line: "725 Albany Street",
                    City: "Boston",
                    State: "MA",
                    PostalCode: "02118")
            })

            // Boston Patient 1: George Adams
            .WithPatient(age: 55, gender: "male", givenName: "George", familyName: "Adams")
            .AddEncounter("Diabetes Follow-up - George Adams")
            .AddObservation(new ObservationState
            {
                Name = "Observation_Glucose_George",
                Code = LabObservations.Glucose,
                Value = 145m,
                Unit = "mg/dL",
                UnitCode = "mg/dL"
            })
            .AddObservation(ObservationState.BloodPressure(systolic: 142, diastolic: 88))
            .AddObservation(new ObservationState
            {
                Name = "Observation_HbA1c_George",
                Code = LabObservations.HemoglobinA1c,
                Value = 7.2m,
                Unit = "%",
                UnitCode = "%"
            })

            // Boston Patient 2: Helen Baker
            .WithPatient(age: 48, gender: "female", givenName: "Helen", familyName: "Baker")
            .AddEncounter("Diabetes Follow-up - Helen Baker")
            .AddObservation(new ObservationState
            {
                Name = "Observation_Glucose_Helen",
                Code = LabObservations.Glucose,
                Value = 132m,
                Unit = "mg/dL",
                UnitCode = "mg/dL"
            })
            .AddObservation(ObservationState.BloodPressure(systolic: 135, diastolic: 85))
            .AddObservation(new ObservationState
            {
                Name = "Observation_HbA1c_Helen",
                Code = LabObservations.HemoglobinA1c,
                Value = 6.8m,
                Unit = "%",
                UnitCode = "%"
            })

            // Organization 2: Seattle General Hospital
            .AddOrganization(new OrganizationState
            {
                Name = "Organization_SeattleGeneral",
                OrganizationName = "Seattle General Hospital",
                Type = OrganizationState.OrganizationTypes.HealthcareProvider,
                Address = new OrganizationAddress(
                    Line: "1959 NE Pacific Street",
                    City: "Seattle",
                    State: "WA",
                    PostalCode: "98195")
            })

            // Seattle Patient 1: Ivan Clark
            .WithPatient(age: 62, gender: "male", givenName: "Ivan", familyName: "Clark")
            .AddEncounter("Diabetes Follow-up - Ivan Clark")
            .AddObservation(new ObservationState
            {
                Name = "Observation_Glucose_Ivan",
                Code = LabObservations.Glucose,
                Value = 158m,
                Unit = "mg/dL",
                UnitCode = "mg/dL"
            })
            .AddObservation(ObservationState.BloodPressure(systolic: 148, diastolic: 92))
            .AddObservation(new ObservationState
            {
                Name = "Observation_HbA1c_Ivan",
                Code = LabObservations.HemoglobinA1c,
                Value = 7.8m,
                Unit = "%",
                UnitCode = "%"
            })

            // Seattle Patient 2: Julia Davis
            .WithPatient(age: 41, gender: "female", givenName: "Julia", familyName: "Davis")
            .AddEncounter("Diabetes Follow-up - Julia Davis")
            .AddObservation(new ObservationState
            {
                Name = "Observation_Glucose_Julia",
                Code = LabObservations.Glucose,
                Value = 118m,
                Unit = "mg/dL",
                UnitCode = "mg/dL"
            })
            .AddObservation(ObservationState.BloodPressure(systolic: 122, diastolic: 78))
            .AddObservation(new ObservationState
            {
                Name = "Observation_HbA1c_Julia",
                Code = LabObservations.HemoglobinA1c,
                Value = 6.2m,
                Unit = "%",
                UnitCode = "%"
            })

            .Build();
    }

    #endregion

    #region Scenario 4: Encounter-Practitioner Chain Test

    /// <summary>
    /// Creates a scenario for testing Encounter-Practitioner chained searches.
    ///
    /// Tests the following search patterns:
    /// <list type="bullet">
    ///   <item><description>Encounter?participant:Practitioner.name=Thompson</description></item>
    ///   <item><description>Encounter?participant:Practitioner.identifier=NPI|1234567890</description></item>
    ///   <item><description>Encounter?type=EMER&amp;participant:Practitioner.name=Thompson</description></item>
    ///   <item><description>Practitioner?_has:Encounter:participant:type=EMER</description></item>
    ///   <item><description>Practitioner?_has:Encounter:participant:date=gt2024-01-01</description></item>
    /// </list>
    ///
    /// Generated Resources:
    /// <list type="bullet">
    ///   <item><description>1 Organization (Emergency Department)</description></item>
    ///   <item><description>3 Practitioners (Dr. Thompson - ED, Dr. Martinez - Cardiology, Dr. Anderson - Surgery)</description></item>
    ///   <item><description>3 Patients</description></item>
    ///   <item><description>9 Encounters (3 per practitioner with varied types: Emergency, Ambulatory, Inpatient)</description></item>
    /// </list>
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <returns>A scenario context for Encounter-Practitioner chain testing.</returns>
    public static ScenarioContext GetEncounterPractitionerChainTest(
        this IFhirSchemaProvider schemaProvider)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);

        return new ScenarioBuilder(schemaProvider)
            .WithName("Encounter-Practitioner Chain Test")
            .WithDescription("Tests Encounter->Practitioner forward chains and Practitioner<-Encounter reverse chains")

            // Setup organization (Emergency Department)
            .AddOrganization(new OrganizationState
            {
                Name = "Organization_EmergencyDepartment",
                OrganizationName = "Metro General Emergency Department",
                Type = OrganizationState.OrganizationTypes.Department,
                Address = new OrganizationAddress(
                    Line: "500 Hospital Drive",
                    City: "Denver",
                    State: "CO",
                    PostalCode: "80204")
            })

            // Practitioner 1: Dr. Thompson - Emergency Medicine
            .AddPractitioner(new PractitionerState
            {
                Specialty = Specialties.EmergencyMedicine,
                GivenName = "Marcus",
                FamilyName = "Thompson",
                Gender = "male",
                NpiNumber = "1234567890"
            })

            // Patient 1: Emergency visits with Dr. Thompson
            .WithPatient(age: 34, gender: "male", givenName: "Kevin", familyName: "Wilson")
            .AddState(EncounterState.Emergency("Chest pain - acute"))
            .AddState(EncounterState.Emergency("Shortness of breath"))
            .AddState(EncounterState.Ambulatory("Follow-up after ED visit"))

            // Practitioner 2: Dr. Martinez - Cardiology
            .AddPractitioner(new PractitionerState
            {
                Specialty = Specialties.Cardiology,
                GivenName = "Sofia",
                FamilyName = "Martinez",
                Gender = "female",
                NpiNumber = "2345678901"
            })

            // Patient 2: Cardiac encounters with Dr. Martinez
            .WithPatient(age: 58, gender: "female", givenName: "Patricia", familyName: "Garcia")
            .AddState(EncounterState.Ambulatory("Cardiac consultation"))
            .AddState(EncounterState.Inpatient("Cardiac catheterization", durationMinutes: 480))
            .AddState(EncounterState.Ambulatory("Post-procedure follow-up"))

            // Practitioner 3: Dr. Anderson - General Surgery
            .AddPractitioner(new PractitionerState
            {
                Specialty = Specialties.GeneralSurgery,
                GivenName = "William",
                FamilyName = "Anderson",
                Gender = "male",
                NpiNumber = "3456789012"
            })

            // Patient 3: Surgical encounters with Dr. Anderson
            .WithPatient(age: 45, gender: "male", givenName: "Richard", familyName: "Lopez")
            .AddState(EncounterState.Ambulatory("Pre-operative consultation"))
            .AddState(EncounterState.Inpatient("Cholecystectomy", durationMinutes: 1440))
            .AddState(EncounterState.Ambulatory("Post-operative follow-up"))

            .Build();
    }

    #endregion

    #region Scenario 5: Condition-Patient Chain Test

    /// <summary>
    /// Creates a scenario for testing Condition-Patient chained searches with chronic conditions.
    ///
    /// Tests the following search patterns:
    /// <list type="bullet">
    ///   <item><description>Condition?subject:Patient.name=Davis</description></item>
    ///   <item><description>Condition?subject:Patient.gender=female</description></item>
    ///   <item><description>Condition?code=73211009&amp;subject:Patient.birthdate=lt1970</description></item>
    ///   <item><description>Patient?_has:Condition:subject:code=73211009 (Diabetes)</description></item>
    ///   <item><description>Patient?_has:Condition:subject:clinical-status=active</description></item>
    /// </list>
    ///
    /// Generated Resources:
    /// <list type="bullet">
    ///   <item><description>1 Organization (Primary Care Clinic)</description></item>
    ///   <item><description>5 Patients</description></item>
    ///   <item><description>10 Conditions (2 per patient: varied chronic conditions)</description></item>
    /// </list>
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <returns>A scenario context for Condition-Patient chain testing.</returns>
    public static ScenarioContext GetConditionPatientChainTest(
        this IFhirSchemaProvider schemaProvider)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);

        // Define condition codes for the scenario
        var diabetes = new FhirCode(FhirCode.Systems.SnomedCt, "73211009", "Diabetes mellitus");
        var hypertension = new FhirCode(FhirCode.Systems.SnomedCt, "38341003", "Hypertensive disorder");
        var asthma = new FhirCode(FhirCode.Systems.SnomedCt, "195967001", "Asthma");
        var copd = new FhirCode(FhirCode.Systems.SnomedCt, "13645005", "Chronic obstructive lung disease");
        var chf = new FhirCode(FhirCode.Systems.SnomedCt, "42343007", "Congestive heart failure");
        var hyperlipidemia = new FhirCode(FhirCode.Systems.SnomedCt, "55822004", "Hyperlipidemia");

        return new ScenarioBuilder(schemaProvider)
            .WithName("Condition-Patient Chain Test")
            .WithDescription("Tests Condition->Patient forward chains and Patient<-Condition reverse chains with chronic conditions")

            // Setup organization
            .AddOrganization(new OrganizationState
            {
                Name = "Organization_PrimaryCareClinc",
                OrganizationName = "Wellness Primary Care Clinic",
                Type = OrganizationState.OrganizationTypes.HealthcareProvider,
                Address = new OrganizationAddress(
                    Line: "200 Wellness Way",
                    City: "Phoenix",
                    State: "AZ",
                    PostalCode: "85001")
            })

            // Patient 1: Nancy Davis (Diabetes + Hypertension)
            .WithPatient(age: 65, gender: "female", givenName: "Nancy", familyName: "Davis")
            .AddEncounter("Chronic disease management - Nancy Davis")
            .AddConditionOnset(diabetes, severity: 2, assignToAttribute: "condition_diabetes_nancy")
            .AddConditionOnset(hypertension, severity: 2, assignToAttribute: "condition_hypertension_nancy")

            // Patient 2: Thomas Evans (Asthma + Hyperlipidemia)
            .WithPatient(age: 42, gender: "male", givenName: "Thomas", familyName: "Evans")
            .AddEncounter("Chronic disease management - Thomas Evans")
            .AddConditionOnset(asthma, severity: 1, assignToAttribute: "condition_asthma_thomas")
            .AddConditionOnset(hyperlipidemia, severity: 1, assignToAttribute: "condition_hyperlipidemia_thomas")

            // Patient 3: Margaret Foster (COPD + Hypertension)
            .WithPatient(age: 72, gender: "female", givenName: "Margaret", familyName: "Foster")
            .AddEncounter("Chronic disease management - Margaret Foster")
            .AddConditionOnset(copd, severity: 3, assignToAttribute: "condition_copd_margaret")
            .AddConditionOnset(hypertension, severity: 2, assignToAttribute: "condition_hypertension_margaret")

            // Patient 4: Charles Grant (CHF + Diabetes)
            .WithPatient(age: 68, gender: "male", givenName: "Charles", familyName: "Grant")
            .AddEncounter("Chronic disease management - Charles Grant")
            .AddConditionOnset(chf, severity: 2, assignToAttribute: "condition_chf_charles")
            .AddConditionOnset(diabetes, severity: 2, assignToAttribute: "condition_diabetes_charles")

            // Patient 5: Susan Hall (Diabetes + Asthma)
            .WithPatient(age: 55, gender: "female", givenName: "Susan", familyName: "Hall")
            .AddEncounter("Chronic disease management - Susan Hall")
            .AddConditionOnset(diabetes, severity: 1, assignToAttribute: "condition_diabetes_susan")
            .AddConditionOnset(asthma, severity: 1, assignToAttribute: "condition_asthma_susan")

            .Build();
    }

    #endregion

    #region Scenario 6: MedicationRequest Chain Test

    /// <summary>
    /// Creates a scenario for testing MedicationRequest chained searches.
    ///
    /// Tests the following search patterns:
    /// <list type="bullet">
    ///   <item><description>MedicationRequest?subject:Patient.name=Brown</description></item>
    ///   <item><description>MedicationRequest?subject:Patient.identifier=MRN|12345</description></item>
    ///   <item><description>MedicationRequest?medication.code=860975&amp;subject:Patient.gender=male</description></item>
    ///   <item><description>Patient?_has:MedicationRequest:subject:medication=Metformin</description></item>
    ///   <item><description>Patient?_has:MedicationRequest:subject:status=active</description></item>
    ///   <item><description>MedicationRequest?requester:Practitioner.name=Prescriber</description></item>
    /// </list>
    ///
    /// Generated Resources:
    /// <list type="bullet">
    ///   <item><description>1 Organization (Family Medicine Clinic)</description></item>
    ///   <item><description>3 Practitioners</description></item>
    ///   <item><description>4 Patients</description></item>
    ///   <item><description>12 MedicationRequests (3 per patient: Metformin, Lisinopril, Atorvastatin)</description></item>
    /// </list>
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <returns>A scenario context for MedicationRequest chain testing.</returns>
    public static ScenarioContext GetMedicationRequestChainTest(
        this IFhirSchemaProvider schemaProvider)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);

        return new ScenarioBuilder(schemaProvider)
            .WithName("MedicationRequest Chain Test")
            .WithDescription("Tests MedicationRequest->Patient forward chains and Patient<-MedicationRequest reverse chains")

            // Setup organization
            .AddOrganization(new OrganizationState
            {
                Name = "Organization_FamilyMedicineClinic",
                OrganizationName = "Valley Family Medicine Clinic",
                Type = OrganizationState.OrganizationTypes.HealthcareProvider,
                Address = new OrganizationAddress(
                    Line: "300 Valley Road",
                    City: "San Jose",
                    State: "CA",
                    PostalCode: "95112")
            })

            // Practitioner 1: Dr. Prescriber (Primary prescriber for first 2 patients)
            .AddPractitioner(new PractitionerState
            {
                Specialty = Specialties.FamilyMedicine,
                GivenName = "John",
                FamilyName = "Prescriber",
                Gender = "male",
                NpiNumber = "4567890123"
            })

            // Patient 1: William Brown (Diabetes medications)
            .WithPatient(age: 52, gender: "male", givenName: "William", familyName: "Brown")
            .AddEncounter("Medication review - William Brown")
            .AddMedicationOrder(MedicationOrderState.Metformin500mg())
            .AddMedicationOrder(MedicationOrderState.Lisinopril10mg())
            .AddMedicationOrder(MedicationOrderState.Atorvastatin20mg())

            // Patient 2: Elizabeth Carter (Metabolic syndrome medications)
            .WithPatient(age: 48, gender: "female", givenName: "Elizabeth", familyName: "Carter")
            .AddEncounter("Medication review - Elizabeth Carter")
            .AddMedicationOrder(MedicationOrderState.Metformin1000mg())
            .AddMedicationOrder(MedicationOrderState.Lisinopril20mg())
            .AddMedicationOrder(new MedicationOrderState
            {
                Code = FhirCode.Medications.Atorvastatin40mg,
                IsChronic = true,
                Frequency = "daily",
                DoseQuantity = 1,
                DoseUnit = "tablet",
                ReasonCode = FhirCode.Conditions.Hyperlipidemia
            })

            // Practitioner 2: Dr. Specialist (Prescriber for patient 3)
            .AddPractitioner(new PractitionerState
            {
                Specialty = Specialties.Endocrinology,
                GivenName = "Maria",
                FamilyName = "Specialist",
                Gender = "female",
                NpiNumber = "5678901234"
            })

            // Patient 3: Daniel Dixon (Diabetes + Cardiac medications)
            .WithPatient(age: 61, gender: "male", givenName: "Daniel", familyName: "Dixon")
            .AddEncounter("Medication review - Daniel Dixon")
            .AddMedicationOrder(MedicationOrderState.Metformin1000mg())
            .AddMedicationOrder(new MedicationOrderState
            {
                Code = FhirCode.Medications.Amlodipine10mg,
                IsChronic = true,
                Frequency = "daily",
                DoseQuantity = 1,
                DoseUnit = "tablet",
                ReasonCode = FhirCode.Conditions.Hypertension
            })
            .AddMedicationOrder(new MedicationOrderState
            {
                Code = FhirCode.Medications.Aspirin81mg,
                IsChronic = true,
                Frequency = "daily",
                DoseQuantity = 1,
                DoseUnit = "tablet"
            })

            // Practitioner 3: Dr. Intern (Prescriber for patient 4)
            .AddPractitioner(new PractitionerState
            {
                Specialty = Specialties.InternalMedicine,
                GivenName = "Robert",
                FamilyName = "Intern",
                Gender = "male",
                NpiNumber = "6789012345"
            })

            // Patient 4: Amanda Ellis (Multiple chronic conditions)
            .WithPatient(age: 56, gender: "female", givenName: "Amanda", familyName: "Ellis")
            .AddEncounter("Medication review - Amanda Ellis")
            .AddMedicationOrder(MedicationOrderState.Metformin500mg())
            .AddMedicationOrder(MedicationOrderState.Amlodipine5mg())
            .AddMedicationOrder(MedicationOrderState.Atorvastatin20mg())

            .Build();
    }

    #endregion

    #region Scenario 7: DiagnosticReport Chain Test

    /// <summary>
    /// Creates a scenario for testing DiagnosticReport chained searches with lab panels.
    ///
    /// Tests the following search patterns:
    /// <list type="bullet">
    ///   <item><description>DiagnosticReport?subject:Patient.identifier=MRN|12345</description></item>
    ///   <item><description>DiagnosticReport?subject:Patient.name=Franklin</description></item>
    ///   <item><description>DiagnosticReport?code=24331-1&amp;subject:Patient.gender=female</description></item>
    ///   <item><description>Patient?_has:DiagnosticReport:subject:code=24331-1 (Lipid Panel)</description></item>
    ///   <item><description>DiagnosticReport?performer:Practitioner.name=Pathologist</description></item>
    ///   <item><description>DiagnosticReport?result:Observation.value-quantity=gt200</description></item>
    /// </list>
    ///
    /// Generated Resources:
    /// <list type="bullet">
    ///   <item><description>1 Organization (Clinical Laboratory)</description></item>
    ///   <item><description>2 Practitioners (Lab director, Pathologist)</description></item>
    ///   <item><description>3 Patients</description></item>
    ///   <item><description>9 DiagnosticReports (3 per patient: CBC, CMP, Lipid Panel)</description></item>
    ///   <item><description>27 Observations (contained in the DiagnosticReports)</description></item>
    /// </list>
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <returns>A scenario context for DiagnosticReport chain testing.</returns>
    public static ScenarioContext GetDiagnosticReportChainTest(
        this IFhirSchemaProvider schemaProvider)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);

        return new ScenarioBuilder(schemaProvider)
            .WithName("DiagnosticReport Chain Test")
            .WithDescription("Tests DiagnosticReport->Patient forward chains with lab panels and observations")

            // Setup organization (Laboratory)
            .AddOrganization(new OrganizationState
            {
                Name = "Organization_ClinicalLaboratory",
                OrganizationName = "Regional Clinical Laboratory",
                Type = OrganizationState.OrganizationTypes.HealthcareProvider,
                Address = new OrganizationAddress(
                    Line: "400 Lab Drive",
                    City: "Portland",
                    State: "OR",
                    PostalCode: "97201")
            })

            // Practitioner 1: Lab Director
            .AddPractitioner(new PractitionerState
            {
                Specialty = Specialties.Pathology,
                GivenName = "Linda",
                FamilyName = "Pathologist",
                Gender = "female",
                NpiNumber = "7890123456"
            })

            // Patient 1: Henry Franklin (Normal labs)
            .WithPatient(age: 45, gender: "male", givenName: "Henry", familyName: "Franklin")
            .AddEncounter("Laboratory workup - Henry Franklin")
            .AddDiagnosticReport(DiagnosticReportState.CompleteBloodCount())
            .AddDiagnosticReport(DiagnosticReportState.ComprehensiveMetabolicPanel())
            .AddDiagnosticReport(DiagnosticReportState.LipidPanel())

            // Practitioner 2: Senior Pathologist
            .AddPractitioner(new PractitionerState
            {
                Specialty = Specialties.Pathology,
                GivenName = "Edward",
                FamilyName = "Senior",
                Gender = "male",
                NpiNumber = "8901234567"
            })

            // Patient 2: Irene Graham (Slightly elevated values)
            .WithPatient(age: 58, gender: "female", givenName: "Irene", familyName: "Graham")
            .AddEncounter("Laboratory workup - Irene Graham")
            .AddDiagnosticReport(new DiagnosticReportState
            {
                Code = DiagnosticReports.CompleteBloodCount,
                Observations =
                [
                    (LabObservations.Hemoglobin, 12.5m, "g/dL"),
                    (LabObservations.Hematocrit, 38m, "%"),
                    (LabObservations.WBC, 8.2m, "10*3/uL"),
                    (LabObservations.RBC, 4.5m, "10*6/uL"),
                    (LabObservations.Platelets, 275m, "10*3/uL")
                ]
            })
            .AddDiagnosticReport(new DiagnosticReportState
            {
                Code = DiagnosticReports.ComprehensiveMetabolicPanel,
                Observations =
                [
                    (LabObservations.Glucose, 115m, "mg/dL"),
                    (LabObservations.Sodium, 138m, "mmol/L"),
                    (LabObservations.Potassium, 4.5m, "mmol/L"),
                    (LabObservations.Creatinine, 1.1m, "mg/dL"),
                    (LabObservations.BUN, 18m, "mg/dL")
                ]
            })
            .AddDiagnosticReport(new DiagnosticReportState
            {
                Code = DiagnosticReports.LipidPanel,
                Observations =
                [
                    (LabObservations.TotalCholesterol, 225m, "mg/dL"),
                    (LabObservations.HDLCholesterol, 48m, "mg/dL"),
                    (LabObservations.LDLCholesterol, 145m, "mg/dL"),
                    (LabObservations.Triglycerides, 165m, "mg/dL")
                ]
            })

            // Patient 3: Jack Harris (Abnormal values for testing)
            .WithPatient(age: 62, gender: "male", givenName: "Jack", familyName: "Harris")
            .AddEncounter("Laboratory workup - Jack Harris")
            .AddDiagnosticReport(new DiagnosticReportState
            {
                Code = DiagnosticReports.CompleteBloodCount,
                Observations =
                [
                    (LabObservations.Hemoglobin, 11.2m, "g/dL"),
                    (LabObservations.Hematocrit, 34m, "%"),
                    (LabObservations.WBC, 11.5m, "10*3/uL"),
                    (LabObservations.RBC, 4.0m, "10*6/uL"),
                    (LabObservations.Platelets, 180m, "10*3/uL")
                ]
            })
            .AddDiagnosticReport(new DiagnosticReportState
            {
                Code = DiagnosticReports.ComprehensiveMetabolicPanel,
                Observations =
                [
                    (LabObservations.Glucose, 185m, "mg/dL"),
                    (LabObservations.Sodium, 142m, "mmol/L"),
                    (LabObservations.Potassium, 5.2m, "mmol/L"),
                    (LabObservations.Creatinine, 1.8m, "mg/dL"),
                    (LabObservations.BUN, 32m, "mg/dL")
                ]
            })
            .AddDiagnosticReport(new DiagnosticReportState
            {
                Code = DiagnosticReports.LipidPanel,
                Observations =
                [
                    (LabObservations.TotalCholesterol, 265m, "mg/dL"),
                    (LabObservations.HDLCholesterol, 35m, "mg/dL"),
                    (LabObservations.LDLCholesterol, 185m, "mg/dL"),
                    (LabObservations.Triglycerides, 245m, "mg/dL")
                ]
            })

            .Build();
    }

    #endregion
}

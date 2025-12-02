// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.Scenarios.Codes;

/// <summary>
/// Common allergen and allergy codes (SNOMED CT).
/// Used for documenting allergic reactions and sensitivities.
/// </summary>
public static class Allergens
{
    // Food Allergies

    /// <summary>Allergy to peanuts (finding)</summary>
    public static FhirCode Peanuts { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "91935009",
        Display: "Allergy to peanut (finding)");

    /// <summary>Allergy to tree nuts (finding)</summary>
    public static FhirCode TreeNuts { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "91934008",
        Display: "Allergy to nut (finding)");

    /// <summary>Allergy to fish (finding)</summary>
    public static FhirCode Fish { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "417532002",
        Display: "Allergy to fish (finding)");

    /// <summary>Allergy to shellfish (finding)</summary>
    public static FhirCode Shellfish { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "300913006",
        Display: "Allergy to shellfish (finding)");

    /// <summary>Allergy to wheat (finding)</summary>
    public static FhirCode Wheat { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "420174000",
        Display: "Allergy to wheat (finding)");

    /// <summary>Allergy to eggs (finding)</summary>
    public static FhirCode Eggs { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "91930004",
        Display: "Allergy to eggs (finding)");

    /// <summary>Allergy to cow's milk (finding)</summary>
    public static FhirCode Milk { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "425525006",
        Display: "Allergy to dairy product (finding)");

    /// <summary>Allergy to soy (finding)</summary>
    public static FhirCode Soy { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "714035009",
        Display: "Allergy to soya (finding)");

    // Drug Allergies

    /// <summary>Allergy to penicillin (finding)</summary>
    public static FhirCode Penicillin { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "91936005",
        Display: "Allergy to penicillin (finding)");

    /// <summary>Allergy to sulfonamide (finding)</summary>
    public static FhirCode Sulfonamides { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "387406002",
        Display: "Allergy to sulfonamide (finding)");

    /// <summary>Allergy to aspirin (finding)</summary>
    public static FhirCode Aspirin { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "293586001",
        Display: "Allergy to aspirin (finding)");

    /// <summary>Allergy to nonsteroidal anti-inflammatory agents (finding)</summary>
    public static FhirCode NSAIDs { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "293637006",
        Display: "Allergy to non-steroidal anti-inflammatory agent (finding)");

    /// <summary>Allergy to latex (finding)</summary>
    public static FhirCode Latex { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "300916003",
        Display: "Latex allergy (finding)");

    // Environmental Allergies

    /// <summary>Allergy to tree pollen (finding)</summary>
    public static FhirCode TreePollen { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "419263009",
        Display: "Allergy to tree pollen (finding)");

    /// <summary>Allergy to grass pollen (finding)</summary>
    public static FhirCode GrassPollen { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "418689008",
        Display: "Allergy to grass pollen (finding)");

    /// <summary>Allergy to pollen (general - alias for GrassPollen)</summary>
    public static FhirCode Pollen => GrassPollen;

    /// <summary>Allergy to mold (finding)</summary>
    public static FhirCode Mold { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "419474003",
        Display: "Allergy to mould (finding)");

    /// <summary>Allergy to house dust mite (finding)</summary>
    public static FhirCode DustMite { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "232347008",
        Display: "House dust mite allergy (finding)");

    /// <summary>Allergy to house dust mites (alias for DustMite)</summary>
    public static FhirCode DustMites => DustMite;

    /// <summary>Allergy to animal dander (finding)</summary>
    public static FhirCode AnimalDander { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "232346004",
        Display: "Animal dander allergy (finding)");

    /// <summary>Allergy to cat dander (finding)</summary>
    public static FhirCode CatDander { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "232350006",
        Display: "Cat dander allergy (finding)");

    /// <summary>Allergy to dog dander (finding)</summary>
    public static FhirCode DogDander { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "232349003",
        Display: "Dog dander allergy (finding)");

    // Insect Allergies

    /// <summary>Allergy to bee venom (finding)</summary>
    public static FhirCode BeeVenom { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "424213003",
        Display: "Allergy to bee venom (finding)");

    /// <summary>Allergy to wasp venom (finding)</summary>
    public static FhirCode WaspVenom { get; } = new(
        System: FhirCode.Systems.SnomedCt,
        Code: "424560008",
        Display: "Allergy to Vespidae (wasp) venom (finding)");
}

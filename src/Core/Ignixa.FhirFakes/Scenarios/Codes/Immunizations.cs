// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.Scenarios.Codes;

/// <summary>
/// Common immunization codes (CVX - Vaccines Administered).
/// CVX codes are maintained by the CDC and identify vaccine products and substances.
/// </summary>
public static class Immunizations
{
    /// <summary>Hepatitis B vaccine, pediatric or pediatric/adolescent dosage</summary>
    public static FhirCode HepB { get; } = new(
        System: FhirCode.Systems.Cvx,
        Code: "08",
        Display: "Hep B, adolescent or pediatric");

    /// <summary>Hepatitis B vaccine, adult dosage</summary>
    public static FhirCode HepBAdult { get; } = new(
        System: FhirCode.Systems.Cvx,
        Code: "43",
        Display: "Hep B, adult");

    /// <summary>Rotavirus vaccine, monovalent (Rotarix - 2-dose series)</summary>
    public static FhirCode RotavirusMonovalent { get; } = new(
        System: FhirCode.Systems.Cvx,
        Code: "119",
        Display: "rotavirus, monovalent");

    /// <summary>Diphtheria, tetanus toxoids and acellular pertussis vaccine (DTaP)</summary>
    public static FhirCode DTaP { get; } = new(
        System: FhirCode.Systems.Cvx,
        Code: "20",
        Display: "DTaP");

    /// <summary>Haemophilus influenzae type b vaccine, PRP-OMP conjugate (PedvaxHib or COMVAX)</summary>
    public static FhirCode Hib { get; } = new(
        System: FhirCode.Systems.Cvx,
        Code: "49",
        Display: "Hib (PRP-OMP)");

    /// <summary>Pneumococcal conjugate vaccine, 13 valent (Prevnar 13)</summary>
    public static FhirCode PCV13 { get; } = new(
        System: FhirCode.Systems.Cvx,
        Code: "133",
        Display: "Pneumococcal conjugate PCV 13");

    /// <summary>Pneumococcal conjugate vaccine, 20 valent (Prevnar 20)</summary>
    public static FhirCode PCV20 { get; } = new(
        System: FhirCode.Systems.Cvx,
        Code: "216",
        Display: "Pneumococcal conjugate PCV20");

    /// <summary>Inactivated poliovirus vaccine (IPV)</summary>
    public static FhirCode IPV { get; } = new(
        System: FhirCode.Systems.Cvx,
        Code: "10",
        Display: "IPV");

    /// <summary>Influenza vaccine, seasonal, injectable, preservative free</summary>
    public static FhirCode Influenza { get; } = new(
        System: FhirCode.Systems.Cvx,
        Code: "140",
        Display: "Influenza, seasonal, injectable, preservative free");

    /// <summary>Measles, mumps and rubella vaccine (MMR)</summary>
    public static FhirCode MMR { get; } = new(
        System: FhirCode.Systems.Cvx,
        Code: "03",
        Display: "MMR");

    /// <summary>Varicella (chickenpox) vaccine</summary>
    public static FhirCode Varicella { get; } = new(
        System: FhirCode.Systems.Cvx,
        Code: "21",
        Display: "varicella");

    /// <summary>Hepatitis A vaccine, pediatric/adolescent dosage, 2 dose schedule</summary>
    public static FhirCode HepA { get; } = new(
        System: FhirCode.Systems.Cvx,
        Code: "83",
        Display: "Hep A, ped/adol, 2 dose");

    /// <summary>Hepatitis A vaccine, adult dosage</summary>
    public static FhirCode HepAAdult { get; } = new(
        System: FhirCode.Systems.Cvx,
        Code: "52",
        Display: "Hep A, adult");

    /// <summary>Meningococcal MCV4P vaccine (Menactra)</summary>
    public static FhirCode MeningococcalMCV4P { get; } = new(
        System: FhirCode.Systems.Cvx,
        Code: "114",
        Display: "meningococcal MCV4P");

    /// <summary>Tetanus, diphtheria toxoids and acellular pertussis vaccine (Tdap)</summary>
    public static FhirCode Tdap { get; } = new(
        System: FhirCode.Systems.Cvx,
        Code: "115",
        Display: "Tdap");

    /// <summary>Human papillomavirus vaccine, quadrivalent (Gardasil)</summary>
    public static FhirCode HPV { get; } = new(
        System: FhirCode.Systems.Cvx,
        Code: "62",
        Display: "HPV, quadrivalent");

    /// <summary>Tetanus and diphtheria toxoids, adult dosage, preservative free, adsorbed</summary>
    public static FhirCode TdAdult { get; } = new(
        System: FhirCode.Systems.Cvx,
        Code: "113",
        Display: "Td (adult), 5 Lf tetanus toxoid, preservative free, adsorbed");

    /// <summary>Zoster (shingles) vaccine, live attenuated (Zostavax)</summary>
    public static FhirCode Zoster { get; } = new(
        System: FhirCode.Systems.Cvx,
        Code: "121",
        Display: "zoster vaccine, live");

    /// <summary>Pneumococcal polysaccharide vaccine, 23 valent (Pneumovax 23)</summary>
    public static FhirCode PPSV23 { get; } = new(
        System: FhirCode.Systems.Cvx,
        Code: "33",
        Display: "pneumococcal polysaccharide vaccine, 23 valent");

    /// <summary>COVID-19 vaccine, mRNA, spike protein, LNP, preservative free, 30 mcg/0.3mL dose</summary>
    public static FhirCode Covid19Pfizer { get; } = new(
        System: FhirCode.Systems.Cvx,
        Code: "208",
        Display: "COVID-19, mRNA, LNP-S, PF, 30 mcg/0.3 mL dose");

    /// <summary>COVID-19 vaccine, mRNA, spike protein, LNP, preservative free, 100 mcg/0.5mL dose</summary>
    public static FhirCode Covid19Moderna { get; } = new(
        System: FhirCode.Systems.Cvx,
        Code: "207",
        Display: "COVID-19, mRNA, LNP-S, PF, 100 mcg/0.5 mL dose");

    /// <summary>COVID-19 vaccine, vector-based (Johnson &amp; Johnson/Janssen)</summary>
    public static FhirCode Covid19Janssen { get; } = new(
        System: FhirCode.Systems.Cvx,
        Code: "212",
        Display: "COVID-19 vaccine, vector-nr, rS-ChAdOx1, PF, 0.5 mL");
}

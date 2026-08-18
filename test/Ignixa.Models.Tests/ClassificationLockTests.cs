// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using System.Reflection;
using Ignixa.Abstractions;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Shouldly;
using Xunit;

namespace Ignixa.Models.Tests;

/// <summary>
/// Structural lock on the type-classifier's outcomes. These assert over the BUILT assemblies via
/// reflection (no FHIR-package load), so they pin the classification contract cheaply: an IDENTICAL
/// datatype lives once in the base with no per-version subclass (resources always get one -- see
/// <see cref="GivenResourceFacade_WhenClassified_ThenEverySupportedVersionHasARealSubclassNotAnAlias"/>);
/// a SUBCLASSED type's base is the shared base type; an INCOMPATIBLE element is typed differently per
/// version; and Resource/DomainResource-level elements are declared once on their base class.
/// </summary>
public sealed class ClassificationLockTests
{
    private static readonly Assembly SerializationAssembly = typeof(Coding).Assembly;
    private static readonly Assembly R4Assembly = typeof(Ignixa.Models.R4.Patient).Assembly;
    private static readonly Assembly R5Assembly = typeof(Ignixa.Models.R5.Patient).Assembly;

    [Fact]
    public void GivenIdenticalType_WhenClassified_ThenItLivesOnlyInTheSharedBaseWithNoSubclass()
    {
        // Coding is byte-identical across R4/R5 -> base-only, no per-version subclass.
        typeof(Coding).Namespace.ShouldBe("Ignixa.Models");

        R4Assembly.GetType("Ignixa.Models.R4.Coding").ShouldBeNull();
        R5Assembly.GetType("Ignixa.Models.R5.Coding").ShouldBeNull();
    }

    [Fact]
    public void GivenIdenticalDatatype_WhenClassified_ThenMoneyIsBaseOnly()
    {
        // Money.Currency IS a coded binding (Currencies?), but unlike Quantity.Comparator (see the
        // value-set-divergence test below) its value set's code set is IDENTICAL across R4/R5, so it
        // correctly stays base-only -- this pins the "same codes -> still Identical" counterpart to
        // that test's "different codes -> Incompatible", proving the hash doesn't over-trigger.
        typeof(Money).Namespace.ShouldBe("Ignixa.Models");

        R4Assembly.GetType("Ignixa.Models.R4.Money").ShouldBeNull();
        R5Assembly.GetType("Ignixa.Models.R5.Money").ShouldBeNull();
    }

    [Fact]
    public void GivenSubclassedResource_WhenClassified_ThenVersionPatientInheritsTheSharedBase()
    {
        typeof(Ignixa.Models.R4.Patient).BaseType.ShouldBe(typeof(Patient));
        typeof(Ignixa.Models.R5.Patient).BaseType.ShouldBe(typeof(Patient));

        // The shared base lives in the Serialization assembly under Ignixa.Models.
        typeof(Patient).Assembly.ShouldBe(SerializationAssembly);
        typeof(Patient).Namespace.ShouldBe("Ignixa.Models");
    }

    [Fact]
    public void GivenSubclassedDatatype_WhenClassified_ThenAttachmentInheritsTheSharedBase()
    {
        // Attachment is INCOMPATIBLE (size retyped), so each version subclasses the shared base.
        typeof(Ignixa.Models.R4.Attachment).BaseType.ShouldBe(typeof(Attachment));
        typeof(Ignixa.Models.R5.Attachment).BaseType.ShouldBe(typeof(Attachment));
    }

    [Fact]
    public void GivenIncompatibleElement_WhenTyped_ThenAccessorReturnTypeDiffersAcrossVersions()
    {
        PropertyInfo r4Size = typeof(Ignixa.Models.R4.Attachment).GetProperty("Size")!;
        PropertyInfo r5Size = typeof(Ignixa.Models.R5.Attachment).GetProperty("Size")!;

        r4Size.ShouldNotBeNull();
        r5Size.ShouldNotBeNull();

        r4Size.PropertyType.ShouldBe(typeof(int?));
        r5Size.PropertyType.ShouldBe(typeof(long?));
        r4Size.PropertyType.ShouldNotBe(r5Size.PropertyType);
    }

    [Fact]
    public void GivenIncompatibleElement_WhenTyped_ThenItIsAbsentFromTheSharedBase()
    {
        // The INCOMPATIBLE element is omitted from the base so the base stays Liskov-substitutable.
        typeof(Attachment).GetProperty("Size").ShouldBeNull();
    }

    [Fact]
    public void GivenValueSetThatGainedCodesUnderTheSameUrl_WhenClassified_ThenBindingIsIncompatibleNotIdentical()
    {
        // Quantity.comparator is bound to http://hl7.org/fhir/ValueSet/quantity-comparator in both R4 and
        // R5 -- same URL, same binding strength -- but R5 added the "ad" (sufficient-to-achieve) code to
        // the SAME value set. Comparing signatures by URL alone would classify this Identical and build
        // the shared enum from R4's codes only, silently dropping "ad" to null on read for R5 callers.
        // The value-set-codes-hash in ElementSignature must catch this and demote it to Incompatible.
        PropertyInfo r4Comparator = typeof(Ignixa.Models.R4.Quantity).GetProperty("Comparator")!;
        PropertyInfo r5Comparator = typeof(Ignixa.Models.R5.Quantity).GetProperty("Comparator")!;

        r4Comparator.ShouldNotBeNull();
        r5Comparator.ShouldNotBeNull();

        r4Comparator.PropertyType.ShouldBe(typeof(Ignixa.Models.R4.QuantityComparator?));
        r5Comparator.PropertyType.ShouldBe(typeof(Ignixa.Models.R5.QuantityComparator?));
        r4Comparator.PropertyType.ShouldNotBe(r5Comparator.PropertyType);

        // Per-version enum, not a shared base enum: R5's Comparator has the extra "Ad" member R4 lacks.
        Enum.GetNames(typeof(Ignixa.Models.R5.QuantityComparator)).ShouldContain("Ad");
        Enum.GetNames(typeof(Ignixa.Models.R4.QuantityComparator)).ShouldNotContain("Ad");

        // Absent from the shared base so it stays Liskov-substitutable (mirrors Attachment.Size above).
        typeof(Quantity).GetProperty("Comparator").ShouldBeNull();
    }

    [Fact]
    public void GivenValueSetThatGainedManyCodesUnderTheSameUrl_WhenClassified_ThenRelatedArtifactTypeIsIncompatible()
    {
        // RelatedArtifact.type is the largest real-world instance of the same-URL-divergent-codes bug
        // this session's fix caught: R5 added ~25 new relationship codes (part-of, amends, cites, ...)
        // to http://hl7.org/fhir/related-artifact-type without changing the URL. A URL-only signature
        // would have silently capped every R5-only code at null forever.
        PropertyInfo r4Type = typeof(Ignixa.Models.R4.RelatedArtifact).GetProperty("Type")!;
        PropertyInfo r5Type = typeof(Ignixa.Models.R5.RelatedArtifact).GetProperty("Type")!;

        r4Type.PropertyType.ShouldNotBe(r5Type.PropertyType);

        string[] r4Codes = Enum.GetNames(typeof(Ignixa.Models.R4.RelatedArtifactType));
        string[] r5Codes = Enum.GetNames(typeof(Ignixa.Models.R5.RelatedArtifactType));
        r5Codes.Length.ShouldBeGreaterThan(r4Codes.Length);
        r5Codes.ShouldContain("PartOf");
        r4Codes.ShouldNotContain("PartOf");

        typeof(RelatedArtifact).GetProperty("Type").ShouldBeNull();
    }

    [Fact]
    public void GivenValueSetThatGainedOneCodeUnderTheSameUrl_WhenClassified_ThenDiscriminatorTypeIsIncompatible()
    {
        // ElementDefinitionSlicingDiscriminator.type: R5 added a single new code ("position") to
        // http://hl7.org/fhir/discriminator-type -- the minimal-diff case, as distinct from the
        // large RelatedArtifactType and single-narrow-domain QuantityComparator cases above.
        PropertyInfo r4Type = typeof(Ignixa.Models.R4.ElementDefinitionSlicingDiscriminator).GetProperty("Type")!;
        PropertyInfo r5Type = typeof(Ignixa.Models.R5.ElementDefinitionSlicingDiscriminator).GetProperty("Type")!;

        r4Type.PropertyType.ShouldNotBe(r5Type.PropertyType);

        Enum.GetNames(typeof(Ignixa.Models.R5.DiscriminatorType)).ShouldContain("Position");
        Enum.GetNames(typeof(Ignixa.Models.R4.DiscriminatorType)).ShouldNotContain("Position");

        typeof(ElementDefinitionSlicingDiscriminator).GetProperty("Type").ShouldBeNull();
    }

    [Fact]
    public void GivenAdditiveElement_WhenClassified_ThenItLivesOnlyOnTheVersionThatIntroducedIt()
    {
        // Observation.bodyStructure is ADDITIVE: introduced in R5, absent from R4. It must surface
        // on the R5 subclass only -- never promoted onto the shared base (that would lie to R4
        // callers) and never appear on the R4 subclass. This locks the Additive bucket, which the
        // Identical (base-only) and Incompatible (retyped) cases above do not cover.
        PropertyInfo? r5BodyStructure = typeof(Ignixa.Models.R5.Observation)
            .GetProperty("BodyStructure", BindingFlags.Public | BindingFlags.Instance);

        r5BodyStructure.ShouldNotBeNull();
        r5BodyStructure!.DeclaringType.ShouldBe(typeof(Ignixa.Models.R5.Observation));

        // Absent from the shared base (declared OR inherited).
        typeof(Observation).GetProperty("BodyStructure").ShouldBeNull();

        // Absent from the R4 subclass too (R4 Observation has no bodyStructure element).
        typeof(Ignixa.Models.R4.Observation).GetProperty("BodyStructure").ShouldBeNull();
    }

    [Fact]
    public void GivenDomainResourceElements_WhenClassified_ThenTheyAreDeclaredOnceOnDomainResourceJsonNode()
    {
        foreach (string name in new[] { "Text", "Contained", "Extension", "ModifierExtension" })
        {
            PropertyInfo? declared = typeof(DomainResourceJsonNode)
                .GetProperty(name, BindingFlags.Public | BindingFlags.Instance);

            declared.ShouldNotBeNull($"DomainResourceJsonNode must declare {name}");
            declared!.DeclaringType.ShouldBe(typeof(DomainResourceJsonNode));
        }
    }

    [Fact]
    public void GivenResourceElements_WhenClassified_ThenTheyAreDeclaredOnceOnResourceJsonNode()
    {
        foreach (string name in new[] { "Id", "Meta", "ImplicitRules", "Language" })
        {
            PropertyInfo? declared = typeof(ResourceJsonNode)
                .GetProperty(name, BindingFlags.Public | BindingFlags.Instance);

            declared.ShouldNotBeNull($"ResourceJsonNode must declare {name}");
            declared!.DeclaringType.ShouldBe(typeof(ResourceJsonNode));
        }
    }

    [Fact]
    public void GivenResourceFacade_WhenGenerated_ThenItInheritsBaseElementsInsteadOfRedeclaringThem()
    {
        // Regression guard for the generator's skip gates: a resource facade that re-emits an inherited
        // element shadows the base member, so which accessor a caller gets depends on their static type.
        // CS0108 is an error here (TreatWarningsAsErrors), so the compiler already covers that direction;
        // this locks the direction it cannot see. Scoped to ResourceJsonNode subclasses on purpose --
        // Attachment.Language and CompositionSection.Text are genuine datatype/backbone elements of the
        // same name and must NOT be caught by this.
        string[] inherited = ["Id", "Meta", "ImplicitRules", "Language", "Text", "Contained", "Extension", "ModifierExtension"];

        int checkedProperties = 0;
        foreach (Type facade in AllResourceFacades())
        {
            foreach (string name in inherited)
            {
                PropertyInfo? property = facade.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (property is null)
                {
                    continue;
                }

                property.DeclaringType.ShouldBeOneOf(
                    typeof(ResourceJsonNode),
                    typeof(DomainResourceJsonNode));
                checkedProperties++;
            }
        }

        // Roughly 11 facades x 3 assemblies x 4-8 properties. A loose floor would let the sweep silently
        // drop an entire assembly and still pass.
        checkedProperties.ShouldBeGreaterThan(200);
    }

    [Fact]
    public void GivenResourceFacade_WhenClassified_ThenEverySupportedVersionHasARealSubclassNotAnAlias()
    {
        // Resource version identity must not be derived from whether an element happens to diverge.
        // CompatibleFhirVersionsAttribute is read with inherit:false (and declared Inherited = false) and
        // a `global using` alias cannot carry an attribute, so a resource that loses its subclass silently
        // loses the As<T> guard and the single-version stamping path in As<T>. AsVersion() is unaffected --
        // it stamps from the registry key. Datatypes are exempt: As<T> is constrained to ResourceJsonNode.
        //
        // Every resource is swept, NOT just attribute-tagged ones. Version-agnostic contract types
        // (Bundle, Parameters, OperationOutcome) deliberately carry no base attribute, and filtering on
        // the attribute would have excluded 4 of the 7 subclasses that actually regressed.
        var assembliesByVersion = new Dictionary<FhirVersion, Assembly>
        {
            [FhirVersion.R4] = R4Assembly,
            [FhirVersion.R5] = R5Assembly,
        };

        var baseResources = SerializationAssembly.GetTypes()
            .Where(t => t.IsPublic
                && t.Namespace == "Ignixa.Models"
                && typeof(ResourceJsonNode).IsAssignableFrom(t))
            .ToList();

        baseResources.Count.ShouldBeGreaterThanOrEqualTo(11);

        int checkedSubclasses = 0;
        foreach (Type baseResource in baseResources)
        {
            // An untagged base is a version-agnostic contract type, which by definition exists in every
            // supported version -- so require a subclass in all of them rather than skipping it.
            FhirVersion[] versions = baseResource.GetCustomAttribute<CompatibleFhirVersionsAttribute>(inherit: false)
                is { } attribute
                ? attribute.Versions.ToArray()
                : assembliesByVersion.Keys.ToArray();

            foreach (FhirVersion version in versions)
            {
                if (!assembliesByVersion.TryGetValue(version, out Assembly? assembly))
                {
                    continue;
                }

                Type? subclass = assembly.GetType($"Ignixa.Models.{version}.{baseResource.Name}");

                subclass.ShouldNotBeNull(
                    $"{baseResource.Name} supports {version} but has no Ignixa.Models.{version}.{baseResource.Name} "
                    + "subclass to carry its version tag");
                subclass!.BaseType.ShouldBe(baseResource);
                subclass.GetCustomAttribute<CompatibleFhirVersionsAttribute>(inherit: false)!
                    .Versions.ToArray().ShouldBe([version]);
                checkedSubclasses++;
            }
        }

        // 11 resources x 2 versions. A loose floor would tolerate losing half the matrix.
        checkedSubclasses.ShouldBeGreaterThanOrEqualTo(22);
    }

    [Fact]
    public void GivenNonDomainResourceFacade_WhenClassified_ThenItDoesNotExposeDomainResourceElements()
    {
        // The other half of the placement invariant. Before this PR, Bundle and Parameters simply had no
        // text/contained/extension/modifierExtension emitted. Now the invariant rests entirely on the
        // generator choosing ResourceJsonNode as their base, so assert the negative directly -- a facade
        // misclassified onto DomainResourceJsonNode would otherwise silently gain all four and let callers
        // write JSON this project's own validator rejects (see Validation.Tests ResourceOnlyUniversalPropertyTests).
        string[] domainResourceOnly = ["Text", "Contained", "Extension", "ModifierExtension"];

        var nonDomainResources = AllResourceFacades()
            .Where(t => !typeof(DomainResourceJsonNode).IsAssignableFrom(t))
            .ToList();

        nonDomainResources.ShouldNotBeEmpty();

        foreach (Type facade in nonDomainResources)
        {
            foreach (string name in domainResourceOnly)
            {
                facade.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)
                    .ShouldBeNull($"{facade.FullName} is not a DomainResource but exposes {name}");
            }
        }
    }

    private static IEnumerable<Type> AllResourceFacades() =>
        new[] { SerializationAssembly, R4Assembly, R5Assembly }
            .SelectMany(a => a.GetTypes())
            .Where(t => t.IsPublic && typeof(ResourceJsonNode).IsAssignableFrom(t));
}

// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Serialization.Models;

/// <summary>
/// Represents a FHIR Composition resource.
/// A Composition is a set of healthcare-related information that is assembled together into
/// a single logical document that provides a single coherent statement of meaning.
/// </summary>
[SuppressMessage("Design", "CA2227", Justification = "POCO style model")]
[SuppressMessage("Design", "CA1819", Justification = "POCO style model")]
public class CompositionJsonNode : ResourceJsonNode
{
    public CompositionJsonNode()
    {
        ResourceType = "Composition";
    }

    /// <summary>
    /// Internal constructor for JsonConverter (accepts pre-parsed JsonObject).
    /// </summary>
    internal CompositionJsonNode(JsonObject jsonObject)
        : base(jsonObject)
    {
    }

    /// <summary>
    /// Public constructor for JsonConverter (accepts pre-parsed JsonObject with optional FHIR version).
    /// </summary>
    public CompositionJsonNode(JsonObject jsonObject, FhirVersion? fhirVersion = null)
        : base(jsonObject, fhirVersion)
    {
    }

    /// <summary>
    /// The workflow/clinical status of the Composition.
    /// </summary>
    [JsonIgnore]
    public CompositionStatus? Status
    {
        get
        {
            var statusStr = GetProperty<string>("status");
            return statusStr is not null ? ParseCompositionStatus(statusStr) : null;
        }
        set
        {
            if (value is null)
            {
                MutableNode.Remove("status");
            }
            else
            {
                SetProperty("status", GetCompositionStatusLiteral(value.Value));
            }
        }
    }

    /// <summary>
    /// The type of document - specifies the particular kind of composition.
    /// </summary>
    [JsonIgnore]
    public CodeableConceptJsonNode Type
    {
        get => GetComplexProperty<CodeableConceptJsonNode>("type")!;
        set
        {
            if (value is null)
            {
                MutableNode.Remove("type");
            }
            else
            {
                MutableNode["type"] = value.MutableNode;
            }
        }
    }

    /// <summary>
    /// Who or what the composition is about.
    /// </summary>
    [JsonIgnore]
    public ReferenceJsonNode Subject
    {
        get => GetComplexProperty<ReferenceJsonNode>("subject")!;
        set
        {
            if (value is null)
            {
                MutableNode.Remove("subject");
            }
            else
            {
                MutableNode["subject"] = value.MutableNode;
            }
        }
    }

    /// <summary>
    /// The composition editing time.
    /// </summary>
    [JsonIgnore]
    public DateTimeOffset? Date
    {
        get
        {
            var dateStr = GetProperty<string>("date");
            return dateStr is not null && DateTimeOffset.TryParse(dateStr, out var result) ? result : null;
        }
        set
        {
            if (value is null)
            {
                MutableNode.Remove("date");
            }
            else
            {
                MutableNode["date"] = value.Value.ToString("o");
            }
        }
    }

    /// <summary>
    /// Official human-readable label for the composition.
    /// </summary>
    [JsonIgnore]
    public string? Title
    {
        get => GetProperty<string>("title");
        set => SetProperty("title", value);
    }

    /// <summary>
    /// Identifies who is responsible for the information in the composition.
    /// </summary>
    [JsonIgnore]
    public MutableJsonList<ReferenceJsonNode> Author => GetListProperty<ReferenceJsonNode>("author");

    /// <summary>
    /// Attests to accuracy of composition.
    /// </summary>
    [JsonIgnore]
    public MutableJsonList<AttesterComponent> Attester => GetListProperty<AttesterComponent>("attester");

    /// <summary>
    /// Organization which maintains the composition.
    /// </summary>
    [JsonIgnore]
    public ReferenceJsonNode? Custodian
    {
        get => GetComplexProperty<ReferenceJsonNode>("custodian");
        set
        {
            if (value is null)
            {
                MutableNode.Remove("custodian");
            }
            else
            {
                MutableNode["custodian"] = value.MutableNode;
            }
        }
    }

    /// <summary>
    /// Relationships to other compositions/documents.
    /// </summary>
    [JsonIgnore]
    public MutableJsonList<RelatesToComponent> RelatesTo => GetListProperty<RelatesToComponent>("relatesTo");

    /// <summary>
    /// The clinical service(s) being documented.
    /// </summary>
    [JsonIgnore]
    public MutableJsonList<EventComponent> Event => GetListProperty<EventComponent>("event");

    /// <summary>
    /// Composition is broken into sections.
    /// </summary>
    [JsonIgnore]
    public MutableJsonList<SectionComponent> Section => GetListProperty<SectionComponent>("section");

    private static CompositionStatus? ParseCompositionStatus(string value) =>
        value switch
        {
            "preliminary" => CompositionStatus.Preliminary,
            "final" => CompositionStatus.Final,
            "amended" => CompositionStatus.Amended,
            "entered-in-error" => CompositionStatus.EnteredInError,
            _ => null,
        };

    private static string GetCompositionStatusLiteral(CompositionStatus status) =>
        status switch
        {
            CompositionStatus.Preliminary => "preliminary",
            CompositionStatus.Final => "final",
            CompositionStatus.Amended => "amended",
            CompositionStatus.EnteredInError => "entered-in-error",
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };

    /// <summary>
    /// The workflow/clinical status of the Composition (FHIR CompositionStatus value set).
    /// </summary>
    public enum CompositionStatus
    {
        [EnumLiteral("preliminary")]
        Preliminary,

        [EnumLiteral("final")]
        Final,

        [EnumLiteral("amended")]
        Amended,

        [EnumLiteral("entered-in-error")]
        EnteredInError,
    }

    /// <summary>
    /// Represents a participant who has attested to the accuracy of the composition.
    /// </summary>
    [SuppressMessage("Design", "CA1034", Justification = "Nested type matches FHIR structure")]
    public class AttesterComponent : BaseJsonNode
    {
        public AttesterComponent()
        {
        }

        public AttesterComponent(JsonObject jsonObject, FhirVersion? fhirVersion = null)
            : base(jsonObject, fhirVersion)
        {
        }

        [JsonIgnore]
        public string? Mode
        {
            get => GetProperty<string>("mode");
            set => SetProperty("mode", value);
        }

        [JsonIgnore]
        public DateTimeOffset? Time
        {
            get
            {
                var timeStr = GetProperty<string>("time");
                return timeStr is not null && DateTimeOffset.TryParse(timeStr, out var result) ? result : null;
            }
            set
            {
                if (value is null)
                {
                    MutableNode.Remove("time");
                }
                else
                {
                    MutableNode["time"] = value.Value.ToString("o");
                }
            }
        }

        [JsonIgnore]
        public ReferenceJsonNode? Party
        {
            get => GetComplexProperty<ReferenceJsonNode>("party");
            set
            {
                if (value is null)
                {
                    MutableNode.Remove("party");
                }
                else
                {
                    MutableNode["party"] = value.MutableNode;
                }
            }
        }
    }

    /// <summary>
    /// Represents a relationship to another composition or document.
    /// </summary>
    [SuppressMessage("Design", "CA1034", Justification = "Nested type matches FHIR structure")]
    public class RelatesToComponent : BaseJsonNode
    {
        public RelatesToComponent()
        {
        }

        public RelatesToComponent(JsonObject jsonObject, FhirVersion? fhirVersion = null)
            : base(jsonObject, fhirVersion)
        {
        }

        [JsonIgnore]
        public string? Code
        {
            get => GetProperty<string>("code");
            set => SetProperty("code", value);
        }

        [JsonIgnore]
        public ReferenceJsonNode? TargetReference
        {
            get => GetComplexProperty<ReferenceJsonNode>("targetReference");
            set
            {
                if (value is null)
                {
                    MutableNode.Remove("targetReference");
                }
                else
                {
                    MutableNode["targetReference"] = value.MutableNode;
                }
            }
        }
    }

    /// <summary>
    /// Represents the clinical service(s) being documented.
    /// </summary>
    [SuppressMessage("Design", "CA1034", Justification = "Nested type matches FHIR structure")]
    public class EventComponent : BaseJsonNode
    {
        public EventComponent()
        {
        }

        public EventComponent(JsonObject jsonObject, FhirVersion? fhirVersion = null)
            : base(jsonObject, fhirVersion)
        {
        }

        [JsonIgnore]
        public MutableJsonList<CodeableConceptJsonNode> Code => GetListProperty<CodeableConceptJsonNode>("code");

        [JsonIgnore]
        public MutableJsonList<ReferenceJsonNode> Detail => GetListProperty<ReferenceJsonNode>("detail");
    }

    /// <summary>
    /// Represents a section of the composition.
    /// </summary>
    [SuppressMessage("Design", "CA1034", Justification = "Nested type matches FHIR structure")]
    public class SectionComponent : BaseJsonNode
    {
        public SectionComponent()
        {
        }

        public SectionComponent(JsonObject jsonObject, FhirVersion? fhirVersion = null)
            : base(jsonObject, fhirVersion)
        {
        }

        /// <summary>
        /// Label for section (e.g. for ToC).
        /// </summary>
        [JsonIgnore]
        public string? Title
        {
            get => GetProperty<string>("title");
            set => SetProperty("title", value);
        }

        /// <summary>
        /// Classification of section (recommended).
        /// </summary>
        [JsonIgnore]
        public CodeableConceptJsonNode? Code
        {
            get => GetComplexProperty<CodeableConceptJsonNode>("code");
            set
            {
                if (value is null)
                {
                    MutableNode.Remove("code");
                }
                else
                {
                    MutableNode["code"] = value.MutableNode;
                }
            }
        }

        /// <summary>
        /// Text summary of the section for human interpretation.
        /// </summary>
        [JsonIgnore]
        public NarrativeJsonNode? Text
        {
            get => GetComplexProperty<NarrativeJsonNode>("text");
            set
            {
                if (value is null)
                {
                    MutableNode.Remove("text");
                }
                else
                {
                    MutableNode["text"] = value.MutableNode;
                }
            }
        }

        /// <summary>
        /// Order of section entries.
        /// </summary>
        [JsonIgnore]
        public CodeableConceptJsonNode? OrderedBy
        {
            get => GetComplexProperty<CodeableConceptJsonNode>("orderedBy");
            set
            {
                if (value is null)
                {
                    MutableNode.Remove("orderedBy");
                }
                else
                {
                    MutableNode["orderedBy"] = value.MutableNode;
                }
            }
        }

        /// <summary>
        /// A reference to data that supports this section.
        /// </summary>
        [JsonIgnore]
        public MutableJsonList<ReferenceJsonNode> Entry => GetListProperty<ReferenceJsonNode>("entry");

        /// <summary>
        /// Why the section is empty.
        /// </summary>
        [JsonIgnore]
        public CodeableConceptJsonNode? EmptyReason
        {
            get => GetComplexProperty<CodeableConceptJsonNode>("emptyReason");
            set
            {
                if (value is null)
                {
                    MutableNode.Remove("emptyReason");
                }
                else
                {
                    MutableNode["emptyReason"] = value.MutableNode;
                }
            }
        }

        /// <summary>
        /// Nested sections (parts of the section).
        /// </summary>
        [JsonIgnore]
        public MutableJsonList<SectionComponent> SubSection => GetListProperty<SectionComponent>("section");
    }
}

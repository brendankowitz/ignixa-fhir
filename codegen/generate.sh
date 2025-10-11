#!/usr/bin/env bash
# Generate FHIR IStructureDefinitionSummaryProvider implementations
# This script uses fhir-codegen with our custom CSharpStructureProviderLanguage

set -e

# Paths
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
OUTPUT_DIR="$SCRIPT_DIR/../src/Sparky.Specification/Generated"
CODEGEN_EXE="$SCRIPT_DIR/fhir-codegen/src/fhir-codegen/bin/Release/net8.0/fhir-codegen"

# Parse arguments
FHIR_VERSION="${1:-All}"

# Create output directory
mkdir -p "$OUTPUT_DIR"

echo -e "\033[0;36mBuilding fhir-codegen tool...\033[0m"
pushd "$SCRIPT_DIR/fhir-codegen" > /dev/null
dotnet build -c Release src/fhir-codegen/fhir-codegen.csproj
popd > /dev/null

echo -e "\033[0;36mBuilding Sparky.Specification.Generators...\033[0m"
dotnet build -c Release "$SCRIPT_DIR/Sparky.Specification.Generators/Sparky.Specification.Generators.csproj"

# Function to generate for a specific version
generate_version() {
    local version=$1
    echo -e "\n\033[0;32mGenerating $version provider...\033[0m"

    local package=""
    case $version in
        R4) package="hl7.fhir.r4.core" ;;
        R4B) package="hl7.fhir.r4b.core" ;;
        R5) package="hl7.fhir.r5.core" ;;
        STU3) package="hl7.fhir.r3.core" ;;
        *) echo "Unknown FHIR version: $version"; return 1 ;;
    esac

    # Run fhir-codegen with our custom language
    "$CODEGEN_EXE" \
        --fhir-package "$package" \
        --language CSharpStructureProvider \
        --language-options "{\"OutputDirectory\":\"$OUTPUT_DIR\",\"Namespace\":\"Sparky.Specification.Generated\"}"

    if [ $? -eq 0 ]; then
        echo -e "\033[0;32m✓ Generated $version provider\033[0m"
        return 0
    else
        echo -e "\033[0;31m✗ Failed to generate $version provider\033[0m"
        return 1
    fi
}

# Generate requested versions
if [ "$FHIR_VERSION" == "All" ]; then
    VERSIONS=("R4" "R4B" "R5" "STU3")
else
    VERSIONS=("$FHIR_VERSION")
fi

SUCCESS=true
for version in "${VERSIONS[@]}"; do
    if ! generate_version "$version"; then
        SUCCESS=false
    fi
done

if [ "$SUCCESS" = true ]; then
    echo -e "\n\033[0;32m✓ All providers generated successfully!\033[0m"
    echo -e "\033[0;36mOutput directory: $OUTPUT_DIR\033[0m"
else
    echo -e "\n\033[0;31m✗ Some providers failed to generate\033[0m"
    exit 1
fi

#!/usr/bin/env bash
# Generate FHIR IStructureDefinitionSummaryProvider implementations
# This script uses Ignixa.Specification.Generators to generate providers and metadata

set -e

# Paths
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
GENERATORS_DIR="$SCRIPT_DIR/Ignixa.Specification.Generators"

# Parse arguments
FHIR_VERSION="${1:-All}"

echo -e "\033[0;36mBuilding Ignixa.Specification.Generators...\033[0m"
dotnet build -c Release "$GENERATORS_DIR/Ignixa.Specification.Generators.csproj"

# Function to generate for a specific version
generate_version() {
    local version=$1
    echo -e "\n\033[0;32mGenerating $version provider...\033[0m"

    # Run the generator tool for structure mode (generates reference metadata and providers)
    # Default mode is "structure", so just pass the version
    dotnet run --project "$GENERATORS_DIR/Ignixa.Specification.Generators.csproj" --configuration Release -- "$version"

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
    VERSIONS=("R4" "R4B" "R5" "R6" "STU3")
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
else
    echo -e "\n\033[0;31m✗ Some providers failed to generate\033[0m"
    exit 1
fi

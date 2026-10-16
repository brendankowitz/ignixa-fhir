#!/usr/bin/env bash
# Generate the FHIR specification artifacts Ignixa checks in: per version, the reference metadata
# (src/Core/Ignixa.Specification/Generated/*ReferenceMetadata.g.cs), the runtime core schema providers
# (*CoreSchemaProvider.g.cs) and the code-system mappings (src/Core/Ignixa.Search/Generated/*CodeSystemMappings.g.cs).

set -e

# Paths
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
GENERATORS_DIR="$SCRIPT_DIR/Ignixa.Specification.Generators"

# Parse arguments
FHIR_VERSION="${1:-All}"

echo -e "\033[0;36mBuilding Ignixa.Specification.Generators...\033[0m"
dotnet build -c Release "$GENERATORS_DIR/Ignixa.Specification.Generators.csproj"

# Runs one generator mode for one version and names the step when it fails. generate_version runs
# under `if !`, where `set -e` is off, so every step must return its own failure.
run_step() {
    local version=$1
    local step=$2
    shift 2
    if ! dotnet run --project "$GENERATORS_DIR/Ignixa.Specification.Generators.csproj" --configuration Release -- "$@"; then
        echo -e "\033[0;31m✗ $version: the $step step failed\033[0m" >&2
        return 1
    fi
}

# Function to generate for a specific version
generate_version() {
    local version=$1
    echo -e "\n\033[0;32mGenerating $version provider...\033[0m"

    # Structure mode (default): the reference metadata.
    run_step "$version" structure "$version" || return 1

    # Core-schema mode: the runtime *CoreSchemaProvider.g.cs (element schemas, invariants, slicing
    # metadata). This is a SEPARATE generator mode from "structure" — omitting it silently leaves the
    # runtime providers stale after a CSharpCoreSchemaLanguage change (see PR #310 slicing metadata).
    run_step "$version" coreschema coreschema "$version" || return 1

    # Code-system mode: src/Core/Ignixa.Search/Generated/*CodeSystemMappings.g.cs, the implicit system of each
    # resource `code` element (read by CodeSystemResolver when indexing token search values).
    run_step "$version" codesystem codesystem "$version" || return 1

    echo -e "\033[0;32m✓ Generated $version provider\033[0m"
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

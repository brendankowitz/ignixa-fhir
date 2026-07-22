#!/bin/bash

# Azure Load Testing (ALT) artifact packaging script for FHIR TestScript load testing
#
# Usage:
#   ./package-alt-artifact.sh [--output OUTPUT_PATH] [--staging STAGING_DIR]
#
# Defaults:
#   OUTPUT_PATH: artifacts/alt-testscript-artifact.zip (relative to repo root)
#   STAGING_DIR: System temp directory
#
# Example:
#   ./package-alt-artifact.sh
#   ./package-alt-artifact.sh --output ./my-artifact.zip

set -euo pipefail

# Color codes
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# Parse arguments
OUTPUT_PATH="artifacts/alt-testscript-artifact.zip"
STAGING_DIR=""

while [[ $# -gt 0 ]]; do
    case $1 in
        --output)
            OUTPUT_PATH="$2"
            shift 2
            ;;
        --staging)
            STAGING_DIR="$2"
            shift 2
            ;;
        *)
            echo "Unknown option: $1" >&2
            exit 1
            ;;
    esac
done

# Resolve script location and repo root
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"

echo -e "${CYAN}=== ALT Artifact Packaging ===${NC}"
echo "Script location: $SCRIPT_DIR"
echo "Repo root: $REPO_ROOT"

# Create staging directory
if [[ -z "$STAGING_DIR" ]]; then
    STAGING_DIR=$(mktemp -d -t alt-artifact-staging-XXXXXXXXXX)
fi
echo "Staging directory: $STAGING_DIR"

# Cleanup function
cleanup() {
    if [[ -d "$STAGING_DIR" ]]; then
        echo -e "${NC}Cleaning up staging directory..." >&2
        rm -rf "$STAGING_DIR"
    fi
}
trap cleanup EXIT

# Step 1: Publish ignixa-matrix CLI
echo -e "\n${YELLOW}[1/3] Publishing ignixa-matrix CLI...${NC}"
CLI_PATH="$REPO_ROOT/tools/Ignixa.ConformanceMatrix.Cli/Ignixa.ConformanceMatrix.Cli.csproj"
PUBLISH_OUTPUT="$STAGING_DIR/runner"

if [[ ! -f "$CLI_PATH" ]]; then
    echo -e "${RED}Error: CLI project not found at: $CLI_PATH${NC}" >&2
    exit 1
fi

if ! command -v dotnet &> /dev/null; then
    echo -e "${RED}Error: dotnet command not found. Install .NET SDK.${NC}" >&2
    exit 1
fi

dotnet publish "$CLI_PATH" \
    -c Release \
    -r linux-x64 \
    --self-contained \
    -p:PublishSingleFile=true \
    -p:InvariantGlobalization=true \
    -p:EnableCompressionInSingleFile=true \
    -o "$PUBLISH_OUTPUT" \
    >&2

# Symbols are useless on the ALT engine and eat into the 50 MB zip budget.
rm -f "$PUBLISH_OUTPUT"/*.pdb

echo -e "${GREEN}Published to: $PUBLISH_OUTPUT${NC}"

# Step 2: Copy TestScripts
echo -e "\n${YELLOW}[2/3] Copying TestScripts...${NC}"
TESTSCRIPTS_SOURCE="$REPO_ROOT/src/Core/Ignixa.TestScript.Suites/testscripts"
TESTSCRIPTS_DEST="$STAGING_DIR/testscripts"

if [[ ! -d "$TESTSCRIPTS_SOURCE" ]]; then
    echo -e "${RED}Error: TestScripts source not found at: $TESTSCRIPTS_SOURCE${NC}" >&2
    exit 1
fi

cp -r "$TESTSCRIPTS_SOURCE" "$TESTSCRIPTS_DEST"
SCRIPT_COUNT=$(find "$TESTSCRIPTS_DEST" -type f | wc -l)
echo -e "${GREEN}Copied $SCRIPT_COUNT TestScript files${NC}"

# Deliberately NOT bundling locustfile.py / requirements.txt / locust.conf:
# those upload to ALT as the test plan and configuration files, and ALT
# auto-extracts the zip next to the test plan — a stale copy inside the zip
# would clobber the uploaded locustfile.

# Step 3: Create zip artifact
echo -e "\n${YELLOW}[3/3] Creating zip artifact...${NC}"

# Check if zip is available
if ! command -v zip &> /dev/null; then
    echo -e "${RED}Error: zip command not found. Install zip utility.${NC}" >&2
    exit 1
fi

# Resolve output path (may be relative)
if [[ "$OUTPUT_PATH" = /* ]]; then
    RESOLVED_OUTPUT="$OUTPUT_PATH"
else
    RESOLVED_OUTPUT="$REPO_ROOT/$OUTPUT_PATH"
fi

OUTPUT_DIR=$(dirname "$RESOLVED_OUTPUT")
if [[ ! -d "$OUTPUT_DIR" ]]; then
    mkdir -p "$OUTPUT_DIR"
fi

# Remove existing zip
if [[ -f "$RESOLVED_OUTPUT" ]]; then
    rm "$RESOLVED_OUTPUT"
fi

# Create zip (suppress verbose output)
cd "$STAGING_DIR"
zip -r -q "$RESOLVED_OUTPUT" . 2>&1 || {
    echo -e "${RED}Error: zip command failed${NC}" >&2
    exit 1
}
cd - > /dev/null

echo -e "${GREEN}Created artifact: $RESOLVED_OUTPUT${NC}"

# Report size
ZIP_SIZE_BYTES=$(stat -f%z "$RESOLVED_OUTPUT" 2>/dev/null || stat -c%s "$RESOLVED_OUTPUT" 2>/dev/null)
ZIP_SIZE_MB=$(echo "scale=2; $ZIP_SIZE_BYTES / 1048576" | bc)

echo -e "\n${CYAN}=== Packaging Summary ===${NC}"
echo "Artifact size: ${ZIP_SIZE_MB} MB"
echo "ALT limit: 50 MB per zip"

# Check size limit (50 MB = 52428800 bytes)
if (( ZIP_SIZE_BYTES > 52428800 )); then
    echo -e "${RED}WARNING: Artifact exceeds 50 MB limit! ALT will reject this file.${NC}"
    echo "Recommendations:"
    echo "  - Split testscripts across multiple zips"
    echo "  - Enable more aggressive compression"
    echo "  - Remove unused TestScript suites"
else
    HEADROOM_MB=$(echo "scale=2; (52428800 - $ZIP_SIZE_BYTES) / 1048576" | bc)
    echo -e "${GREEN}Headroom: ${HEADROOM_MB} MB${NC}"
fi

FILE_COUNT=$(find "$STAGING_DIR" -type f | wc -l 2>/dev/null || echo "?")
echo -e "${GREEN}Files: $FILE_COUNT total${NC}"
echo -e "\n${GREEN}Upload to Azure Load Testing together with locustfile.py (test plan),${NC}"
echo -e "${GREEN}requirements.txt and locust.conf (configuration files) from this folder.${NC}"

#!/usr/bin/env bash
# -------------------------------------------------------------------------------------------------
# Regen-drift guard for the typed-model generator.
#
# Regenerates the typed-model output and fails if it differs from the output already on disk.
# Catches: stale generated code, and classification churn (e.g. when a new FHIR version moves a
# type from identical/additive -> incompatible, demoting an element from base to per-version).
#
# It compares a content snapshot of the generated dirs taken BEFORE and AFTER regeneration, so it
# works whether or not the generated output is committed yet. In CI (where the output IS committed)
# this is equivalent to "regenerate, then assert no git diff".
#
# Run locally or in CI:  build/check-typed-model-regen.sh
# Requires the FHIR packages (cached offline in this repo); does NOT hit the network in CI.
# -------------------------------------------------------------------------------------------------
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

generated_dirs=(
    "src/Core/Ignixa.Serialization/Generated/Models"
    "src/Core/Models/Ignixa.Models.R4/Generated"
    "src/Core/Models/Ignixa.Models.R5/Generated"
)

snapshot() {
    # sha256sum on Linux; shasum -a 256 on macOS. Guard the empty-dir case so xargs does not
    # block on stdin when find yields nothing.
    local sha_cmd="sha256sum"
    if ! command -v sha256sum >/dev/null 2>&1; then
        sha_cmd="shasum -a 256"
    fi

    for dir in "${generated_dirs[@]}"; do
        if [ -d "$dir" ] && [ -n "$(find "$dir" -type f -print -quit)" ]; then
            find "$dir" -type f -print0 | sort -z | xargs -0 $sha_cmd
        fi
    done
}

before="$(snapshot)"

echo "Regenerating typed-model output..."
dotnet run --project codegen/Ignixa.Specification.Generators -- typed-model -p:DisableGitVersion=true

after="$(snapshot)"

if [ "$before" = "$after" ]; then
    echo "OK: generated typed-model output is up to date."
    exit 0
fi

echo "DRIFT: typed-model output changed after regeneration. Commit the regenerated files:"
echo "  dotnet run --project codegen/Ignixa.Specification.Generators -- typed-model"
echo ""
git --no-pager diff -- "${generated_dirs[@]}" || true
git status --porcelain -- "${generated_dirs[@]}" || true
exit 1

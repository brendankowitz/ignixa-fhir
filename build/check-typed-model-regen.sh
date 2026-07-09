#!/usr/bin/env bash
# -------------------------------------------------------------------------------------------------
# Regen-drift guard for the typed-model generator.
#
# Regenerates the typed-model output and fails if it differs from the output already on disk.
# Catches: content drift in generated files, AND classification churn that changes WHICH files get
# emitted (e.g. a value-set gains/loses codes between versions and an element demotes from base to
# per-version). The generator wipes each output directory before regenerating (see CleanGeneratedDir
# in Program.cs) specifically so a file the current classification no longer produces is actually
# absent from the "after" snapshot -- not left behind with stale, unchanged content the way a
# create/overwrite-only emitter would leave it (which this guard's content-hash comparison alone
# could not have detected).
#
# It compares a content snapshot of the generated dirs taken BEFORE and AFTER regeneration, so it
# works whether or not the generated output is committed yet. Once wired into CI (where the output IS
# committed), this would be equivalent to "regenerate, then assert no git diff".
#
# Run locally:  build/check-typed-model-regen.sh
# NOT YET wired into CI or a pre-commit hook -- run it manually before committing generated changes.
# Requires the FHIR packages already present in your local Firely SDK package cache (this repo does
# not ship or check in that cache); does not otherwise hit the network.
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
dotnet run --project codegen/Ignixa.Specification.Generators -- typed-model

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

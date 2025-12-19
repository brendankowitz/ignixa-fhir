# Accept Implemented ADR

Move an implemented ADR from feature folder to `docs/adr/` as accepted documentation.

**Usage**: `/fn-accept {feature-name} {adr-filename}`

## Instructions

1. **Verify implementation is complete**: The feature described in the ADR should be implemented and working

2. **Read the proposed ADR** from `docs/features/{feature-name}/{adr-filename}.md`

3. **Final trim pass** - Remove any content that's not needed for documentation:
   - No implementation notes or TODOs
   - No phase tracking or status checklists
   - Keep only: Context, Decision, Consequences
   - Ensure it's concise (fits on one screen ideally)

4. **Update status**:
   - Change `**Status**: Proposed` to `**Status**: Accepted`
   - Update date to current date

5. **Move to `docs/adr/`**:
   - Copy file to `docs/adr/{adr-filename}.md`
   - Verify it follows naming convention: `adr-{YYMM}-{topic}.md`

6. **Update feature status**:
   - Change `readme.md` status to "Complete"
   - Update Decision section with link to final ADR location

7. **Output**: Confirm the ADR was accepted and show final location

## Post-Acceptance

The feature folder remains for historical context. The investigations document the exploration process; the accepted ADR in `docs/adr/` is the authoritative decision record.

Future changes that supersede this ADR should:
1. Create a new feature area (or reuse existing)
2. Reference the original ADR being superseded
3. Follow the full investigation -> ADR flow

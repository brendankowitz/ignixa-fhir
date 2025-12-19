# Feature: FHIR Compatibility Improvements

**Status**: Proposed
**Created**: 2025-10-28

## Problem Statement

Current compatibility test results show 22.2% pass rate (199/898 tests). Analysis reveals 10 distinct failure patterns that can be fixed systematically to reach 65%+ pass rate.

## Constraints

- Must maintain backwards compatibility with existing clients
- Quick wins prioritized (highest ROI fixes first)
- OperationOutcome format must comply with FHIR spec
- Bundle JSON must be complete and valid
- Timezone handling must use UTC

## Investigations

| Investigation | Status | Summary |
|--------------|--------|---------|
| [compatibility-remediation](investigations/compatibility-remediation.md) | Viable | 3-phase remediation plan targeting +35% pass rate with 8 hours of quick wins |
| [isourcenode-consolidation](investigations/isourcenode-consolidation.md) | Complete | ISourceNode renamed to ISourceNavigator with clear separation from IElement for parsing vs semantic concerns |

## Decision

*No ADR yet - investigations in progress*

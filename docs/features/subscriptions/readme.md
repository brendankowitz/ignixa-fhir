# Feature: FHIR Subscriptions

**Status**: Investigation Complete
**Created**: 2025-11-04

## Problem Statement

FHIR Subscriptions enable proactive event notifications from server to clients. This is essential for real-time integrations, care coordination, and monitoring workflows.

## Constraints

- Must support R4 criteria-based subscriptions
- Must support R5 SubscriptionTopic for future compatibility
- Channel types: rest-hook (required), websocket (optional), email/sms (future)
- Must integrate with existing transaction infrastructure
- Must handle subscription lifecycle (requested, active, error, off)

## Investigations

| Investigation | Status | Summary |
|--------------|--------|---------|
| [subscription-engine](investigations/subscription-engine.md) | Complete | Investigation complete, proven patterns from Microsoft reference and fhir-candle ready for integration |
| [transaction-table](investigations/transaction-table.md) | Complete | Event-driven subscriptions leveraging existing transaction table infrastructure for guaranteed delivery |

## Decision

*No ADR yet - implementation planning based on research findings*

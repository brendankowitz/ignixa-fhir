---
sidebar_position: 3
title: Subscriptions
description: Real-time FHIR notifications (planned)
---

# Subscriptions

:::caution Not Yet Implemented
FHIR Subscriptions are planned but not yet implemented in Ignixa.
:::

## Planned Features

The following subscription capabilities are planned for future releases:

- **R4 Subscriptions** - Criteria-based notifications
- **R5 Topic-Based Subscriptions** - SubscriptionTopic and enhanced filtering
- **Channel types** - REST hooks, WebSockets
- **Notification bundles** - Event and handshake notifications

## Workarounds

Until subscriptions are implemented, consider:

- **Polling** - Periodically query for changes using `_lastUpdated` search parameter
- **Bulk export** - Use `$export` with `_since` parameter for batch change detection

## Related Documentation

- [Bulk Operations](/docs/server/features/bulk-operations) - Export changes in batch
- [Search Parameters](/docs/server/fhir/search-parameters) - Query for recent changes

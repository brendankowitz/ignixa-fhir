---
sidebar_position: 3
title: Subscriptions
description: Real-time FHIR notifications
---

# Subscriptions

Ignixa supports FHIR Subscriptions for real-time notifications when resources change.

## Overview

Subscriptions allow clients to receive notifications when resources matching specified criteria are created, updated, or deleted.

## Creating a Subscription

```bash
POST /Subscription
Content-Type: application/fhir+json

{
  "resourceType": "Subscription",
  "status": "requested",
  "reason": "Monitor new patient admissions",
  "criteria": "Encounter?status=in-progress",
  "channel": {
    "type": "rest-hook",
    "endpoint": "https://example.org/webhook/fhir",
    "payload": "application/fhir+json",
    "header": [
      "Authorization: Bearer secret-token"
    ]
  }
}
```

## Channel Types

### REST Hook

Sends HTTP POST to endpoint:

```json
{
  "channel": {
    "type": "rest-hook",
    "endpoint": "https://example.org/webhook",
    "payload": "application/fhir+json"
  }
}
```

### WebSocket

Real-time WebSocket connection:

```json
{
  "channel": {
    "type": "websocket"
  }
}
```

### Email

Email notifications:

```json
{
  "channel": {
    "type": "email",
    "endpoint": "mailto:alerts@example.org",
    "payload": "application/fhir+json"
  }
}
```

## Payload Types

| Type | Description |
|------|-------------|
| `empty` | No payload, just notification |
| `id-only` | Only resource type and ID |
| `full-resource` | Complete resource |
| `application/fhir+json` | JSON format |
| `application/fhir+xml` | XML format |

## Criteria

Filter which resources trigger notifications:

```json
// All new patients
"criteria": "Patient"

// Observations with specific code
"criteria": "Observation?code=http://loinc.org|29463-7"

// Encounters for a patient
"criteria": "Encounter?patient=Patient/123"

// Critical lab results
"criteria": "Observation?code=http://loinc.org|94500-6&value-quantity=gt40"
```

## Subscription Status

| Status | Description |
|--------|-------------|
| `requested` | Initial state, pending activation |
| `active` | Subscription is active |
| `error` | Delivery failed |
| `off` | Manually disabled |

## R5 Topic-Based Subscriptions

Ignixa supports R5 Topic-Based Subscriptions:

### SubscriptionTopic

```json
{
  "resourceType": "SubscriptionTopic",
  "url": "http://example.org/SubscriptionTopic/patient-admission",
  "title": "Patient Admission",
  "status": "active",
  "resourceTrigger": [{
    "resource": "Encounter",
    "supportedInteraction": ["create", "update"],
    "queryCriteria": {
      "current": "status=in-progress"
    }
  }]
}
```

### R5 Subscription

```json
{
  "resourceType": "Subscription",
  "status": "requested",
  "topic": "http://example.org/SubscriptionTopic/patient-admission",
  "channelType": {
    "system": "http://terminology.hl7.org/CodeSystem/subscription-channel-type",
    "code": "rest-hook"
  },
  "endpoint": "https://example.org/webhook",
  "contentType": "application/fhir+json"
}
```

## Notification Bundle

When triggered, Ignixa sends a notification bundle:

```json
{
  "resourceType": "Bundle",
  "type": "history",
  "entry": [{
    "resource": {
      "resourceType": "SubscriptionStatus",
      "subscription": { "reference": "Subscription/123" },
      "topic": "http://example.org/SubscriptionTopic/patient-admission",
      "notificationType": "event-notification"
    }
  }, {
    "resource": {
      "resourceType": "Encounter",
      "id": "456",
      // ... full resource
    },
    "request": {
      "method": "POST",
      "url": "Encounter"
    }
  }]
}
```

## Error Handling

### Retry Policy

Failed deliveries are retried with exponential backoff:

| Attempt | Delay |
|---------|-------|
| 1 | 1 minute |
| 2 | 5 minutes |
| 3 | 30 minutes |
| 4 | 2 hours |
| 5 | 24 hours |

After 5 failures, subscription status changes to `error`.

### $status Operation

Check subscription delivery status:

```bash
GET /Subscription/123/$status
```

## Configuration

```json
{
  "Subscriptions": {
    "Enabled": true,
    "MaxRetries": 5,
    "MaxActiveSubscriptions": 100,
    "WebhookTimeout": 30,
    "AllowedChannelTypes": ["rest-hook", "websocket"]
  }
}
```

## Security Considerations

- Validate webhook endpoints
- Use HTTPS for all endpoints
- Include authentication headers
- Limit subscription criteria complexity

## Related Documentation

- [ADR: Subscriptions](/docs/adr/)
- [Security](/docs/server/security/authentication)

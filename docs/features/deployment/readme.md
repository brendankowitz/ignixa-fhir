# Feature: Azure Container Apps Deployment

**Status**: Research
**Created**: 2025-10-28

## Problem Statement

Production deployment of the FHIR server requires containerization, observability (health checks, metrics, tracing), and automated CI/CD. Azure Container Apps provides serverless scaling with container benefits.

## Constraints

- Must support Azure SQL Database backend
- Must integrate Application Insights and Prometheus metrics
- Health checks required for Kubernetes-style probes
- CI/CD via GitHub Actions
- Cost target: $40-80/month for production workloads

## Investigations

| Investigation | Status | Summary |
|--------------|--------|---------|
| [azure-container-apps](investigations/azure-container-apps.md) | Viable | Multi-stage Dockerfile design, ACA configuration, observability stack |

## Decision

*No ADR yet - research in progress*

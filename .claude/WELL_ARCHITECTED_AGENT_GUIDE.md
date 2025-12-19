# Well-Architected Agent Guide

## Overview

The Well-Architected Agent is a specialized Claude Code agent that conducts comprehensive code and architecture reviews based on Microsoft's **Azure Well-Architected Framework**. It evaluates your codebase against five core pillars to identify risks, opportunities, and best practices.

## Five Pillars of the Well-Architected Framework

### 1. 🛡️ Reliability
Ensures your workload meets uptime and recovery targets through:
- **Resilience patterns** (retry logic, circuit breakers, timeouts)
- **Error handling** (comprehensive try-catch, graceful degradation)
- **Data reliability** (transactions, validation, consistency)
- **Health monitoring** (health checks, observability)

### 2. 🔒 Security
Protects your workload from attacks and maintains data integrity:
- **Authentication & Authorization** (identity management, RBAC)
- **Data protection** (encryption at rest/transit, PII handling)
- **Input validation** (SQL injection, XSS prevention)
- **Secrets management** (no hardcoded credentials)
- **Security configuration** (CORS, CSRF, TLS)

### 3. 💰 Cost Optimization
Optimizes resource utilization and eliminates waste:
- **Resource efficiency** (connection pooling, memory management)
- **Data transfer optimization** (caching, compression, pagination)
- **Compute efficiency** (async operations, lazy loading)
- **Waste reduction** (no dead code, proper resource cleanup)

### 4. ⚙️ Operational Excellence
Reduces production issues through DevOps practices:
- **Observability** (structured logging, metrics, tracing)
- **Code quality** (naming, documentation, low complexity)
- **Testing** (unit tests, integration tests, AAA pattern)
- **DevOps practices** (CI/CD, feature flags, health probes)
- **Automation** (automated tests, linting, validation)

### 5. ⚡ Performance Efficiency
Ensures system scalability and responsiveness:
- **Scalability** (horizontal scaling, async processing)
- **Data access** (query optimization, connection pooling)
- **Caching** (memory/distributed caching, invalidation)
- **Algorithm efficiency** (time/space complexity)
- **Resource management** (no memory leaks, efficient GC)

---

## Usage

### Slash Commands

The Well-Architected Agent can be invoked using several slash commands:

#### Full Architecture Review
```
/wa-review
```
Conducts a comprehensive review across all five pillars. Best for:
- Pre-production readiness assessments
- Quarterly architecture reviews
- Major release evaluations
- Onboarding new team members to codebase quality standards

#### Focused Pillar Reviews

**Security Audit:**
```
/wa-security
```
Deep dive into security vulnerabilities and best practices. Use when:
- Preparing for security audits
- Implementing authentication/authorization
- Handling sensitive data (PII, PHI)
- Responding to security incidents

**Performance Analysis:**
```
/wa-performance
```
Identify performance bottlenecks and optimization opportunities. Use when:
- Experiencing performance issues
- Preparing for load testing
- Optimizing database queries
- Reducing response times

**Reliability Assessment:**
```
/wa-reliability
```
Evaluate resilience and fault tolerance. Use when:
- Implementing error handling
- Adding retry/circuit breaker patterns
- Improving system availability
- Preparing disaster recovery plans

---

### Direct Agent Invocation

You can also invoke the agent directly using the Task tool:

#### Full Review Example
```
Use the well-architected-agent to conduct a comprehensive review of the Ignixa.Api project,
focusing on all five pillars. Pay special attention to FHIR-specific security requirements
and multi-tenancy isolation.
```

#### Targeted Feature Review Example
```
Use the well-architected-agent to review the Patient search implementation in
src/Ignixa.Application/Features/Patient/ against the Performance Efficiency and
Reliability pillars. Check for N+1 queries and proper error handling.
```

#### Pre-Production Readiness Example
```
Use the well-architected-agent to assess production readiness of the Bundle streaming
feature. Focus on Reliability and Operational Excellence pillars - verify health checks,
logging, error handling, and monitoring are in place.
```

---

## Review Output

The agent provides structured, actionable output:

### 1. Executive Summary
- Overall health score
- Critical issues count
- Pillar-by-pillar scores (X/10)

### 2. Prioritized Findings

**🚨 Critical (P0) - Immediate Action Required**
- Security vulnerabilities
- Data corruption risks
- Availability threats
- Specific file locations with line numbers

**⚠️ High Priority (P1) - Address Soon**
- Performance bottlenecks
- Missing resilience patterns
- Operational gaps
- Effort estimates provided

**ℹ️ Medium Priority (P2) - Plan for Future**
- Code quality improvements
- Optimization opportunities
- Technical debt reduction

### 3. Strengths & Best Practices
**✅** Highlights what's done well to reinforce positive patterns

### 4. Detailed Pillar Analysis
- Score and status per pillar
- Specific findings with file references
- Gaps and recommendations

### 5. Implementation Roadmap
- Phased approach (immediate, short-term, medium-term, long-term)
- Prioritized by risk and impact

---

## Example Scenarios

### Scenario 1: New Feature Pre-Merge Review
**Context:** You've implemented FHIR Subscriptions and want to ensure quality before merging.

**Command:**
```
Use the well-architected-agent to review the FHIR Subscriptions implementation in
src/Ignixa.Application/Features/Subscriptions/ and src/Ignixa.Api/Infrastructure/SubscriptionEndpoints.cs

Focus on:
- Reliability: Error handling, retry logic for webhook delivery
- Security: Authorization checks, webhook URL validation
- Performance: Async processing, connection pooling
- Operational Excellence: Logging, metrics for subscription events
```

**Expected Output:**
- Specific code locations with issues
- Security vulnerabilities (e.g., webhook URL validation missing)
- Performance concerns (e.g., synchronous webhook calls blocking)
- Logging gaps
- Actionable recommendations with effort estimates

---

### Scenario 2: Production Incident Post-Mortem
**Context:** Database connection pool exhaustion caused an outage.

**Command:**
```
/wa-reliability

Investigate database connection management patterns across the codebase.
Check for:
- Proper connection pooling configuration
- Missing using statements or IDisposable implementations
- Long-running queries without timeouts
- Connection leaks
```

**Expected Output:**
- All locations creating database connections
- Missing disposal patterns
- Timeout configurations
- Circuit breaker recommendations
- Resource limit configurations

---

### Scenario 3: Security Compliance Audit
**Context:** Preparing for HITRUST compliance audit.

**Command:**
```
/wa-security

Conduct a security audit for HITRUST compliance focusing on:
- PHI data encryption (at rest and in transit)
- Access control and authorization
- Audit logging for PHI access
- Input validation and injection prevention
- Secrets management (API keys, connection strings)
```

**Expected Output:**
- Hardcoded secrets or credentials
- Missing encryption implementations
- Authorization gaps
- Logging of sensitive data
- Input validation weaknesses
- Compliance gaps with specific remediation steps

---

### Scenario 4: Performance Optimization Sprint
**Context:** Response times for Patient search are too slow.

**Command:**
```
/wa-performance

Analyze the Patient search implementation for performance issues:
- Files: src/Ignixa.Application/Features/Patient/SearchPatientHandler.cs
- Files: src/Ignixa.DataLayer/Repositories/PatientRepository.cs

Check for:
- N+1 query patterns
- Missing indexes
- Inefficient LINQ queries
- Lack of caching
- Pagination issues
```

**Expected Output:**
- Specific query inefficiencies with line numbers
- Index recommendations
- Caching opportunities
- Pagination improvements
- Before/after code examples
- Expected performance gains

---

## Integration with Project Workflow

### Pre-Commit Reviews
Use targeted pillar reviews during development:
```
# Before committing authentication changes
/wa-security

# Before committing search optimization
/wa-performance
```

### Pull Request Reviews
Include in PR checklist:
```
- [ ] Code reviewed by team member
- [ ] Unit tests added/updated
- [ ] /wa-review run with no critical issues
- [ ] Documentation updated
```

### Sprint Planning
Plan remediation work:
1. Run `/wa-review` at sprint start
2. Export P0 and P1 findings
3. Add to sprint backlog with effort estimates
4. Track progress with ADR updates

### Production Readiness
Before deploying to production:
```
/wa-review

Focus on production readiness checklist:
- Health checks implemented
- Monitoring and alerting configured
- Error handling comprehensive
- Secrets externalized
- Performance tested under load
- Security headers configured
- Backup and recovery tested
```

---

## Best Practices

### 1. Run Reviews Regularly
- **Weekly:** During active development (focused pillar reviews)
- **Sprint End:** Full review before release
- **Monthly:** Full architecture review
- **Quarterly:** Comprehensive assessment with roadmap update

### 2. Prioritize Findings
- **P0 (Critical):** Address before production deployment
- **P1 (High):** Include in current sprint/release
- **P2 (Medium):** Plan for next quarter

### 3. Track Improvements
- Document findings in ADRs
- Create GitHub issues for P0/P1 items
- Measure progress with periodic re-reviews

### 4. Context-Specific Reviews
Provide context for better analysis:
```
# Good: Specific context
Use well-architected-agent to review the multi-tenant routing logic in
TenantResolutionMiddleware.cs. This is a critical security boundary -
ensure partition isolation is enforced correctly.

# Better: Include business context
Use well-architected-agent to review Patient data export feature.
This handles PHI data and must comply with HIPAA. Focus on Security
and Operational Excellence pillars, especially audit logging.
```

### 5. Combine with Other Agents
```
# Step 1: Review against ADR requirements
Use adr-analyzer to verify implementation matches ADR-2530

# Step 2: Well-Architected review
/wa-review on the Subscriptions feature

# Step 3: Code review
Use coding-agent to refactor any issues found
```

---

## Interpreting Scores

### Pillar Scores (X/10)

| Score | Status | Meaning | Action |
|-------|--------|---------|--------|
| 9-10 | ✅ Excellent | Best practices followed, minimal issues | Maintain standards |
| 7-8 | 🟢 Good | Solid implementation, minor improvements | Address P2 items |
| 5-6 | 🟡 Fair | Some gaps, needs attention | Prioritize P1 items |
| 3-4 | 🟠 Needs Improvement | Significant issues, risks present | Address P0/P1 immediately |
| 0-2 | 🔴 Critical | Major risks, production readiness blocked | Stop and remediate P0 |

### Overall Health Assessment

**Excellent:** All pillars 8+, no critical issues
- Production ready
- Maintenance mode

**Good:** Most pillars 6+, few high-priority issues
- Production ready with monitoring
- Plan improvements for next sprint

**Fair:** Mixed scores, some critical issues
- Address critical issues before production
- Create remediation plan

**Needs Improvement:** Multiple pillars below 6, many critical issues
- Not production ready
- Requires architecture review and refactoring

**Critical:** Multiple pillars below 4, systemic issues
- Significant rework required
- Escalate to architecture review board

---

## Tips for Maximum Value

### 1. Be Specific About Scope
```
# Instead of: "Review the API"
# Use: "Review src/Ignixa.Api/Infrastructure/FhirEndpoints.cs focusing on
#       input validation, error handling, and authentication"
```

### 2. Ask Follow-Up Questions
```
# After review, dig deeper:
"The review found N+1 queries in PatientRepository. Show me specific
examples with recommended fixes using Entity Framework Include()."
```

### 3. Request Remediation Plans
```
"Based on the P0 security findings, create a detailed remediation plan
with code examples and effort estimates for each item."
```

### 4. Compare Before/After
```
# After implementing recommendations:
"Re-run /wa-performance on PatientSearchHandler and compare scores
to the previous review. Highlight improvements made."
```

### 5. Export Findings for Team
```
"Format the critical and high-priority findings as GitHub issues with:
- Title, description, file references
- Acceptance criteria
- Effort estimates
- Labels (security/performance/reliability)"
```

---

## Limitations & Considerations

### What the Agent DOES:
✅ Analyzes code structure and patterns
✅ Identifies common anti-patterns
✅ Applies Well-Architected Framework principles
✅ Provides specific file references and recommendations
✅ Estimates relative effort
✅ Prioritizes by risk and impact

### What the Agent DOESN'T:
❌ Execute code or run tests
❌ Access runtime metrics or logs
❌ Perform penetration testing
❌ Guarantee absence of all issues
❌ Replace human code review
❌ Provide absolute time estimates

### Best Used Alongside:
- **Manual code reviews** - Human judgment and context
- **Automated testing** - Unit, integration, performance tests
- **Static analysis tools** - SonarQube, Roslyn analyzers
- **Runtime monitoring** - Application Insights, logging
- **Security scanning** - OWASP ZAP, dependency scanners

---

## Feedback & Improvement

The Well-Architected Agent is continuously improved based on:
- Azure Well-Architected Framework updates
- Project-specific patterns (CLAUDE.md)
- Team feedback
- Industry best practices

**Share Feedback:**
- False positives: Help tune detection patterns
- Missing patterns: Suggest new checks
- Unclear recommendations: Request clarification
- Success stories: Share improvements achieved

---

## Quick Reference

| Command | Purpose | When to Use |
|---------|---------|-------------|
| `/wa-review` | Full assessment (all 5 pillars) | Pre-production, quarterly reviews |
| `/wa-security` | Security audit | Before handling sensitive data, security audits |
| `/wa-performance` | Performance analysis | Performance issues, optimization sprints |
| `/wa-reliability` | Reliability assessment | Incident post-mortems, resilience improvements |

**Priority Levels:**
- 🚨 **P0 Critical** - Fix before production
- ⚠️ **P1 High** - Fix this sprint/release
- ℹ️ **P2 Medium** - Plan for next quarter

**Pillar Emoji Guide:**
- 🛡️ Reliability
- 🔒 Security
- 💰 Cost Optimization
- ⚙️ Operational Excellence
- ⚡ Performance Efficiency

---

## Additional Resources

- [Azure Well-Architected Framework Documentation](https://learn.microsoft.com/en-us/azure/well-architected/)
- [Azure Architecture Center](https://learn.microsoft.com/en-us/azure/architecture/)
- [Well-Architected Review Assessment Tool](https://learn.microsoft.com/en-us/assessments/azure-architecture-review/)
- Project CLAUDE.md - Coding standards and architecture rules
- Project ADRs - Architecture decision records (docs/adr/)

---

**Version:** 1.0
**Last Updated:** December 2025
**Maintained By:** Development Team

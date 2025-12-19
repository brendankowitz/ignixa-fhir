# Well-Architected Framework Agent for Claude Code

## 🎯 Overview

This agent brings the **Microsoft Azure Well-Architected Framework** to your Claude Code development workflow, enabling comprehensive code and architecture reviews based on industry-standard cloud architecture principles.

### What is the Azure Well-Architected Framework?

The Azure Well-Architected Framework is a set of quality-driven tenets and architectural decision points that help organizations build robust, secure, cost-effective, and high-performing applications. It's built on **five core pillars**:

1. **🛡️ Reliability** - Build resilient systems that recover from failures
2. **🔒 Security** - Protect data and systems from threats
3. **💰 Cost Optimization** - Maximize efficiency and eliminate waste
4. **⚙️ Operational Excellence** - Streamline operations with DevOps practices
5. **⚡ Performance Efficiency** - Scale and perform under load

---

## 📦 What's Included

This implementation provides:

### 1. Well-Architected Agent
**File:** `.claude/agents/well-architected-agent.md`

A specialized Claude Code agent that:
- Evaluates code against all five WAF pillars
- Identifies critical security vulnerabilities
- Detects performance bottlenecks and N+1 queries
- Validates error handling and resilience patterns
- Reviews observability and operational readiness
- Provides prioritized, actionable recommendations with effort estimates

### 2. Slash Commands
Quick-access commands for common review scenarios:

| Command | File | Purpose |
|---------|------|---------|
| `/wa-review` | `.claude/commands/wa-review.md` | Full 5-pillar assessment |
| `/wa-security` | `.claude/commands/wa-security.md` | Security-focused audit |
| `/wa-performance` | `.claude/commands/wa-performance.md` | Performance analysis |
| `/wa-reliability` | `.claude/commands/wa-reliability.md` | Reliability assessment |

### 3. Documentation

| Document | Purpose |
|----------|---------|
| `docs/guides/well-architected-agent-guide.md` | Comprehensive guide with examples and best practices |
| `docs/guides/wa-quick-reference.md` | Quick reference card for daily use |
| `docs/guides/well-architected-readme.md` | This overview document |

---

## 🚀 Quick Start

### Basic Usage

1. **Full Architecture Review**
   ```
   /wa-review
   ```
   Evaluates your entire codebase against all five pillars.

2. **Focused Security Audit**
   ```
   /wa-security
   ```
   Deep dive into security vulnerabilities and best practices.

3. **Performance Analysis**
   ```
   /wa-performance
   ```
   Identifies performance bottlenecks and optimization opportunities.

### Example Scenarios

#### Pre-Production Readiness Check
```
Use well-architected-agent to assess production readiness of the Patient Search feature.

Focus on:
- Reliability: Error handling, timeouts, retry logic
- Security: Authorization, input validation, PHI protection
- Operational Excellence: Logging, health checks, monitoring
```

#### Security Compliance Audit
```
/wa-security

Audit the Authentication and Authorization implementation for HIPAA compliance.
Check for hardcoded secrets, proper encryption, and audit logging.
```

#### Performance Optimization
```
/wa-performance

Analyze src/Ignixa.Application/Features/Patient/SearchPatientHandler.cs
for performance issues. Check for N+1 queries, missing caching, and
inefficient LINQ operations.
```

---

## 📊 Review Output Structure

The agent provides structured, actionable output:

### 1. Executive Summary
- Overall health assessment
- Pillar-by-pillar scores (0-10)
- Critical issue count

### 2. Prioritized Findings

#### 🚨 Critical (P0) - Fix Before Production
Example:
```
🚨 Security - Hardcoded Database Credentials
   Location: appsettings.json:15
   Impact: Database compromise risk
   Recommendation: Move to Azure Key Vault
   Effort: Small (2-4 hours)
```

#### ⚠️ High Priority (P1) - Address This Sprint
Example:
```
⚠️ Performance - N+1 Query Pattern
   Location: PatientRepository.cs:42
   Impact: 100+ database queries for 100 patients
   Recommendation: Use Include() for eager loading
   Effort: Small (1-2 hours)
```

#### ℹ️ Medium Priority (P2) - Plan for Next Quarter
Example:
```
ℹ️ Operational Excellence - Missing XML Documentation
   Location: IPatientRepository.cs
   Impact: Reduced code maintainability
   Recommendation: Add XML comments to public interfaces
   Effort: Medium (4-8 hours)
```

### 3. Strengths & Best Practices
```
✅ Security - Proper Authorization Middleware
   Location: Program.cs:78
   Description: Correctly implements JWT authentication
   Pattern: Authentication/Authorization Middleware
```

### 4. Implementation Roadmap
Phased approach with timelines and dependencies.

---

## 🎓 Common Use Cases

### During Development

**Before Committing:**
```bash
# If touching security code
/wa-security

# If optimizing queries
/wa-performance

# If adding error handling
/wa-reliability
```

**Pull Request Review:**
```bash
# Full feature review
/wa-review

# Include findings in PR description
```

### Production Deployment

**Pre-Production Checklist:**
```
Use well-architected-agent to verify production readiness:
- Health endpoints implemented (/health, /ready)
- Error handling comprehensive with proper logging
- Secrets externalized to Key Vault
- Security headers configured
- Performance tested under expected load
- Monitoring and alerting configured
```

### Post-Incident Analysis

**After Outage:**
```
/wa-reliability

Investigate the database connection pool exhaustion incident.
Review all connection management code for:
- Proper disposal patterns
- Connection pooling configuration
- Timeout settings
- Circuit breaker implementation
```

### Compliance Audits

**HIPAA/HITRUST Preparation:**
```
/wa-security

Conduct security audit for HITRUST compliance:
- PHI encryption at rest and in transit
- Access controls and authorization
- Audit logging for PHI access
- Secrets management
- Input validation
```

---

## 🔍 What the Agent Checks

### Reliability Pillar
- ✅ Comprehensive error handling (try-catch)
- ✅ Retry logic with exponential backoff
- ✅ Circuit breakers for external dependencies
- ✅ Timeouts on all async operations (CancellationToken)
- ✅ Graceful degradation patterns
- ✅ Health check endpoints
- ✅ Idempotent operation design
- ❌ Empty catch blocks
- ❌ Missing timeout configurations
- ❌ Single points of failure

### Security Pillar
- ✅ Authentication & authorization on all endpoints
- ✅ Input validation and sanitization
- ✅ Parameterized queries (no SQL injection)
- ✅ Secrets in secure vaults (no hardcoded credentials)
- ✅ Encryption at rest and in transit
- ✅ Security headers (CORS, CSRF, CSP)
- ✅ PII/PHI proper handling
- ❌ Hardcoded passwords/API keys
- ❌ SQL string concatenation
- ❌ Missing authorization checks
- ❌ Logging sensitive data

### Cost Optimization Pillar
- ✅ Connection pooling (database, HTTP)
- ✅ Proper resource disposal (using statements)
- ✅ Caching strategies
- ✅ Efficient queries (no N+1 patterns)
- ✅ Pagination for large datasets
- ✅ Async/await for I/O operations
- ❌ New HttpClient per request
- ❌ Memory leaks (missing disposal)
- ❌ Loading entire datasets into memory
- ❌ No caching for frequent data

### Operational Excellence Pillar
- ✅ Structured logging with correlation IDs
- ✅ Appropriate log levels (Debug/Info/Warning/Error)
- ✅ Metrics and telemetry
- ✅ Unit and integration tests (AAA pattern)
- ✅ Clear naming and documentation
- ✅ Low cyclomatic complexity (<10)
- ✅ Consistent code style
- ❌ No logging or minimal logging
- ❌ Catching exceptions without logging
- ❌ Magic numbers and hardcoded values
- ❌ Missing unit tests

### Performance Efficiency Pillar
- ✅ Stateless design (horizontal scaling)
- ✅ Async processing (non-blocking I/O)
- ✅ Query optimization (Include vs lazy loading)
- ✅ Efficient algorithms (avoid O(n²))
- ✅ Memory management (no leaks)
- ✅ Batch operations (avoid loops)
- ✅ Response compression
- ❌ Blocking calls (.Result, .Wait())
- ❌ N+1 query patterns
- ❌ Multiple LINQ enumerations
- ❌ String concatenation in loops

---

## 📈 Score Interpretation

### Pillar Scores (0-10)

| Score | Status | Interpretation | Action Required |
|-------|--------|----------------|-----------------|
| 9-10 | ✅ Excellent | Best practices consistently followed | Maintain standards |
| 7-8 | 🟢 Good | Solid implementation, minor gaps | Address P2 items in backlog |
| 5-6 | 🟡 Fair | Some significant gaps | Prioritize P1 items this sprint |
| 3-4 | 🟠 Needs Improvement | Many issues, risks present | Fix P0/P1 before production |
| 0-2 | 🔴 Critical | Critical gaps, not production-ready | Stop and remediate P0 |

### Overall Health Levels

**Excellent (All pillars 8+)**
- ✅ Production ready
- ✅ Maintenance mode
- Focus on continuous improvement

**Good (Most pillars 6+)**
- ✅ Production ready with monitoring
- Plan improvements for next sprint
- Low-medium risk

**Fair (Mixed scores 4-7)**
- ⚠️ Address critical issues before production
- Create remediation roadmap
- Medium risk

**Needs Improvement (Multiple pillars <6)**
- ⛔ Not production ready
- Requires significant work
- High risk

**Critical (Multiple pillars <4)**
- 🚨 Major architectural concerns
- Escalate for architecture review
- Very high risk

---

## 🔗 Integration with Development Workflow

### Sprint Planning
1. Run `/wa-review` at sprint start
2. Export P0/P1 findings
3. Add to sprint backlog with effort estimates
4. Track resolution in ADRs

### Pull Request Process
```markdown
## PR Checklist
- [ ] Code reviewed by peer
- [ ] Unit tests added/updated (100% coverage)
- [ ] `/wa-review` run - no P0 issues
- [ ] Security scan passed (`/wa-security`)
- [ ] Performance validated (`/wa-performance`)
- [ ] Documentation updated
```

### Production Deployment Gate
```
Gate: Well-Architected Review

Criteria:
- Overall score: Good or Excellent
- P0 issues: 0
- P1 issues: <5 with mitigation plan
- All pillars: ≥6/10
```

### Continuous Improvement
- **Weekly:** Run focused reviews during active development
- **Sprint End:** Full `/wa-review` before release
- **Monthly:** Track score trends, measure improvements
- **Quarterly:** Comprehensive assessment with stakeholders

---

## 🤝 Integration with Other Agents

The Well-Architected Agent works seamlessly with other project agents:

### Typical Workflow

```
1. Feature Planning
   └─ adr-analyzer: Review requirements in ADR

2. Implementation
   └─ coding-agent or fast-coding-agent: Write code

3. Quality Assurance
   └─ well-architected-agent: Review against WAF pillars

4. Remediation
   └─ coding-agent: Fix identified issues

5. Verification
   ├─ adr-analyzer: Verify ADR compliance
   └─ well-architected-agent: Re-score to confirm improvements
```

### Example Multi-Agent Session

```bash
# Step 1: Check ADR requirements
Use adr-analyzer to review ADR-2530 for Subscriptions feature

# Step 2: Implement feature
Use coding-agent to implement FHIR Subscriptions per ADR-2530

# Step 3: Well-Architected review
/wa-review on the Subscriptions implementation

# Step 4: Fix critical issues
Use coding-agent to remediate P0 security findings from WAF review

# Step 5: Verify compliance
Use adr-analyzer to verify final implementation matches ADR-2530
```

---

## 📚 Resources & Documentation

### Framework Documentation
- [Azure Well-Architected Framework](https://learn.microsoft.com/en-us/azure/well-architected/)
- [Architecture Center](https://learn.microsoft.com/en-us/azure/architecture/)
- [Well-Architected Assessment Tool](https://learn.microsoft.com/en-us/assessments/azure-architecture-review/)

### Project Documentation
- **CLAUDE.md** - Project coding standards and architecture rules
- **docs/adr/** - Architecture Decision Records
- **docs/investigations/** - Technical investigation documents

### Agent Files
- `.claude/agents/well-architected-agent.md` - Agent configuration
- `docs/guides/well-architected-agent-guide.md` - Comprehensive guide
- `docs/guides/wa-quick-reference.md` - Quick reference card

---

## 🎯 Best Practices

### 1. Review Early and Often
Don't wait until code review - run focused pillar reviews during development:
```bash
# While implementing authentication
/wa-security

# While optimizing database queries
/wa-performance

# While adding error handling
/wa-reliability
```

### 2. Provide Context
Help the agent understand your specific needs:
```
Good: "Review the Patient export feature focusing on PHI security and HIPAA compliance"
Better: "Review Patient export in src/Features/Patient/ExportPatientHandler.cs.
         This handles protected health information and must comply with HIPAA.
         Focus on encryption, authorization, and audit logging."
```

### 3. Act on Findings
- **P0 (Critical):** Block deployment until fixed
- **P1 (High):** Must fix before production
- **P2 (Medium):** Add to technical debt backlog

### 4. Track Improvements
```bash
# Initial review
/wa-review
# Score: Reliability 4/10, Security 5/10

# After remediation
/wa-review
# Score: Reliability 8/10, Security 9/10

# Document in ADR or commit message
```

### 5. Combine with Automated Tools
Well-Architected reviews complement but don't replace:
- Static analysis (Roslyn, SonarQube)
- Security scanning (OWASP ZAP, Snyk)
- Performance testing (load tests, profiling)
- Automated testing (unit, integration, E2E)

---

## 🔧 Customization

The agent is designed to work with this FHIR server project's specific architecture:

### Project-Specific Considerations
- **Layer Validation:** Checks dependency rules (API → Application → Domain → DataLayer)
- **Multi-Tenancy:** Validates partition isolation
- **FHIR Compliance:** Applies healthcare-specific security patterns
- **Technology Stack:** Understands .NET 9, EF Core, Autofac, Medino patterns

### Extending the Agent
To add custom checks:
1. Edit `.claude/agents/well-architected-agent.md`
2. Add project-specific patterns to checklists
3. Update examples in documentation

---

## 💡 Tips for Maximum Value

### Start Small
```bash
# Don't start with full codebase review
# Start with a single feature
Use well-architected-agent to review
src/Ignixa.Application/Features/Patient/SearchPatientHandler.cs
```

### Ask Follow-Up Questions
```bash
# After review identifies issues
"Show me how to implement the circuit breaker pattern
for the external FHIR validation service call at line 67"
```

### Request Code Examples
```bash
"For the N+1 query issue in PatientRepository.cs:42,
provide a before/after code example showing the fix
using EF Core Include()"
```

### Export for Team
```bash
"Format all P0 and P1 findings as GitHub issues with:
- Descriptive titles
- File locations
- Acceptance criteria
- Effort estimates
- Appropriate labels"
```

---

## ⚠️ Limitations

### What the Agent CAN Do:
✅ Static code analysis
✅ Pattern detection (anti-patterns, best practices)
✅ Architecture evaluation
✅ Framework principle application
✅ Prioritized recommendations
✅ Effort estimation (relative)

### What the Agent CANNOT Do:
❌ Execute code or run tests
❌ Access runtime metrics
❌ Perform penetration testing
❌ Guarantee 100% issue detection
❌ Replace human code review
❌ Provide absolute time estimates

### Best Combined With:
- Human code review and domain expertise
- Automated testing (unit, integration, E2E)
- Static analysis tools (SonarQube, Roslyn)
- Security scanning (SAST, DAST, dependency checks)
- Runtime monitoring (Application Insights, logs)

---

## 🆘 Troubleshooting

### Agent Not Finding Expected Issues
- Be more specific about scope and file locations
- Provide context about what you're concerned about
- Ask for specific pillar deep dive

### Too Many Findings
- Start with focused pillar reviews (`/wa-security`, `/wa-performance`)
- Review specific features/modules instead of entire codebase
- Filter by priority (ask for P0/P1 only)

### Need More Detail
- Ask for code examples: "Show before/after for the N+1 query fix"
- Request patterns: "Explain the retry pattern with Polly library"
- Dig deeper: "Analyze all database connection usage in the DataLayer"

---

## 📞 Support & Feedback

### Getting Help
- Check `WELL_ARCHITECTED_AGENT_GUIDE.md` for detailed examples
- Review `WA_QUICK_REFERENCE.md` for common patterns
- Examine example reviews in the guide

### Providing Feedback
Help improve the agent by sharing:
- **False Positives:** Issues flagged incorrectly
- **Missing Patterns:** Important checks not covered
- **Success Stories:** Improvements achieved using the agent
- **Suggestions:** Additional pillars or checks to add

---

## 📝 Version History

**Version 1.0** (December 2025)
- Initial release with all five WAF pillars
- Four slash commands (full review + 3 focused reviews)
- Comprehensive documentation and quick reference
- Integration with existing project agents
- FHIR-specific patterns and healthcare considerations

---

## 🚀 Getting Started Checklist

- [ ] Read this README
- [ ] Review `WA_QUICK_REFERENCE.md` for command reference
- [ ] Run `/wa-review` on a small feature to see output format
- [ ] Try focused pillar review (`/wa-security` or `/wa-performance`)
- [ ] Integrate into your pull request workflow
- [ ] Schedule regular reviews (weekly/monthly)

---

**Ready to elevate your code quality?** Start with `/wa-review` and begin your journey to architectural excellence!

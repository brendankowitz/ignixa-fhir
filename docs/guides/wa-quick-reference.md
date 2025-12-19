# Well-Architected Framework - Quick Reference

## 📋 Slash Commands

| Command | Focus | Use Case |
|---------|-------|----------|
| `/wa-review` | **All 5 Pillars** | Full assessment, pre-production checks |
| `/wa-security` | 🔒 **Security** | Security audits, compliance, auth/authz |
| `/wa-performance` | ⚡ **Performance** | Bottlenecks, optimization, slow queries |
| `/wa-reliability` | 🛡️ **Reliability** | Error handling, resilience, availability |

---

## 🏛️ Five Pillars

### 🛡️ Reliability
**Goal:** System meets uptime and recovery targets

**Key Checks:**
- ✅ Error handling (try-catch, graceful degradation)
- ✅ Retry logic + circuit breakers
- ✅ Timeouts on all async operations (CancellationToken)
- ✅ Health checks (/health, /ready endpoints)
- ✅ Idempotent operations

**Red Flags:**
- ❌ Empty catch blocks
- ❌ No retry logic for external calls
- ❌ Missing timeouts
- ❌ Cascading failures

---

### 🔒 Security
**Goal:** Protect confidentiality, integrity, availability

**Key Checks:**
- ✅ Authentication & authorization on all endpoints
- ✅ Encryption (TLS, data at rest)
- ✅ Input validation (SQL injection, XSS prevention)
- ✅ Secrets in Key Vault (no hardcoded credentials)
- ✅ Security headers (CORS, CSRF)

**Red Flags:**
- ❌ Hardcoded passwords/API keys
- ❌ SQL string concatenation
- ❌ Missing authorization checks
- ❌ Logging sensitive data
- ❌ Weak crypto (MD5, SHA1)

---

### 💰 Cost Optimization
**Goal:** Maximize efficiency, minimize waste

**Key Checks:**
- ✅ Connection pooling (DB, HTTP)
- ✅ Proper disposal (using statements, IDisposable)
- ✅ Caching (reduce redundant fetches)
- ✅ Pagination (large datasets)
- ✅ Async/await (non-blocking I/O)

**Red Flags:**
- ❌ New HttpClient per request
- ❌ N+1 query patterns
- ❌ Loading entire datasets into memory
- ❌ No caching
- ❌ Memory leaks (missing disposal)

---

### ⚙️ Operational Excellence
**Goal:** Reduce production issues through DevOps

**Key Checks:**
- ✅ Structured logging (correlation IDs)
- ✅ Metrics & telemetry
- ✅ Unit + integration tests
- ✅ Clear naming & documentation
- ✅ Low complexity (cyclomatic < 10)

**Red Flags:**
- ❌ No logging or catching without logging
- ❌ Magic numbers/hardcoded values
- ❌ High complexity (>10)
- ❌ No tests for business logic
- ❌ Inconsistent code style

---

### ⚡ Performance Efficiency
**Goal:** Scale and respond efficiently

**Key Checks:**
- ✅ Horizontal scalability (stateless)
- ✅ Async operations (non-blocking)
- ✅ Query optimization (no SELECT *, use Include)
- ✅ Efficient algorithms (avoid O(n²))
- ✅ Memory management (no leaks, GC efficient)

**Red Flags:**
- ❌ Blocking calls (.Result, .Wait())
- ❌ N+1 query patterns
- ❌ Multiple LINQ enumerations
- ❌ No pagination
- ❌ String concatenation in loops

---

## 🎯 Priority Levels

| Icon | Priority | Action | Timeline |
|------|----------|--------|----------|
| 🚨 | **P0 Critical** | Fix immediately | Before production |
| ⚠️ | **P1 High** | Address soon | This sprint/release |
| ℹ️ | **P2 Medium** | Plan for later | Next quarter |

---

## 📊 Score Interpretation

| Score | Status | Meaning | Action |
|-------|--------|---------|--------|
| 9-10 | ✅ Excellent | Best practices | Maintain |
| 7-8 | 🟢 Good | Solid, minor issues | Address P2 |
| 5-6 | 🟡 Fair | Some gaps | Fix P1 items |
| 3-4 | 🟠 Needs Work | Significant issues | Fix P0/P1 now |
| 0-2 | 🔴 Critical | Major risks | Stop & remediate |

---

## 🔍 Common Patterns to Check

### Security Scan
```bash
# Find hardcoded secrets
Grep: pattern="password|apikey|secret|connectionstring" -i=true

# Check SQL injection risk
Grep: pattern="FromSqlRaw|ExecuteSqlRaw" output_mode="content"

# Verify authorization
Grep: pattern="Authorize|AllowAnonymous" output_mode="content"
```

### Performance Scan
```bash
# Find blocking calls
Grep: pattern="\.Result|\.Wait\(\)" output_mode="content"

# Check for N+1 queries
Grep: pattern="await.*\.Where\(|\.ToListAsync\(\)" output_mode="content"

# Find eager loading
Grep: pattern="Include\(|ThenInclude\(" output_mode="content"
```

### Reliability Scan
```bash
# Check error handling
Grep: pattern="catch.*Exception" output_mode="content" -C=3

# Find retry logic
Grep: pattern="Polly|RetryAsync|CircuitBreaker" output_mode="content"

# Check timeouts
Grep: pattern="CancellationToken|timeout" -i=true output_mode="content"
```

---

## 💡 Quick Tips

### Before Committing
```bash
/wa-security  # If touching auth/authz
/wa-performance  # If changing queries/APIs
```

### Before PR Review
```bash
/wa-review  # Full check for feature work
```

### Before Production
```bash
# Full readiness check
Use well-architected-agent to assess production readiness:
- Reliability: Health checks, error handling, monitoring
- Security: Secrets externalized, auth/authz complete
- Operational Excellence: Logging, metrics, runbooks
```

### After Incident
```bash
/wa-reliability  # Post-mortem analysis
```

---

## 🎓 Example Usage

### Simple Feature Review
```
Use well-architected-agent to review src/Ignixa.Application/Features/Patient/SearchPatientHandler.cs
focusing on Performance and Reliability pillars.
```

### Security Audit
```
/wa-security

Focus on PHI data handling in the Patient export feature.
Verify encryption, authorization, and audit logging.
```

### Performance Investigation
```
/wa-performance

Investigate slow response times in Patient search.
Check for N+1 queries, missing indexes, and caching opportunities.
```

### Production Readiness
```
/wa-review

Assess FHIR Subscriptions feature for production deployment.
Focus on Reliability and Operational Excellence.
```

---

## 📚 Resources

- **Full Guide:** `docs/guides/well-architected-agent-guide.md`
- **Agent Config:** `.claude/agents/well-architected-agent.md`
- **Azure WAF Docs:** https://learn.microsoft.com/en-us/azure/well-architected/
- **Project Standards:** `CLAUDE.md`

---

## 🤝 Integration with Other Agents

| Workflow | Agent Sequence |
|----------|----------------|
| **Feature Development** | `adr-analyzer` → `coding-agent` → `well-architected-agent` |
| **Security Compliance** | `well-architected-agent` (security) → `coding-agent` (fixes) |
| **Performance Optimization** | `well-architected-agent` (performance) → `coding-agent` (optimizations) |
| **Production Readiness** | `adr-analyzer` → `well-architected-agent` → approval |

---

**Pro Tip:** Run `/wa-review` weekly during active development to catch issues early!

---

**Version:** 1.0
**Updated:** December 2025

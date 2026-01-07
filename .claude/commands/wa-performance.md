# Well-Architected Performance Review

Conduct a focused performance analysis based on the Well-Architected Framework Performance Efficiency pillar:

**Performance Pillar Focus Areas:**
- Scalability (horizontal scaling, async operations)
- Data Access (N+1 queries, connection pooling, indexing)
- Caching Strategy (memory/distributed caching, invalidation)
- Algorithm Efficiency (time/space complexity, Query optimization)
- Network & I/O (batching, compression, streaming)
- Resource Management (memory leaks, GC pressure, thread pool)

Analyze the codebase for:
- 🚨 Critical performance issues (blocking calls, N+1 queries)
- ⚠️ Performance bottlenecks (inefficient algorithms, missing caching)
- ✅ Performance best practices observed

Provide specific file references with optimization recommendations and effort estimates.

Use the well-architected-agent with performance pillar deep dive scope.

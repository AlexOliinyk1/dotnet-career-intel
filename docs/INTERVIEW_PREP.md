# .NET Architect Interview Preparation System
## What You Need to PASS — Not Just Survive

---

## INTERVIEW STRUCTURE (What to Expect)

Most high-paying .NET architect interviews follow this pattern:

```
Round 1: Recruiter Screen (30 min) — Salary, availability, basics
Round 2: Technical Screen (60 min) — C#/.NET deep-dive, coding
Round 3: System Design (60-90 min) — THE MOST IMPORTANT ROUND
Round 4: Architecture Discussion (45-60 min) — Real-world trade-offs
Round 5: Behavioral / Culture (45 min) — Leadership, communication
Round 6: Final / Bar Raiser (30-60 min) — Senior leadership alignment
```

**For US $150K+ roles, expect 4-6 rounds.**
**For EU €80K+ roles, expect 3-4 rounds.**
**For platform assessments (Toptal/Turing), expect 2-3 structured rounds.**

---

## ROUND-BY-ROUND PREPARATION

### ROUND 1: Recruiter Screen

**What they actually evaluate:**
- Can you communicate clearly in English?
- Are your salary expectations in range?
- Are you legally able to work (remote/B2B)?
- Are you a flight risk or genuinely interested?

**Preparation:**
- Practice your 2-minute pitch: "I'm a .NET architect with X years of experience. I've designed and led [specific systems]. I'm looking for [specific role type] where I can [specific impact]."
- Have your salary range ready: "Based on my experience and market data, I'm targeting $X-Y for this type of role."
- Know the company: Read their engineering blog, check their tech stack on StackShare/BuiltWith.

**Red flags to avoid:**
- Badmouthing current/past employers
- Being vague about what you want
- Asking about salary before they do (let them bring it up)

---

### ROUND 2: Technical Deep-Dive

#### C# Mastery Questions (Expect 15-20 min)

**Must know cold:**
```
Q: Explain the difference between ValueTask and Task. When would you use each?
A: ValueTask avoids heap allocation when the result is already available
   synchronously. Use for hot paths that frequently complete synchronously.
   Caveat: cannot be awaited multiple times, cannot use .Result safely.

Q: How does the async state machine work internally?
A: Compiler generates a struct implementing IAsyncStateMachine.
   Each await point is a state. MoveNext() advances through states.
   First await that doesn't complete synchronously boxes the struct to heap.

Q: Explain Span<T> and Memory<T>. When is each appropriate?
A: Span<T> is stack-only (ref struct), provides safe pointer-like access to
   contiguous memory. Cannot be stored in heap objects.
   Memory<T> can be stored on heap, used with async methods.
   Use Span<T> for synchronous hot-path parsing/processing.

Q: What are source generators? How do they differ from reflection?
A: Compile-time code generation. No runtime overhead unlike reflection.
   Uses Roslyn analyzer infrastructure. Examples: System.Text.Json,
   logging, regex. Write ISourceGenerator/IIncrementalGenerator.

Q: Records vs classes vs structs — when to use each?
A: Records: immutable data with value semantics (DTOs, events, value objects).
   Classes: mutable state, identity-based, complex behavior.
   Structs: small (< 16 bytes), short-lived, no allocation overhead.
   Record structs: value semantics + no heap allocation.
```

**Also prepare:**
- Nullable reference types: how they work, adoption strategy
- Pattern matching: switch expressions, property patterns, list patterns
- Generic math interfaces (INumber<T>)
- Channels (System.Threading.Channels) for producer-consumer
- IAsyncEnumerable for streaming scenarios

#### ASP.NET Core Architecture (15-20 min)

**Must know:**
```
Q: Explain the middleware pipeline. How does it differ from filters?
A: Middleware: request-level, runs for every request, order matters.
   Filters: action-level, only for MVC/API controllers.
   Pipeline: Middleware → Routing → Endpoint → Filters → Action.
   Custom middleware for cross-cutting (logging, auth, error handling).

Q: How do you handle API versioning in a large .NET application?
A: Options: URL path (/v1/), query string (?v=1), header (X-API-Version).
   Use Asp.Versioning.Http package. Sunset deprecated versions.
   For microservices: consumer-driven contract testing.

Q: Explain the built-in DI container lifetime scopes.
A: Transient: new instance every injection.
   Scoped: shared per request (EF DbContext, UnitOfWork).
   Singleton: shared for app lifetime (HttpClient factory, caches).
   Captive dependency problem: singleton consuming scoped = bug.

Q: How do you implement rate limiting in ASP.NET Core?
A: Built-in: app.UseRateLimiter() with fixed window, sliding window,
   token bucket, concurrency policies. Can combine.
   Distributed: Redis-based rate limiting for multi-instance.
   Per-client, per-endpoint, or global policies.
```

#### Entity Framework Core (10 min)

```
Q: How do you handle N+1 query problems?
A: Eager loading (.Include()), split queries (.AsSplitQuery()),
   explicit loading for conditional relationships.
   Monitor with .ToQueryString() and SQL profiling.

Q: Database migration strategy in production?
A: Idempotent migrations, backward-compatible changes only.
   Never: drop columns in same release as code removal.
   Pattern: add new → migrate data → remove old (2 releases).
   Rollback strategy for each migration.
```

---

### ROUND 3: SYSTEM DESIGN (The $150K+ Gate)

**This round makes or breaks architect-level offers.**

#### Framework for Every System Design Question:

```
Step 1: REQUIREMENTS (5 min)
  - Functional: What does the system do?
  - Non-functional: Scale, latency, availability, consistency
  - Constraints: Budget, team, timeline, regulatory

Step 2: ESTIMATION (5 min)
  - Users: DAU/MAU
  - Throughput: requests/sec, writes/sec
  - Storage: data growth rate
  - Bandwidth: data transfer needs

Step 3: HIGH-LEVEL DESIGN (15 min)
  - Core components and their responsibilities
  - Data flow between components
  - API contracts
  - Database choices with justification

Step 4: DEEP DIVE (15 min)
  - Pick 2-3 most interesting/challenging components
  - Dive into implementation details
  - Discuss patterns (CQRS, Saga, Outbox, etc.)

Step 5: SCALE & TRADE-OFFS (10 min)
  - How it handles 10x load
  - Single points of failure
  - Consistency vs availability decisions
  - Cost optimization
```

#### The 5 System Design Questions You WILL Be Asked:

**1. Design an E-Commerce Platform**
```
Key decisions:
- Order service with Saga pattern for distributed transactions
- Product catalog with read replicas + Elasticsearch
- Payment processing: idempotency keys, retry with backoff
- Cart: Redis for session, event-driven inventory reservation
- CQRS: separate read/write models for product catalog
Technologies: .NET, Azure Service Bus, Redis, SQL Server + Elasticsearch
```

**2. Design a Real-Time Notification System**
```
Key decisions:
- SignalR hub for WebSocket connections
- Azure Service Bus for reliable message delivery
- Fan-out: user preference-based routing
- Delivery guarantees: at-least-once with idempotent consumers
- Storage: hot (Redis) for recent, cold (Cosmos/Table Storage) for history
- Scale: sticky sessions or Redis backplane for SignalR
Technologies: .NET, SignalR, Azure Service Bus, Redis, CosmosDB
```

**3. Design a Multi-Tenant SaaS Platform**
```
Key decisions:
- Tenant isolation: database-per-tenant vs schema vs row-level
- Authentication: Azure AD B2C, tenant-aware JWT
- Configuration: per-tenant feature flags, limits
- Data partitioning strategy
- Billing: usage metering, Stripe integration
- Deployment: single deployment, tenant routing middleware
Technologies: .NET, Azure AD B2C, EF Core, SQL Server with RLS
```

**4. Design a Distributed Task Processing System**
```
Key decisions:
- Queue-based load leveling (Azure Service Bus / RabbitMQ)
- Worker scaling (K8s HPA based on queue depth)
- Exactly-once processing (outbox pattern + idempotency)
- Dead letter handling and retry policies
- Progress tracking: SignalR for real-time updates
- Monitoring: OpenTelemetry distributed tracing
Technologies: .NET, Kubernetes, RabbitMQ/Kafka, PostgreSQL, Redis
```

**5. Design a Migration from Monolith to Microservices**
```
Key decisions:
- Strangler Fig pattern: incremental migration
- Domain boundary identification (DDD bounded contexts)
- API Gateway: routing between old and new
- Data synchronization during transition (CDC / dual-write)
- Testing: consumer-driven contracts
- Rollback strategy for each extracted service
Technologies: .NET, YARP (reverse proxy), MassTransit, Docker, K8s
```

---

### ROUND 4: Architecture Discussion

**Common questions and strong answers:**

```
Q: How do you decide between microservices and a monolith?
Strong answer: "Start monolith, extract when you have clear bounded contexts
and team scaling requires it. Microservices have a tax: networking,
observability, data consistency. The question isn't 'should we use
microservices' but 'which boundaries justify the operational cost.'
In my last project, we extracted 3 of 12 potential services because
only those 3 had different scaling characteristics."

Q: How do you handle distributed transactions?
Strong answer: "Avoid them. Use Saga pattern with compensation.
Orchestration-based for complex workflows (MassTransit state machines),
choreography for simple ones (events via Service Bus).
For exactly-once semantics: transactional outbox pattern.
Accept eventual consistency and design the UX around it."

Q: How do you approach observability in a distributed system?
Strong answer: "Three pillars: logs, metrics, traces. OpenTelemetry for
vendor-neutral instrumentation. Structured logging with Serilog +
correlation IDs propagated via Activity. Metrics: request rate,
error rate, duration (RED method). Distributed tracing: every
service boundary creates a span. Alerting on SLOs, not thresholds."

Q: How do you manage technical debt?
Strong answer: "I categorize debt by risk and cost. Every sprint has
20% capacity for debt reduction. I use Architecture Decision Records
(ADRs) to document why we took on debt and when we'll pay it.
I don't ask permission to refactor — I include it in feature estimates.
The real question is: which debt will slow us down next quarter?"
```

---

### ROUND 5: Behavioral / Leadership

**The STAR Framework (Situation → Task → Action → Result):**

#### Prepare 6 Stories:

**Story 1: Technical Decision**
"Tell me about a critical architecture decision you made."
→ Prepare: What was the context, what options existed, how you evaluated them, what was the outcome with metrics.

**Story 2: Conflict Resolution**
"Describe a disagreement with a team member about technical approach."
→ Prepare: How you listened, how you found common ground, how you made the final decision, how the other person responded.

**Story 3: Failure/Learning**
"Tell me about a production incident or project failure."
→ Prepare: What happened, your role, how you responded, what you changed permanently. Show ownership, not blame.

**Story 4: Mentoring**
"How have you helped junior developers grow?"
→ Prepare: Specific example, what you taught, how they improved, measurable outcome.

**Story 5: Cross-Team Influence**
"How did you drive alignment across multiple teams?"
→ Prepare: RFCs, ADRs, tech talks, guild leadership, standards you established.

**Story 6: Business Impact**
"Describe a technical decision that directly impacted business metrics."
→ Prepare: Reduced costs by X%, improved performance by Y%, enabled Z new capability.

---

## PLATFORM-SPECIFIC PREPARATION

### Toptal Assessment
```
Round 1: English + Personality (30 min)
  - Casual conversation, clear communication check
  - Why freelance? Why Toptal?

Round 2: Technical Screen (60-90 min)
  - Live coding in C#
  - Algorithm problem (medium difficulty)
  - System design mini-question

Round 3: Test Project (1-2 weeks)
  - Build a small application
  - Reviewed for code quality, architecture, testing
  - Must demonstrate architect-level decisions
```

### Turing Assessment
```
Round 1: Automated coding test (60-90 min)
  - Multiple choice + coding challenges
  - .NET specific questions

Round 2: Technical interview (45-60 min)
  - Live discussion about architecture
  - System design question

Round 3: Soft skills / communication
  - English proficiency
  - Collaboration style
```

---

## DAILY PRACTICE SCHEDULE (4-Week Sprint)

### Week 1-2: Foundation
| Day | Focus (2 hrs) |
|-----|---------------|
| Mon | C# deep-dive: async internals, memory model, new features |
| Tue | System design: practice #1 (e-commerce) |
| Wed | Azure services deep-dive: AKS, Service Bus, Functions |
| Thu | System design: practice #2 (notification system) |
| Fri | Mock interview: technical screen (use Pramp/interviewing.io) |
| Sat | Terraform + IaC hands-on lab |
| Sun | Review + fill gaps identified during week |

### Week 3-4: Advanced
| Day | Focus (2 hrs) |
|-----|---------------|
| Mon | System design: practice #3 (multi-tenant SaaS) |
| Tue | CQRS + Event Sourcing: build mini-project |
| Wed | System design: practice #4 (task processing) |
| Thu | Behavioral stories: write and practice aloud |
| Fri | Full mock interview: all rounds |
| Sat | AI/ML integration: Semantic Kernel hands-on |
| Sun | Review + final preparation for specific vacancy |

---

## QUICK REFERENCE: Common Gotchas

| Topic | What They Expect | Common Mistake |
|-------|------------------|----------------|
| Microservices | Nuanced trade-off discussion | "Always use microservices" |
| Database choice | Justified selection with alternatives | Default to SQL Server always |
| Caching | Strategy (what, where, invalidation) | "Just add Redis" |
| Authentication | OAuth2/OIDC flow understanding | Vague about token lifecycle |
| Error handling | Resilience patterns, circuit breakers | Try-catch everywhere |
| Testing | Test pyramid, what to test at each level | "100% code coverage" |
| CI/CD | Full pipeline including rollback | Only the happy path |
| Monitoring | SLOs, alerting strategy, runbooks | "We use Application Insights" |
| Scale | Specific numbers and bottleneck analysis | "It scales horizontally" |
| Security | OWASP Top 10, threat modeling | Surface-level "we use HTTPS" |

---

*This preparation system is based on analysis of 50+ .NET architect interview question banks, real job listings from Djinni, DOU, LinkedIn, and Glassdoor interview reports (Feb 2026).*

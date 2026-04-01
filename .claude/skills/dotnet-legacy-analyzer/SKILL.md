---
name: dotnet-legacy-analyzer
description: >
  Analyze legacy .NET/C# microservice projects for bugs, potential bugs, memory leaks, security
  vulnerabilities, performance issues, and refactoring opportunities. Use this skill whenever the
  user asks to review, audit, analyze, or assess a .NET or C# codebase — especially legacy code.
  Triggers include phrases like "analyze this code", "review my .NET project", "find bugs",
  "check for memory leaks", "security audit", "code quality review", "refactor suggestions",
  "legacy code assessment", "technical debt analysis", or any request involving .NET/C# code
  review and improvement. Also trigger when the user uploads .cs, .csproj, .sln files and asks
  for feedback, or when they paste C# code and ask "what's wrong with this" or "how can I
  improve this". Even partial reviews like "check this service" or "is this safe" on C# code
  should trigger this skill.
---

# .NET Legacy Microservice Analyzer

Systematic analysis of legacy .NET/C# microservice codebases to identify bugs, security
vulnerabilities, performance bottlenecks, and improvement opportunities. The analysis follows
a phased approach — from critical issues requiring immediate attention to infrastructure and
maintainability improvements.

## Core Rules

These rules apply throughout the entire analysis, regardless of phase:

1. **Skip commented-out code entirely.** Do not analyze, report on, or mention commented-out
   code blocks. They are dead code — treat them as invisible.

2. **Suggest, don't implement.** Every finding should include a concise explanation of the
   recommended approach and a short illustrative example (5-10 lines max), but never a full
   implementation. The developer should understand *what* to do and *why*, not receive
   copy-paste code.

3. **Generate an MD report at the end.** After completing all applicable phases, compile the
   full analysis into a structured Markdown file and save it. Details in the Output Format section.

## Phase Structure

The analysis has 4 phases, each progressively deeper. Read the corresponding reference file
before starting each phase:

| Phase | Focus | Reference | When to Run |
|-------|-------|-----------|-------------|
| 1 | Critical Issues | `references/phase1-critical.md` | Always — run first |
| 2 | Deep Analysis | `references/phase2-deep-analysis.md` | Always for thorough reviews |
| 3 | Optimization & Refactoring | `references/phase3-optimization.md` | When requested or full audits |
| 4 | Microservice & Infrastructure | `references/phase4-microservice-infra.md` | For full project audits |

The phased approach exists because not all issues have equal urgency. A SQL injection matters
more than a naming convention. Separating phases lets the developer prioritize effectively.

## Analysis Workflow

### Step 0: Scope Assessment

Before analyzing, understand what you're working with:

1. **Determine scope** — Full project/solution, single file, or code snippet?
2. **Identify framework** — .NET Framework (4.x), .NET Core, .NET 5+?
3. **Identify project type** — Web API, Worker Service, Console, background processor?
4. **Check for ORM** — Entity Framework (version?), Dapper, ADO.NET raw, NHibernate?
5. **Check for messaging** — Kafka, RabbitMQ, MassTransit? (determines Phase 4 scope)
6. **Note the domain** — Banking, document processing, integration service, etc.?

Communicate the scope back to the user in 2-3 sentences before starting analysis.

### Step 1: Run Phase 1 (Critical)

Read `references/phase1-critical.md`. Scan for security vulnerabilities, memory leaks, resource
leaks, data corruption risks, and critical async anti-patterns. These are "stop and fix now" issues.

### Step 2: Run Phase 2 (Deep Analysis)

Read `references/phase2-deep-analysis.md`. Analyze bug patterns, async/await issues, thread
safety, exception handling, and database access patterns.

### Step 3: Run Phase 3 (Optimization) — if applicable

Read `references/phase3-optimization.md`. Evaluate performance, code quality, SOLID adherence,
refactoring opportunities, and modernization potential.

### Step 4: Run Phase 4 (Microservice & Infrastructure) — if applicable

Read `references/phase4-microservice-infra.md`. Assess Kafka communication patterns, external
HTTP client usage (only if detected), API contracts, resiliency, logging, and configuration.

### Step 5: Generate Report

Compile all findings into a Markdown file. Follow the output format below.

## Output Format

### Finding Template

For each issue found, use this structure:

```
### [SEVERITY] Short Description
**Location:** FileName.cs → MethodName (or ClassName)
**Category:** Security | Memory Leak | Bug | Performance | Code Quality | Async | Thread Safety | ...
**Risk:** One sentence — what can go wrong if this isn't fixed
**Problem:** Brief description of what the code does wrong (with short snippet if helpful)
**Suggested Approach:** How to fix it — describe the approach and show a minimal example (5-10 lines)
**Effort:** Low | Medium | High
```

### Severity Levels

- 🔴 **CRITICAL** — Security vulnerabilities, data corruption risks, production memory leaks
- 🟠 **HIGH** — Bugs likely to manifest, resource leaks, async deadlock potential
- 🟡 **MEDIUM** — Code quality issues, edge-case bugs, performance problems
- 🔵 **LOW** — Style improvements, modernization suggestions, minor optimizations

### MD Report Structure

The final report saved as a Markdown file should follow this structure:

```markdown
# .NET Legacy Code Analysis Report
**Project:** [name or description]
**Framework:** [detected framework]
**Date:** [analysis date]
**Scope:** [what was analyzed]

## Executive Summary
- **Overall Health Score:** X/10
- **Findings:** N critical, N high, N medium, N low
- **Top 3 Priorities:** [the three most impactful things to fix]
- **Quick Wins:** [low-effort, high-impact improvements]
- **Technical Debt Level:** Low | Moderate | Significant | Severe

## Table of Contents
[auto-generated based on phases run]

## Phase 1: Critical Issues
[findings ordered by severity]

## Phase 2: Deep Analysis
[findings ordered by severity]

## Phase 3: Optimization & Refactoring
[findings if this phase was run]

## Phase 4: Microservice & Infrastructure
[findings if this phase was run]

## Summary Statistics
[breakdown table of findings by category and severity]
```

Save the report to the working directory as `analysis-report.md` (or with the project name
if known, e.g., `analysis-report-order-service.md`).

## Important Guidelines

- When analyzing partial code (single file or snippet), state what you CAN and CANNOT assess.
  Example: "Thread safety can't be fully verified without seeing how this class is consumed."
- Respect the project's existing framework version. Don't suggest C# 12 features for a .NET
  Framework 4.6 project. Only suggest upgrades where they provide clear value and the framework
  supports them.
- For banking or regulated domain code, emphasize compliance-relevant issues — data protection,
  audit trails, transaction integrity.
- Focus on impact over volume. 5 critical findings are worth more than 50 nitpicks. For large
  files, prioritize the most dangerous and impactful issues.
- If uncertain about intent, note it as a question rather than a definitive finding.

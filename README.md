# Northwind Support Desk

A support ticket management system: raise tickets, search and filter them, assign them to agents,
and move them through their lifecycle.

**.NET 10 Web API** + **React 19 / TypeScript** + **SQL Server**, in one monorepo.

---

## The assignment

You are joining a team that already has this app working. Complete the tasks **in order**.
Full requirements are in **[`ASSIGNMENT_CANDIDATE.md`](ASSIGNMENT_CANDIDATE.md)**.

| # | Task | Done looks like | Effort |
| --- | --- | --- | --- |
| 1 | **Fix a defect, add server-side filtering** | Root cause fixed with a regression test; every list filter applied in the database with a correct count | ~2.5 h |
| 2 | **Ticket escalation & assignment** | Automatic triage on create, escalation with history, SLA status, UI for all of it | ~7 h |
| 3 | **Login and authentication** | Agents sign in; API rejects unauthenticated calls; login UI | ~4 h |
| 4 | **Dockerise the API** *(optional)* | `docker compose up` gives a migrated, seeded API with no baked-in secret | ~1.5 h |

### Guidance

- **Time: 2 days.** Honestly marking a task "not finished" beats rushing all of them.
- **Fork** this repo to your own GitHub account and work there. **Do not push to the original.**
- **AI tools are allowed.** You must understand and be able to defend every line you submit.
- **Commit as you go** and keep the real history. Commit the Task 1 defect fix and the filtering
  separately from each other and from the feature work. A single squashed commit is not accepted.
- **Where the spec is ambiguous**, make a decision, write it down in the README, and move on.
- **Out of scope:** registration, password reset, roles, OAuth/SSO, refresh tokens, MFA,
  notifications, CI, caching, visual redesign. Mention them under *Known limitations* instead.

### Submission

1. Update this **README** with what you built, your design decisions (chosen, rejected and why),
   the defect's root cause, dev login credentials, how to run and test it, AI usage, and known
   limitations. A reviewer must be able to run it without asking you anything.
2. Add an **APPROACH.md** explaining your approach and thought process for each task.
3. Commit both, then send us the **link to your fork**, the **dev login credentials**, and a
   **rough note of time spent** on what. (If the fork is private, add the reviewers as collaborators.)

---

## Quick start

**Prerequisites:** .NET SDK 10 (`global.json`), Node.js 22 (`.nvmrc`; 20.19+ works), Docker.

```bash
cp .env.example .env          # safe placeholders; configure JWT as described below
npm install                   # installs the web app
npm run db:up                 # SQL Server 2022 in Docker on localhost:1433
npm run api                   # terminal 1: migrates, seeds, then serves the API
npm run web                   # terminal 2: React dev server
```

| What | Where |
| --- | --- |
| Web app | http://localhost:5173 |
| API | https://localhost:7043 (also http://localhost:5043) |
| Swagger | https://localhost:7043/swagger |

In Development the API applies migrations and seeds demo data on startup. **Never commit real
credentials.**

**Visual Studio 2026:** run `npm run db:up`, open `SupportDesk.sln`, set
**SupportDesk.Presentation** as the startup project, and press F5 on the **https** profile.

## Tests

```bash
npm test                      # dotnet test, then the web tests (Vitest)
```

Both pass on a clean checkout without an external database (the backend uses disposable SQLite databases).

## Task 3 Authentication

### Configuration and Development Login

Before starting the API or using EF CLI, configure JWT in that terminal. The API
does not automatically load the root `.env`. PowerShell example:

```powershell
$env:Jwt__SigningKey = node -e "process.stdout.write(require('node:crypto').randomBytes(48).toString('base64'))"
$env:Jwt__Issuer = 'NorthwindSupport'
$env:Jwt__Audience = 'NorthwindWeb'
$env:Jwt__LifetimeMinutes = '60'
npm run api
```

In Bash use `export Jwt__SigningKey="$(node -e "process.stdout.write(require('node:crypto').randomBytes(48).toString('base64'))")`
and export the other three variables above. Keep a stable random key securely outside
source control if sessions must survive API restarts. Missing or invalid settings
fail startup clearly. JWT keys require at least 32 UTF-8 bytes; issuer and audience
are mandatory. Lifetime defaults to 60 minutes, configurable from 1 to 1440.

**Development-only credentials, never for production:**

- Email: `alex.turner@northwind-support.example`
- Password: `Northwind-Dev-Only!2026`
- The other seeded demo agents use the same fixture password. Inactive Yuki cannot log in.

Development startup seeds missing hashes for only the six known demo addresses.
It never overwrites existing credentials. Production startup does not seed them.

### Design and API

Credentials use nullable `Agent.PasswordHash`, avoiding duplicate user identities
or a full Identity schema for this agent-only system. Migration
`20261011143214_AddAgentCredentials` adds only an optional `nvarchar(512)` column.
Existing agents, tickets and history are preserved. Only salted, versioned PBKDF2
hashes produced by ASP.NET Core Identity `PasswordHasher<Agent>` are stored.
Successful verification can upgrade an older hash. Domain remains independent of
Identity; the Application authentication interface is implemented in Infrastructure.
Presentation owns HTTP, JWT issuance and bearer authentication.

`POST /api/auth/login` accepts email/password and returns `accessToken`,
`expiresAtUtc` and `agent` (`id`, `fullName`, `email`). Incorrect password, unknown
email and inactive agents receive the same generic 401. No hashes are returned.
Unknown/uncredentialed identities still perform dummy-hash verification to reduce
the obvious password-hashing timing difference.

`GET /api/auth/me` returns the current authenticated active agent. HS256 JWTs have
subject = AgentId, issuer, audience, unique token ID and expiry. Validation checks
algorithm/signature, issuer, audience and lifetime with zero expiry grace. The
agent must still exist and be active on each authenticated request.

Fallback authorization protects existing controllers by default. Login and
Development Swagger are public. `/health` is explicitly anonymous in preparation
for deployment checks; no Task 4 container work was added. Missing, expired or
invalid tokens receive 401.

Escalation accepts only a reason. Caller-supplied actor JSON cannot determine
identity. The audit actor is `agent:<id> <current database name>`, truncated to
100 characters while retaining the stable ID prefix. Historical audit rows remain
unchanged; all existing escalation business rules are preserved.

### React Session and Security Trade-offs

React has a typed session/provider, accessible login form and protected routes.
It returns to the requested page, displays the agent name and supports logout.
The API client attaches bearer tokens. Restored sessions are checked using `/auth/me`;
expiry and protected-request 401 clear authentication. Stale responses cannot clear
a newer session or revive one after logout. Escalation no longer requests an actor.

Tokens use **sessionStorage**, scoped to the browser tab and surviving refresh.
Passwords are never persisted. This is readable by JavaScript and vulnerable to
XSS, unlike HttpOnly cookies. Production should use HTTPS, CSP/XSS defenses and
consider a same-site HttpOnly-cookie/BFF design with CSRF protection.

No registration, recovery, OAuth, refresh tokens, MFA, roles, rate limiting or
lockout is included. Production needs brute-force protection, secure provisioning
and signing-key rotation. Logout clears only the local session: a copied JWT
remains valid until expiry or agent deactivation. Password changes do not revoke
tokens through a security stamp. Tabs do not share logout. Existing assignment,
reference and escalation concurrency limitations still apply.

### Authentication Tests

Authentication tests are included in `dotnet test` and the frontend suite.
HTTP cases run real Kestrel servers against SQLite by default. Repeat with SQL Server:

```powershell
$env:SUPPORTDESK_TEST_SQLSERVER = 'Server=localhost\SQLEXPRESS;Integrated Security=True;TrustServerCertificate=True;Encrypt=False'
dotnet test apps/api/tests/SupportDesk.UnitTests/SupportDesk.UnitTests.csproj -c Release --filter 'FullyQualifiedName~TicketCreationTests|FullyQualifiedName~TicketEscalationApiTests'
```

Fixtures generate test-only JWT keys and migrate/drop only their GUID-suffixed
databases. Existing HTTP tests now send signed bearer tokens and use the same
production authorization policy. Frontend tests are jsdom/mocked HTTP. See
`TASK3_VERIFICATION.md` for actual SQL/HTTP results and outstanding browser checks.

Ticket filtering tests run the real EF Core `TicketQueries` implementation against a fresh
in-memory SQLite relational database per test. They cover exact filters, search, combined
filters, filtered counts, pagination and stable sorting. These automated tests are separate
from the SQL Server Express smoke checks; SQLite does not prove SQL Server collation or all
provider-specific behavior.

## Commands

| Command | What it does |
| --- | --- |
| `npm run db:up` / `db:down` / `db:reset` | Start / stop / wipe SQL Server |
| `npm run api` / `npm run web` | Run the API / the React app |
| `npm run build` | Build both |
| `npm run lint` / `typecheck` / `format` | Web lint and types / `dotnet format` |
| `npm run ef -- migrations add <Name>` | Add an EF Core migration |

## API at a glance

| Endpoint | Description |
| --- | --- |
| `GET /api/tickets` | Paged list with `search`, `status`, `priority`, `categoryId`, `customerId`, `assignedAgentId`, `unassignedOnly`, `sortBy`, `sortDirection`. Filters combine with AND and are applied in the database before counting and paging. Search matches title, reference or customer name. |
| `GET /api/tickets/{id}` | One ticket |
| `POST /api/tickets` | Create a ticket |
| `POST /api/tickets/{id}/escalate` | Escalate one priority level; body contains only `reason` (5-500 characters). Actor comes from authenticated identity. Returns 200 with `ticket` and `escalation`; 400 for invalid input, 404 for unknown ticket, 409 for Critical/Resolved/Closed tickets. |
| `GET /api/tickets/{id}/escalations` | Read-only audit history ordered by escalation timestamp descending, then ID descending. Returns 200 with an array (empty for an existing ticket without history), or 404 for an unknown ticket. |
| `PATCH /api/tickets/{id}/status` · `/assignment` | Change status / assignee |
| `GET /api/customers` · `/api/agents` · `/api/categories` | Reference data |

Errors are `ProblemDetails`: `400` validation, `404` not found, `409` conflict or business rule.

**Seed data:** 5 categories, 6 customers (3 Premium), 6 agents (one inactive, one at their limit)
and 40 tickets across every status, priority and SLA state.

## Troubleshooting

### Automatic triage limitations

Ticket creation applies configured priority/SLA rules and selects an agent from the current
database workload. The creation response includes `triage` reasons; these explanations are
not persisted and are not returned on later GET requests.

Capacity is not atomically enforced across concurrent requests. `SaveChangesAsync` commits
the ticket values together, but the earlier workload read is outside that transaction. Two
requests can observe one remaining slot and both choose the same agent. The schema has no
capacity constraint or reservation mechanism. Reliable enforcement requires a database-wide
transaction/locking strategy spanning workload selection and insert, coordinated with other
assignment/status writers and SQL Server retry handling. A process-local lock would not
protect multiple API instances. Reference generation also retains its existing concurrent
collision risk (both requests can compute the same next reference). No concurrency-safety
claim is made for this implementation.

### Escalation audit storage

`TicketEscalations` stores immutable before/after snapshots, with a required ticket reference
and optional previous/next agent references. All three foreign keys restrict deletion, so
deleting a ticket or referenced agent cannot erase its audit history. The ticket index supports
history lookup; agent indexes support foreign-key checks. Dates use UTC and `datetime2(3)`.
Migration `20261011095659_AddTicketEscalations` adds only this table, its checks and indexes.

The entity has no public setters or mutation methods. `SupportDbContext` rejects tracked
updates/deletes through both synchronous and asynchronous saves. This is application-level
append-only protection, not a database trigger: raw SQL or bulk operations can bypass it and
must not be used to rewrite history. SQL Server mapping tests can be run with `SUPPORTDESK_TEST_SQLSERVER` and
filter `FullyQualifiedName~TicketEscalationMappingTests`; each case migrates and deletes only
its own `SupportDeskEscalationTest_<guid>` database.

The escalation handler captures UTC time once, calculates the next priority's SLA from that
time, and retains an eligible incumbent after excluding this ticket from their capacity
comparison. Otherwise it selects a replacement or leaves the ticket unassigned. Ticket
changes and a new audit snapshot share one EF `SaveChangesAsync` transaction. Tests inject
database failures into ticket updates and audit inserts and verify neither is committed.
`TicketEscalationApiTests` also supports isolated SQL Server runs, using only disposable
`SupportDeskEscalationApiTest_<guid>` databases. History retrieval uses no-tracking
database filtering, ordering and DTO projection, without changing stored records.

New `escalatedBy` values come from authenticated agent identity, not request JSON.
Historical caller-supplied audit records remain unchanged. Atomic saves do not prevent two concurrent requests from reading the same
ticket version and producing stale updates/history. There is no optimistic concurrency
token or locking spanning reads and writes. Cross-request assignment capacity is also
subject to the previously documented race; concurrency safety is not claimed.

### Dynamic SLA status

SLA status is calculated on each ticket read, never persisted. Each query captures
`IClock.UtcNow` once. The current window starts at the latest escalation timestamp
(ordered by timestamp then ID), or ticket creation when no escalation exists, and
ends at the stored deadline. This preserves the applied Premium multiplier and minimum
even if configuration changes later. Assignment/status update timestamps do not restart it.
`AtRiskThresholdPercent` remains configurable: remaining ticks at or below that percentage
of the current window are AtRisk, including the exact deadline; only time after the
deadline is Breached. A recorded resolution on/before the deadline is Met, otherwise Breached.
Closed tickets use their preserved resolution time. Legacy terminal tickets with no
resolution time, tickets with no deadline, and invalid non-positive open windows cannot
be assessed and return NotApplicable. No completion timestamp is guessed.
The evaluator receives explicit UTC timestamps and a validated threshold, keeping Domain
independent of clock/configuration services. Query projection retrieves the latest start
in SQL and evaluates status at the final projection. The optional `slaStatus` query parameter
accepts `NotApplicable`, `WithinSla`, `AtRisk`, `Breached`, or `Met`. It combines with other
filters using AND and filters in SQL before counting, sorting and paging. Classification
rules are shared with the evaluator through an expression tree; no client-side filtering
or persisted status is used. SQL Server durations use day boundaries and time parts,
avoiding small integer DATEDIFF overflows and retaining sub-millisecond clock precision.
SQLite coverage uses millisecond-precision stored dates, matching this application's
SQL Server `datetime2(3)` columns; it is not proof of SQL Server translation.
There is no separate SLA-start column or migration. Existing atomic escalation history
provides the restart time; legacy due dates without history are assumed to start at creation.
Concurrent escalation limitations described above remain unchanged.

`TicketSlaFilteringTests` defaults to SQLite. Set `SUPPORTDESK_TEST_SQLSERVER` to a SQL
Server connection string and run it with the same test filter to verify SQL Server behavior.
Each case creates, migrates and drops only its own `SupportDeskSlaFilterTest_<guid>` database.
Tests inspect executed count/page SQL and compare filtered membership with the evaluator.

### Automatic triage verification

The normal backend suite uses SQLite for repeatable relational query and HTTP creation
tests. The same `TicketCreationTests` can also run against SQL Server with the existing
migrations. Set a server connection string (create/drop database permission is required):

```powershell
$env:SUPPORTDESK_TEST_SQLSERVER = 'Server=localhost\SQLEXPRESS;Integrated Security=True;TrustServerCertificate=True;Encrypt=False'
dotnet test apps/api/tests/SupportDesk.UnitTests/SupportDesk.UnitTests.csproj -c Release --filter FullyQualifiedName~TicketCreationTests
Remove-Item Env:SUPPORTDESK_TEST_SQLSERVER
```

Each case overrides the supplied database name with a unique
`SupportDeskTriageTest_<guid>` database and deletes only that database during cleanup.
Existing application data is not seeded, changed or deleted. SQL Server-only tests are
explicitly skipped when the environment variable is absent; ordinary cases use SQLite.

Coverage is split between existing policy tests (all priority/tier combinations, flags,
eligibility, fewest-open selection and ID ties), `AgentQueriesTests` (real open-count and
specialization projection), configuration/domain tests, and HTTP creation tests (201,
response reasons, persisted values, custom windows/floor, unavailable agents, 400 and 404).
Command tests also verify that query/save failures propagate instead of returning success.

SQL Server Express verification on 2026-10-11 passed all 17 HTTP creation/concurrency cases.
This is SQL Server verification, separate from the default SQLite suite. Cleanup was
confirmed with zero remaining `SupportDeskTriageTest_` databases.

Two test-only scheduling gates reproduce unsafe interleavings through real HTTP requests:

- `Post_ConcurrentStaleWorkloadsCanExceedCapacity` holds two completed workload reads,
  then lets reference reads/inserts proceed sequentially. Both requests return 201 with
  distinct references, but assign two open tickets to an agent whose capacity is one.
- `Post_ConcurrentReferenceReadsCanCauseUniqueKeyConflict` holds both generated references
  before either insert. One request returns 201; the other encounters SQL Server duplicate
  key error 2601/2627 and returns 500. Only one ticket is saved.

These are diagnostic tests that assert the current limitations, not proof of safety.
The decorators only control timing and collect errors; production code is unchanged.
They must be updated when proper cross-request protection is implemented.

| Symptom | Fix |
| --- | --- |
| API cannot reach the database | `npm run db:up`, wait ~20 s, check `docker compose ps` |
| Port 1433 already in use | Change the port in `docker-compose.yml` and `.env` |
| Browser warns about the certificate | `dotnet dev-certs https --trust`, or use the web app, which proxies |
| Web app shows "Unable to load tickets" | The API is not running, or `VITE_API_PROXY_TARGET` points at the wrong port |
| SQL Server slow on Apple Silicon | Enable Rosetta in Docker Desktop → Settings → General |

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
cp .env.example .env          # optional, every value has a working default
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

Both pass on a clean checkout and neither needs a database.

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

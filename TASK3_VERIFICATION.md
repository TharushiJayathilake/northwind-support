# Task 3 Authentication Verification

Verified on 2026-10-11. Current branch: `feature/task-3-authentication`.
No staging, commits, pushes, branch switches, merges, rebases or resets were performed.

## Completion

Steps 60-81 are implemented: credentials/migration, password hashing, JWT login/me,
central authorization, authenticated escalation identity, React login/session/routes,
logout/expiry/401 handling, tests and documentation. Task 4 and optional reporting
were not implemented. Functional implementation is complete; real browser visual,
keyboard and click-through verification remains outstanding.

## Executed Results

| Check | Passed | Failed | Skipped | Warnings |
| --- | ---: | ---: | ---: | ---: |
| Backend unit/database/HTTP tests | 322 | 0 | 3 | - |
| Architecture tests | 4 | 0 | 0 | - |
| Frontend tests, 13 files | 135 | 0 | 0 | - |
| Separate SQL Server query/schema/HTTP tests | 119 | 0 | 0 | - |
| Live demo HTTP/proxy/database smoke assertions | 33 | 0 | 0 | - |
| Backend Release build | Successful | 0 errors | - | 0 |
| Frontend TypeScript | Successful | 0 errors | - | - |
| Frontend production build | Successful | 0 errors | - | 0 reported |
| ESLint | Successful | 0 errors | - | 0 |
| EF migration consistency | No pending model changes | - | - | - |
| git diff --check | Successful | 0 whitespace errors | - | See note |

Git reports a CRLF-to-LF normalization notice for the generated model snapshot.
This is not a whitespace failure or a compiler/linter warning. Vitest prints a
jsdom performance suggestion, not a failing check or lint warning.

The default three backend skips are SQL Server-only schema/concurrency diagnostics;
all executed in the separate SQL run. SQL tests overlap default tests and should
not be added as unique cases. Default suites total 461 passed and three skipped.

## Architecture and Migration

- Nullable `Agent.PasswordHash`, with a domain method accepting an already-produced
  hash. No Identity/EF dependency was introduced into Domain or Application.
- Application `IAgentAuthentication` returns only safe agent display data;
  Infrastructure implements database lookup, Identity hashing verification and
  rehashing. Presentation issues and validates JWTs and applies fallback authorization.
- Migration `20261011143214_AddAgentCredentials` adds only nullable
  `Agents.PasswordHash nvarchar(512)`. Existing data and relationships are preserved.
  It was applied to fresh SQL test/demo databases, not existing user databases.
- Password hashing uses the established ASP.NET Core Identity implementation,
  not custom cryptography. Tests verify random salts, correct/incorrect passwords,
  inactive/unknown users and non-overwriting development credential seeding.
- JWTs have a stable AgentId subject, issuer, audience, HS256 signature and expiry.
  Signing keys are process/environment configuration, never committed secrets.
  Settings validation fails startup for missing/short keys or invalid settings.
- Every controller is protected by the central policy except anonymous login.
  Development Swagger and `/health` are publicly accessible.
- The request DTO for escalation contains only `Reason`. The internal command
  receives the authenticated actor from the controller. Extra actor JSON is ignored.
  History contains `agent:<id> <name>`, with the name truncated if necessary to keep
  the total within 100 characters. The ID prefix is retained. Old history is untouched.

## Frontend

React has a typed provider/session, protected routes, accessible email/password
login, pending/error feedback, requested-route return, agent display and logout.
The shared API client attaches JWTs and clears the matching session on protected
401 responses. Login 401 does not create a redirect loop. Session generation checks
prevent pending login or old 401 responses from reviving/clearing newer state.

sessionStorage preserves the tab session across refresh, with runtime narrowing
on restoration and login responses. `/auth/me` validates restored sessions and
refreshes agent display data; timers clear expired sessions. No password is stored.
The temporary actor input and payload field were removed from escalation. Triage,
filters, SLA indicators, history and refresh functionality remain covered by tests.

## Live Verification Details

Real HTTP fixtures use actual Kestrel servers, signed JWTs, production authentication
configuration and fresh disposable SQL Server databases. The 119 SQL cases cover
authentication and Task 2 regressions, including migration/schema, triage, escalation,
history, atomic rollback, SLA classification/filtering and known concurrency races.
No disposable test databases remained after cleanup.

A separate actual Development API and Vite React dev server were started:

- React: `http://127.0.0.1:5181/login`
- API: `http://127.0.0.1:7045`
- Swagger: `http://127.0.0.1:7045/swagger`
- Isolated demo database: `SupportDeskAuthSmoke_8248edab5d074b2b8f0a3377ab698d0e`
- API launcher process: 17208; Vite process: 7728.

The fresh demo database was intentionally retained so the application can be tried.
It is separate from user/application databases. Its JWT key is ephemeral and exists
only in the API process environment; restarting requires configuration as in README.

A final rebuild initially encountered the running demo's locked Windows executable
(MSB3026 retries followed by copy errors). Only that verified isolated process was
stopped; the complete test run and Release build then passed cleanly. The demo was
restarted against the same database with a fresh signing key. Login/me/health were
rechecked successfully, and the existing development credential still verified.

Live HTTP checks exercised the React proxy, not mocked fetches: login, wrong/unknown/
inactive credentials, missing/invalid bearer tokens, me, Standard/Premium/forced
triage, automatic assignment, escalation with spoofed actor JSON, sequential
escalations, history, forbidden operations, all SLA filters, combined filters/counts
and cleared client authorization. Direct SQL reads confirmed ticket values, two
audit records and hashed-only development credentials. Public health and Swagger
were also checked. Two initial smoke-harness assumptions were corrected (unknown
routes meet fallback authorization; customers return an array, not a paged object).
They were harness issues, not application fixes; the final complete run passed.

No browser automation connector or installed browser automation package was
available. Serving React and invoking its proxy is not a real browser UI test.
Browser login/click-through, responsive appearance and keyboard interaction remain
unverified. Frontend tests use jsdom and mocked API responses.

## Test Changes

- `AgentAuthenticationTests`: two relational tests for salted hashing/verification
  and repeatable development seeding that preserves an existing credential.
- `JwtSettingsTests`: six invalid configuration cases.
- `TicketCreationTests`: public login/me, identical rejection messages, central
  authorization on five endpoint families, invalid/expired/wrong-audience/wrong-key
  tokens and authenticated login-create-triage-escalate-history-logout workflow.
- `TicketEscalationApiTests`: spoofed actor ignored, actual JWT actor persisted.
- `TestAuthentication`: generated test keys, real signed bearer tokens and the same
  production authentication policy; no anonymous test bypass.
- `AuthFlow.test.tsx`: 15 frontend cases for validation, navigation, session display,
  token attachment, failures, pending submissions, logout, expiry, restoration,
  malformed data and stale-response/session-generation protection.
- Existing escalation tests now submit only a reason. Four backend and three
  frontend caller-actor validation cases became obsolete; actor identity is no
  longer input. Domain audit field-length tests remain.
- The all-agents-deactivated creation HTTP case now expects 401 because the caller
  was also deactivated. Capacity/specialist cases still prove unassigned 201 creation.

## Remaining Security and Concurrency Limitations

- HTTPS is required outside the loopback-only demo. The demo uses HTTP locally.
- sessionStorage exposes JWTs to JavaScript/XSS; production should evaluate an
  HttpOnly-cookie/BFF design, CSP and CSRF protections appropriate to that design.
- No rate limiting, account lockout, MFA, recovery, refresh tokens, roles or external
  identity provider. These were intentionally excluded, not silently implemented.
- Logout is client-side, not server-side revocation. Copied JWTs remain usable until
  expiry or agent deactivation. Password changes do not invalidate tokens by stamp.
- Signing-key rotation and secure provisioning are operational production work.
- Existing cross-request capacity/reference/escalation races remain documented.
  Authentication does not resolve those races or add concurrency guarantees.
- Audit append-only protection applies to supported EF saves, not raw SQL/bulk writes.
- Successful ticket JSON continues to rely on its typed backend contract; it is
  not fully runtime-schema validated. Auth login/restoration and errors are narrowed.

## Manual Commit Files

All current modified/untracked paths below belong to Task 3. Review and stage
manually; no Git write operations were performed.

### Root

- `.env.example`
- `Directory.Packages.props`
- `README.md`
- `TASK3_VERIFICATION.md` (new)

### Backend

- `apps/api/src/SupportDesk.Application/Abstractions/IAgentAuthentication.cs` (new)
- `apps/api/src/SupportDesk.Application/Contracts/Tickets/TicketRequests.cs`
- `apps/api/src/SupportDesk.Application/Features/Tickets/Commands/EscalateTicket/EscalateTicketCommandHandler.cs`
- `apps/api/src/SupportDesk.Application/Features/Tickets/Commands/EscalateTicket/EscalateTicketRequestValidator.cs`
- `apps/api/src/SupportDesk.Domain/Aggregates/Agents/Agent.cs`
- `apps/api/src/SupportDesk.Infrastructure/Data/Configurations/AgentConfiguration.cs`
- `apps/api/src/SupportDesk.Infrastructure/Data/Migrations/20261011143214_AddAgentCredentials.cs` (new)
- `apps/api/src/SupportDesk.Infrastructure/Data/Migrations/20261011143214_AddAgentCredentials.Designer.cs` (new)
- `apps/api/src/SupportDesk.Infrastructure/Data/Migrations/SupportDbContextModelSnapshot.cs`
- `apps/api/src/SupportDesk.Infrastructure/Data/SupportDbSeeder.cs`
- `apps/api/src/SupportDesk.Infrastructure/DependencyInjection.cs`
- `apps/api/src/SupportDesk.Infrastructure/Services/AgentAuthentication.cs` (new)
- `apps/api/src/SupportDesk.Infrastructure/SupportDesk.Infrastructure.csproj`
- `apps/api/src/SupportDesk.Presentation/Authentication/AuthenticationExtensions.cs` (new)
- `apps/api/src/SupportDesk.Presentation/Authentication/JwtSettings.cs` (new)
- `apps/api/src/SupportDesk.Presentation/Authentication/JwtTokens.cs` (new)
- `apps/api/src/SupportDesk.Presentation/Controllers/AuthController.cs` (new)
- `apps/api/src/SupportDesk.Presentation/Controllers/TicketsController.cs`
- `apps/api/src/SupportDesk.Presentation/Program.cs`
- `apps/api/src/SupportDesk.Presentation/SupportDesk.Presentation.csproj`
- `apps/api/tests/SupportDesk.UnitTests/Infrastructure/AgentAuthenticationTests.cs` (new)
- `apps/api/tests/SupportDesk.UnitTests/Presentation/JwtSettingsTests.cs` (new)
- `apps/api/tests/SupportDesk.UnitTests/Presentation/TicketCreationTests.cs`
- `apps/api/tests/SupportDesk.UnitTests/Presentation/TicketEscalationApiTests.cs`
- `apps/api/tests/SupportDesk.UnitTests/TestDoubles/TestAuthentication.cs` (new)

### Frontend

- `apps/web/src/App.tsx`
- `apps/web/src/api/client.ts`
- `apps/web/src/features/auth/AuthFlow.test.tsx` (new)
- `apps/web/src/features/auth/AuthProvider.tsx` (new)
- `apps/web/src/features/auth/LoginPage.tsx` (new)
- `apps/web/src/features/auth/ProtectedRoute.tsx` (new)
- `apps/web/src/features/auth/context.ts` (new)
- `apps/web/src/features/auth/session.ts` (new)
- `apps/web/src/features/tickets/TicketEscalationFlow.test.tsx`
- `apps/web/src/features/tickets/components/EscalationEligibility.test.tsx`
- `apps/web/src/features/tickets/components/EscalationForm.test.tsx`
- `apps/web/src/features/tickets/components/EscalationForm.tsx`
- `apps/web/src/index.css`
- `apps/web/src/main.tsx`
- `apps/web/src/routes.tsx`
- `apps/web/src/types/api.ts`

Suggested commit: `feat(auth): add agent login, JWT authorization and React sessions`

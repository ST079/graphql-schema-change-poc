# Veel GraphQL Schema Change Intelligence (POC)

A command-line tool that detects changes to a GraphQL schema and tells frontend and mobile developers what the changes mean for their code.

Given an **old** and a **new** schema plus the GraphQL operations used by each client (Android, Frontend, …), the tool will:

- Diff the schemas (added, removed, modified, or deprecated types, fields, arguments, and enum values)
- Classify each change by severity: `Info`, `Warning`, or `Breaking`
- Parse the client operations (AST-based, including nested selections and fragments)
- Work out which client operations are affected and which are not
- Suggest *possible* migrations, such as `Campaign.videoUrl` → `Campaign.video`. These are always labelled as suggestions and never as confirmed renames.
- Write a Markdown report and a JSON report, and optionally send a webhook notification

The tool doesn't block schema changes by default. Its job is to show the impact of a change and help backend and client developers talk about it. The full specification is in [`CLAUDE.md`](./CLAUDE.md).

> **Want to see it in action?** Follow the step-by-step demo in [`run.md`](./run.md).

## Architecture

```text
                   GraphQL Schema (old + new)
                         │
                         ▼
                  ┌───────────────┐
                  │ SchemaLoader  │
                  └──────┬────────┘
                         ▼
                  ┌───────────────┐
                  │ Schema Diff   │  SchemaDiffer
                  └──────┬────────┘
                         ▼
                  ┌───────────────┐
                  │Classification │  ChangeClassifier
                  └──────┬────────┘
                         ▼
                  ┌───────────────┐   ◄── client .graphql operations
                  │Client Analysis│  OperationAnalyzer
                  └──────┬────────┘
                         ▼
                  ┌───────────────┐
                  │Impact Analysis│  ImpactAnalyzer
                  └──────┬────────┘
                         ▼
                    ChangeReport
                    /     |      \
                   ▼      ▼       ▼
             Markdown   JSON    Webhook (optional, off in CI)
                  │       │
                  └───┬───┘
                      ▼
               GitHub Actions
                      │
               ┌──────┴──────┐
               ▼             ▼
          Job Summary     Artifact
```

Each stage only consumes the output of the previous one. Report generators and the webhook all format the same `ChangeReport`, and the workflow only prepares inputs and runs the CLI.

## Project Structure

```text
.
├── Veel.GraphQL.SchemaIntelligence.sln
├── .github/workflows/
│   └── graphql-schema-intelligence.yml   # CI: build, test, analyze, publish report
├── nuget.config                          # Pins package source to nuget.org
├── schema-intelligence.example.json      # Example config (copy to schema-intelligence.json)
├── src/
│   ├── Veel.GraphQL.SchemaIntelligence/        # CLI + domain logic
│   │   ├── Program.cs                        # CLI: analyze --old-schema --new-schema
│   │   ├── Changes/
│   │   │   ├── ChangeType.cs                 # Kinds of structural change
│   │   │   ├── SchemaChange.cs               # One detected change
│   │   │   ├── ChangeSeverity.cs             # Info / Warning / Breaking
│   │   │   ├── ClassifiedSchemaChange.cs     # Change + severity + reason
│   │   │   └── ChangeClassifier.cs           # SchemaChange → ClassifiedSchemaChange
│   │   ├── Clients/
│   │   │   ├── ClientDefinition.cs           # Client name + operations directory
│   │   │   ├── GraphQLOperation.cs           # Operation model, selections, field references
│   │   │   ├── ClientOperationReference.cs   # An operation that uses a changed element
│   │   │   ├── OperationLoadException.cs     # User-facing operation load errors
│   │   │   └── OperationAnalyzer.cs          # Load operations, resolve fields, match changes
│   │   ├── Impact/
│   │   │   ├── ClientImpact.cs               # Impact of one change on one client
│   │   │   ├── SchemaChangeImpact.cs         # One change + its client impacts + action
│   │   │   ├── ImpactAnalysis.cs             # Full result + summary
│   │   │   └── ImpactAnalyzer.cs             # Changes + affected operations → impacts
│   │   ├── Notifications/
│   │   │   ├── INotificationSender.cs        # Delivers a ChangeReport somewhere
│   │   │   ├── WebhookNotificationSender.cs  # Generic HTTP POST of the JSON report
│   │   │   ├── WebhookOptions.cs             # Config file + environment variables
│   │   │   └── NotificationException.cs      # Secret-free delivery/config errors
│   │   ├── Reporting/
│   │   │   ├── ChangeReport.cs               # Format-independent report model
│   │   │   ├── MarkdownReportGenerator.cs    # ChangeReport → Markdown
│   │   │   └── JsonReportGenerator.cs        # ChangeReport → JSON (versioned contract)
│   │   └── Schema/
│   │       ├── GraphQLSchema.cs              # Parser-independent schema model
│   │       ├── SchemaLoader.cs               # SDL parsing + validation
│   │       ├── SchemaLoadException.cs        # User-facing load/validation errors
│   │       └── SchemaDiffer.cs               # Old vs new schema → SchemaChange[]
│   └── Veel.GraphQL.SchemaIntelligence.Tests/  # xUnit tests
│       ├── ChangeClassifierTests.cs
│       ├── DemoFixturesTests.cs
│       ├── ImpactAnalyzerTests.cs
│       ├── JsonReportGeneratorTests.cs
│       ├── OperationAnalyzerTests.cs
│       ├── ReportGeneratorTests.cs
│       ├── SchemaDifferTests.cs
│       ├── SchemaLoaderTests.cs
│       ├── TestReports.cs
│       ├── WebhookNotificationSenderTests.cs
│       ├── WebhookOptionsTests.cs
│       └── TestPaths.cs
├── reports/
│   ├── schema-change-report.md           # Generated by the demo run
│   └── schema-change-report.json         # Generated by the demo run
└── demo/
    ├── old-schema.graphql                # Campaign with videoUrl (+ unchanged User)
    ├── new-schema.graphql                # Campaign with video: CampaignVideo (+ unchanged User)
    ├── android/
    │   ├── GetCampaign.graphql           # uses videoUrl → expected affected
    │   └── GetCampaignDetails.graphql    # uses videoUrl → expected affected
    └── frontend/
        ├── CampaignDetails.graphql       # uses videoUrl → expected affected
        ├── CampaignCard.graphql          # no videoUrl  → expected unaffected
        └── GetUser.graphql               # unrelated    → expected unaffected
```

## Implementation Status

| Phase | Description                | Status         |
|-------|----------------------------|----------------|
| 1     | Project setup              | ✅ Done        |
| 2     | Schema loader              | ✅ Done        |
| 3     | Schema diff engine         | ✅ Done        |
| 4     | Change classification      | ✅ Done        |
| 5     | GraphQL operation parser   | ✅ Done        |
| 6     | Client impact analyzer     | ✅ Done        |
| 7     | Markdown report            | ✅ Done        |
| 8     | JSON report                | ✅ Done        |
| 9     | Webhook notification       | ✅ Done        |
| 10    | Testing & CI readiness     | ✅ Done        |
| 11    | GitHub Actions             | ✅ Done        |
| 12    | Cerberus integration       | ⏳ Not started |

The CLI loads two schemas and prints each structural change with its severity. If client directories are given, it also prints which client operations each change affects and a recommended action. It writes a Markdown report and a JSON report to `<output>/schema-change-report.md` and `.json`.

Schema loading is available as a library through `SchemaLoader.LoadFromFile(path)` and `SchemaLoader.Parse(sdl)`. Both return a validated `GraphQLSchema`, or throw `SchemaLoadException` with a message that can be shown to the user. `SchemaDiffer.Compare(old, new)` returns the changes as `SchemaChange` records.

### Schema loading: what is supported

- Object, interface, union, enum, input object, and custom scalar types
- Field arguments, default values, list and non-null type wrappers
- `@deprecated` on fields, arguments, input fields, and enum values
- An explicit `schema { query: ... }` definition, or the conventional `Query`/`Mutation`/`Subscription` names
- Directive definitions and descriptions are accepted but not modelled
- Validation catches unknown type references, duplicate names (including a repeated interface or union member), a missing or invalid root type, input/output type misuse, invalid interface or union members, and empty types
- Not supported yet: `extend type ...`. The schema must be provided with extensions already merged in, which is how HotChocolate exports it.

### Schema diffing: detected changes

| Area | Change types |
|---|---|
| Types | `TypeAdded`, `TypeRemoved`, `TypeKindChanged` |
| Object/interface fields | `FieldAdded`, `FieldRemoved`, `FieldTypeChanged` (includes nullability and list changes) |
| Field arguments | `ArgumentAdded`, `ArgumentRemoved`, `ArgumentTypeChanged` |
| Input object fields | `InputFieldAdded`, `InputFieldRemoved`, `InputFieldTypeChanged` |
| Enums | `EnumValueAdded`, `EnumValueRemoved` |
| Interfaces / unions | `InterfaceAdded`, `InterfaceRemoved`, `UnionMemberAdded`, `UnionMemberRemoved` |
| Deprecation | `DeprecationAdded`, `DeprecationRemoved` (fields, arguments, input fields, enum values) |

The differ reports only what changed in the structure; it does not judge severity. A removal and an addition are never paired up as a rename. Output is sorted by type name, field, argument, then change type.

Not detected yet:

- Default value changes
- Changes to a deprecation reason
- Changes to root operation types

### Change classification: severity rules

`ChangeClassifier` looks only at the change itself. It does not consider which clients are affected; that comes later, in impact analysis.

| Severity | Change types |
|---|---|
| **Breaking** | `TypeRemoved`, `TypeKindChanged`, `FieldRemoved`, `FieldTypeChanged`, `ArgumentRemoved`, `ArgumentTypeChanged`, `InputFieldRemoved`, `InputFieldTypeChanged`, `EnumValueRemoved`, `InterfaceRemoved`, `UnionMemberRemoved`, and `ArgumentAdded`/`InputFieldAdded` when the new item is non-null and has no default value |
| **Warning** | `DeprecationAdded` |
| **Info** | `TypeAdded`, `FieldAdded` (non-null fields included), `EnumValueAdded`, `InterfaceAdded`, `UnionMemberAdded`, `DeprecationRemoved`, and `ArgumentAdded`/`InputFieldAdded` when the new item is optional or has a default |

Type changes are always classified as Breaking. That includes changes that would be safe in GraphQL, such as making an output field `String` → `String!`. The classifier doesn't analyse type compatibility in more detail yet. An unknown `ChangeType` throws a `NotSupportedException` instead of getting a guessed severity.

### Client operation analysis

`OperationAnalyzer` uses the same rules for every client. A client is just a `ClientDefinition(Name, OperationsDirectory)`.

- **Loading:** It scans the directory recursively for `*.graphql` and `*.gql` files and parses them with GraphQL-Parser's AST. It never uses string matching or regex.
- **What it understands:**
  - Queries, mutations and subscriptions, including anonymous operations
  - Several operations in one file
  - Nested fields, aliases and arguments
  - Variables
  - Inline fragments, and named fragments, which may be defined in another file of the same client
- **Field references:** It resolves selections against the **old schema** (the schema clients are built against today). For example, `campaign.video.url` becomes `Query.campaign` → `Campaign.video` → `CampaignVideo.url`. Fragment spreads are expanded, and each reference records the fragment it came through.
- **Matching a change to operations:**
  - **Field changes:** operations that select that exact `Type.field`.
  - **Argument changes:** operations that select the field and pass that argument.
  - **Enum, input-type and type-level changes:** operations that select fields on the type, receive it, pass it as an argument, or declare a variable of that type.
- **Not handled yet:**
  - Input or enum types nested inside other input types used in variables
  - Directives such as `@include` and `@skip`. They're ignored, so conditionally selected fields still count as used.

### Impact analysis

`ImpactAnalyzer.Analyze(classifiedChanges, operationReferences)` returns an `ImpactAnalysis`. It has three levels:

- **`SchemaChangeImpact`**, one per change. It holds the change, its client impacts, the recommended action, and possible migrations. `HasClientImpact` says whether any client uses the changed element.
- **`ClientImpact`**, one per change per affected client. It lists the client's affected operations, sorted by name.
- **`ImpactSummary`**: counts per severity, the affected clients, and for each client the number of distinct affected operations and the highest severity.

Rules:

- Severity is always the classifier's. A Breaking change stays Breaking even when no scanned client uses it; it is simply reported with no client impact.
- Results are ordered by severity (Breaking first), then by schema location. Clients and operations are sorted by name.
- Recommended actions come from fixed rules for each change type; no AI or LLM is involved.
- **Possible migrations:** for a removed field, any field added to the same type whose name contains or is contained in the removed name is suggested, for example `videoUrl` → `video`. These are always labelled *"Possible migration candidate … Developer verification required."* They are never treated as a rename.

### Markdown report

`ChangeReport.Create(service, generatedAt, analysis, clients, operations)` builds the report model. It wraps the impact analysis and adds the clients that were scanned and the operations that were not affected. `MarkdownReportGenerator.Generate(report)` only formats it, and the same report always produces exactly the same text: the timestamp is passed in, and line endings are always `\n`.

Report sections, in order:

1. Title, service, generation time, and the schemas that were compared
2. Summary table
3. One section per severity (🔴 Breaking, 🟡 Warnings, 🟢 Informational). Sections with no changes are left out. Each change shows its type, severity, reason, affected clients, recommended action, and any possible migration candidate (always marked *Developer verification required*).
4. Client Impact, per client, listing each affected operation and the changes it uses
5. Unaffected Operations
6. A table of all changes
7. Recommendation, shown only when a breaking change affects a client

With no changes, the report contains only the summary line *No schema changes detected.* If a breaking change has no detected users, the report says so explicitly. If no clients were scanned, it says that instead.

### JSON report

`JsonReportGenerator.Generate(report)` formats the same `ChangeReport` that the Markdown generator uses. The JSON follows a fixed contract that is kept separate from the internal C# classes, so refactoring the code won't change the output. The contract is versioned (`reportVersion: 1`): new properties may be added, and an incompatible change bumps the version.

```text
{ reportVersion, serviceName, generatedAt, oldSchema, newSchema,
  summary: { totalChanges, breakingChanges, warningChanges, infoChanges,
             affectedClients, affectedOperations, clients: [{ clientName, affectedOperations, highestSeverity }] },
  changes: [{ changeType, severity, path, schemaType, fieldName, argumentName, oldType, newType,
              oldValue, newValue, description, reason, recommendedAction, hasClientImpact,
              possibleMigrations: [{ from, to, confidence: "Possible", developerVerificationRequired: true }] }],
  clientImpacts: [{ clientName, severity, changeType, path, schemaType, fieldName, argumentName, oldType, newType,
                    affectedOperations: [names], operations: [{ name, operationType, filePath, usages }],
                    reason, recommendedAction }],
  scannedClients: [names],
  unaffectedOperations: [{ clientName, operationName, filePath }] }
```

- Enums are written as names, for example `"Breaking"` and `"FieldRemoved"`.
- Meaningful nulls are kept, for example `"newType": null` for a removed field.
- Collections are always arrays, never null.
- Output is indented, uses `\n` line endings, and is identical for the same report.

### Webhook notification

The POC can optionally send the generated schema change report to an external HTTP webhook. The webhook is **provider-agnostic**: it POSTs the same JSON as `schema-change-report.json`, so ClickUp, n8n, an internal notification service or anything else can consume it through its own adapter. The analysis code never changes for this.

```text
Schema change → Impact analysis → ChangeReport → JSON → HTTP POST → External notification system
```

- **Request:** `POST` with `Content-Type: application/json; charset=utf-8`. The body is exactly the JSON report (`reportVersion: 1`). Event metadata goes in headers so the body stays identical to the report: `X-Schema-Intelligence-Event: graphql.schema.changed` and `X-Schema-Intelligence-Report-Version: 1`. If a bearer token is configured, it is sent as `Authorization: Bearer <token>`.
- **Disabled by default.** When disabled, no request is made. The CLI sends a webhook only when there are schema changes, and always after the reports have been written.
- **Failures:**
  - A non-2xx response, a timeout (`TimeoutSeconds`, default 10), an unreachable endpoint, or invalid configuration produces a clear `ERROR:` and exit code 1.
  - There are no retries.
  - Error messages never include the URL, the token or the response body.

**Configuration.** Settings are applied in this order, and later ones override earlier ones:

1. A JSON file passed with `--config <file>`
2. Environment variables
3. `--webhook <url>`, which also enables the webhook

```json
{
  "SchemaIntelligence": {
    "Webhook": {
      "Enabled": true,
      "Url": "https://example.com/webhook",
      "TimeoutSeconds": 10
    }
  }
}
```

| Environment variable | Meaning |
|---|---|
| `SCHEMA_INTELLIGENCE_WEBHOOK_ENABLED` | `true` / `false` |
| `SCHEMA_INTELLIGENCE_WEBHOOK_URL` | Endpoint. `GRAPHQL_CHANGE_WEBHOOK_URL` is also accepted. |
| `SCHEMA_INTELLIGENCE_WEBHOOK_TIMEOUT_SECONDS` | Timeout in seconds (default 10) |
| `SCHEMA_INTELLIGENCE_WEBHOOK_BEARER_TOKEN` | Optional bearer token |

> ⚠️ **Never commit real webhook URLs or tokens.** Many webhook URLs contain a secret. Use environment variables (or CI secrets), or a local `schema-intelligence.json`, which is git-ignored. `schema-intelligence.example.json` is a safe template.

## Prerequisites

- .NET SDK 10.0 or later (`dotnet --list-sdks`)
- NuGet package: [`GraphQL-Parser`](https://www.nuget.org/packages/GraphQL-Parser) (graphql-dotnet). It has no dependencies and parses both SDL and operations.

The repo includes a local `nuget.config` that uses only nuget.org. This lets the project restore without access to any private or company package feeds.

## Build

```bash
dotnet build Veel.GraphQL.SchemaIntelligence.sln
```

## Testing

```bash
dotnet build
dotnet test
```

The xUnit suite runs in about a second. It needs no network access, no external services, and no machine-specific paths. It covers:

| Area | Test file(s) |
|---|---|
| Schema loading and validation | `SchemaLoaderTests` |
| Schema diffing, including nullable/list wrappers and deterministic ordering | `SchemaDifferTests` |
| Severity classification | `ChangeClassifierTests` |
| GraphQL operation analysis (queries, mutations, subscriptions, nesting, aliases, fragment chains) | `OperationAnalyzerTests` |
| Client impact analysis and recommendations | `ImpactAnalyzerTests` |
| Markdown reporting | `ReportGeneratorTests` |
| JSON reporting | `JsonReportGeneratorTests` |
| Webhook notification and configuration, using a fake HTTP handler | `WebhookNotificationSenderTests`, `WebhookOptionsTests` |
| Complete end-to-end analysis of the demo: changes, severities, affected and unaffected operations, Markdown/JSON consistency, the no-change scenario, determinism | `EndToEndTests` |
| The compiled CLI, run as a separate process: exit codes, report files, error messages without stack traces or secrets, webhook skipping | `CliTests` |

Test helpers:

- `TestReports.RunDemo()` runs the real pipeline on `demo/` with a fixed timestamp.
- `FakeHttpMessageHandler` records webhook requests.

Identical inputs produce identical reports, apart from the `Generated` timestamp, which the caller supplies. Every failure exits with code `1` and a single `ERROR:` line.

## CI

The workflow is `.github/workflows/graphql-schema-intelligence.yml`. It runs on pull requests and on pushes to `main` that touch GraphQL files, `src/`, `demo/`, the solution, `nuget.config`, or the workflow itself. Documentation-only changes don't trigger it. It runs these steps:

1. **restore**: `dotnet restore`, using the repository's `nuget.config`
2. **build**: `dotnet build --configuration Release --no-restore`
3. **test**: `dotnet test --configuration Release --no-build`
4. **schema analysis**: compares the schema at the **base** revision with the schema in **this** revision. For a PR, the base is the target-branch commit; for a push, it's the previous commit.
5. **report generation**: the CLI writes `reports/schema-change-report.md` and `.json`
6. **publication**: the Markdown report is added to the **job summary**, and both files are uploaded as the **`graphql-schema-change-report`** artifact (kept for 14 days)

**What passes and what fails.** The workflow is informational: it doesn't block schema changes.

| Situation | Result |
|---|---|
| Build failure, test failure, invalid schema, or invalid client operation (the analysis can't be trusted) | ❌ fail |
| Breaking change | ✅ pass, plus a `::warning` annotation and the report |
| Warning, informational change, or no changes | ✅ pass |

A future `failOnBreakingChange` setting could make breaking changes fail the build. It is **not implemented**, and the current behaviour is equivalent to `failOnBreakingChange: false`.

**Inputs.** These are workflow `env` settings:

| Setting | Value |
|---|---|
| `SCHEMA_FILE` | `demo/new-schema.graphql`, which stands in for the backend schema in this POC |
| `ANDROID_OPERATIONS` | `demo/android` |
| `FRONTEND_OPERATIONS` | `demo/frontend` |
| `SERVICE_NAME` | `Cerberus` |

The workflow reads the base version with `git show <base>:<SCHEMA_FILE>`. If there is no base version (the file is new, or the push is to a new branch), it posts a notice and compares the schema with itself. The real Android and frontend repositories aren't part of this repo, so CI analyzes the demo operations until they are connected.

**Security.**

- The workflow uses `permissions: contents: read`, `pull_request` (not `pull_request_target`), and `persist-credentials: false`.
- It provides **no secrets**, so the webhook is never enabled in CI and fork PRs can't reach anything sensitive.
- If notifications from CI are wanted later, enable them only for trusted `push` events on `main`, through repository secrets mapped to the `SCHEMA_INTELLIGENCE_WEBHOOK_*` variables. Never for pull requests.

**Running the same thing locally.** This is the command the workflow runs, with your own base schema:

```bash
dotnet restore && dotnet build -c Release --no-restore && dotnet test -c Release --no-build
git show origin/main:demo/new-schema.graphql > /tmp/base-schema.graphql
dotnet run --project src/Veel.GraphQL.SchemaIntelligence -c Release --no-build -- analyze \
  --old-schema /tmp/base-schema.graphql \
  --new-schema demo/new-schema.graphql \
  --android demo/android \
  --frontend demo/frontend \
  --service Cerberus \
  --output reports
```

When the CLI runs inside GitHub Actions (`GITHUB_ACTIONS=true`), it also prints a `::warning` annotation for breaking changes. That is the only CI-specific behaviour, and it never changes the exit code.

## Run the CLI

```bash
dotnet run --project src/Veel.GraphQL.SchemaIntelligence -- analyze \
  --old-schema ./demo/old-schema.graphql \
  --new-schema ./demo/new-schema.graphql \
  --android ./demo/android \
  --frontend ./demo/frontend
```

`--android` and `--frontend` are optional. Without them, only the schema changes are analyzed. Other options:

- `--service <name>` sets the service name shown in the report. The default is `GraphQL API`.
- `--output <dir>` sets where reports are written. The default is `reports`.
- `--format markdown|json|all` chooses which reports to write. The default is `all`.
- `--config <file>` reads webhook settings from a JSON file.
- `--webhook <url>` enables the webhook and sets its URL. This puts the URL in shell history, so in CI prefer the environment variables.

Passing the same file twice prints `No schema changes detected.`. Invalid input exits with code `1` and a single `ERROR:` message.

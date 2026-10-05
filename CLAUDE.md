# GraphQL Schema Change Intelligence POC

## Project Overview

Build a standalone Proof of Concept (POC) called **Veel GraphQL Schema Change Intelligence**.

The purpose of this project is to automatically detect GraphQL schema changes in the backend and inform frontend and mobile developers:

* What changed
* Where it changed
* What the previous schema was
* What the new schema is
* Whether the change is breaking, warning, or informational
* Which frontend operations may be affected
* Which Android operations may be affected
* What action the client developers should take
* Generate a human-readable report
* Generate a machine-readable JSON report
* Optionally send the result to a webhook
* Eventually integrate into GitHub/GitLab CI/CD

The system must **not prevent schema changes by default**.

The main purpose is **visibility and impact analysis**, not simply blocking breaking changes.

---

# 1. Problem Statement

Veel has multiple clients consuming GraphQL APIs:

```text
Android Application
        |
        v
     Janus
        |
        v
     GraphQL
        |
        v
     Cerberus
```

There is also a web/frontend client consuming the same GraphQL schema.

Backend developers sometimes need to modify the GraphQL schema.

For example:

### Previous schema

```graphql
type Campaign {
    id: ID!
    title: String!
    videoUrl: String
}
```

### New schema

```graphql
type Campaign {
    id: ID!
    title: String!
    video: CampaignVideo
}
```

The backend change may be intentional and valid, but existing clients may still contain:

```graphql
query GetCampaign {
    campaign {
        id
        title
        videoUrl
    }
}
```

The problem is that frontend/mobile developers may not know:

* `Campaign.videoUrl` was removed
* `Campaign.video` was added
* Their operation uses `videoUrl`
* They need to migrate to `video`

The goal of this POC is to automate this communication.

---

# 2. Core Objective

The system should implement this flow:

```text
Backend GraphQL Schema
          |
          v
     Schema Diff
          |
          v
   Change Classification
          |
          v
   Client Operation Analysis
          |
          v
     Impact Analysis
          |
          v
     Change Report
          |
          +------------------+
          |                  |
          v                  v
      JSON Report       Human Report
                             |
                  +----------+----------+
                  |                     |
                  v                     v
              PR Comment            Webhook
```

The most important outcome is:

> When a GraphQL schema changes, developers should immediately know what changed and which client operations need attention.

---

# 3. POC Scope

## In Scope

The initial POC must support:

1. Loading an old GraphQL schema
2. Loading a new GraphQL schema
3. Comparing the schemas
4. Detecting schema changes
5. Classifying changes
6. Determining severity
7. Loading GraphQL operations from Android
8. Loading GraphQL operations from frontend
9. Determining affected client operations
10. Generating a Markdown report
11. Generating a JSON report
12. Providing migration/action suggestions
13. Supporting webhook notification as an optional output
14. Providing a CLI interface
15. Unit tests
16. Example/demo schemas and client operations

## Out of Scope for Initial POC

Do NOT initially build:

* A database
* A web dashboard
* Authentication
* User management
* A full notification platform
* Production deployment infrastructure
* Kubernetes integration
* Tempo integration
* Grafana integration
* Automatic code modification
* Automatic client deployment
* Automatic schema rollback

These can be future enhancements.

---

# 4. Technology Stack

Use:

* .NET / C#
* Modern .NET version compatible with the development environment
* GraphQL schema parsing library where appropriate
* GraphQL AST/document parsing for client operations
* xUnit for tests
* JSON for machine-readable output
* Markdown for human-readable output
* HTTP webhook for optional notifications
* GitHub Actions for CI demonstration

Prefer standard .NET libraries and well-maintained open-source packages.

Do not introduce unnecessary frameworks.

The project should remain small and easy to understand.

---

# 5. Repository Structure

Use the following structure:

```text
veel-graphql-schema-intelligence/
│
├── src/
│   ├── Veel.GraphQL.SchemaIntelligence/
│   │   │
│   │   ├── Program.cs
│   │   │
│   │   ├── Schema/
│   │   │   ├── SchemaLoader.cs
│   │   │   ├── GraphQLSchema.cs
│   │   │   └── SchemaDiffer.cs
│   │   │
│   │   ├── Changes/
│   │   │   ├── SchemaChange.cs
│   │   │   ├── ChangeType.cs
│   │   │   └── ChangeClassifier.cs
│   │   │
│   │   ├── Clients/
│   │   │   ├── ClientDefinition.cs
│   │   │   ├── GraphQLOperation.cs
│   │   │   └── OperationAnalyzer.cs
│   │   │
│   │   ├── Impact/
│   │   │   ├── ClientImpact.cs
│   │   │   └── ImpactAnalyzer.cs
│   │   │
│   │   ├── Reporting/
│   │   │   ├── ChangeReport.cs
│   │   │   ├── MarkdownReportGenerator.cs
│   │   │   └── JsonReportGenerator.cs
│   │   │
│   │   └── Notifications/
│   │       ├── INotificationSender.cs
│   │       └── WebhookNotificationSender.cs
│   │
│   └── Veel.GraphQL.SchemaIntelligence.Tests/
│       ├── SchemaDifferTests.cs
│       ├── ChangeClassifierTests.cs
│       ├── OperationAnalyzerTests.cs
│       ├── ImpactAnalyzerTests.cs
│       └── ReportGeneratorTests.cs
│
├── demo/
│   ├── old-schema.graphql
│   ├── new-schema.graphql
│   │
│   ├── android/
│   │   ├── GetCampaign.graphql
│   │   └── GetCampaignDetails.graphql
│   │
│   └── frontend/
│       ├── CampaignDetails.graphql
│       └── CampaignCard.graphql
│
├── reports/
│   ├── example-report.md
│   └── example-report.json
│
├── .github/
│   └── workflows/
│       └── graphql-schema-check.yml
│
├── README.md
├── CLAUDE.md
└── Veel.GraphQL.SchemaIntelligence.sln
```

Do not unnecessarily deviate from this structure.

---

# 6. Example Schema

## Old Schema

`demo/old-schema.graphql`

```graphql
type Campaign {
    id: ID!
    title: String!
    videoUrl: String
}

type Query {
    campaign: Campaign
}
```

## New Schema

`demo/new-schema.graphql`

```graphql
type Campaign {
    id: ID!
    title: String!
    video: CampaignVideo
}

type CampaignVideo {
    id: ID!
    url: String!
}

type Query {
    campaign: Campaign
}
```

This represents:

```text
REMOVED:
Campaign.videoUrl

ADDED:
Campaign.video

ADDED TYPE:
CampaignVideo
```

---

# 7. Example Client Operations

## Android

`demo/android/GetCampaign.graphql`

```graphql
query GetCampaign {
    campaign {
        id
        title
        videoUrl
    }
}
```

`demo/android/GetCampaignDetails.graphql`

```graphql
query GetCampaignDetails {
    campaign {
        id
        title
        videoUrl
    }
}
```

## Frontend

`demo/frontend/CampaignDetails.graphql`

```graphql
query CampaignDetails {
    campaign {
        id
        title
        videoUrl
    }
}
```

`demo/frontend/CampaignCard.graphql`

```graphql
query CampaignCard {
    campaign {
        id
        title
    }
}
```

The analyzer should determine:

```text
Android:
    GetCampaign          affected
    GetCampaignDetails   affected

Frontend:
    CampaignDetails      affected
    CampaignCard         unaffected
```

---

# 8. Schema Change Types

Create a `ChangeType` model/enumeration.

At minimum support:

```text
Added
Removed
Modified
Deprecated
```

More specific classifications should be supported where possible:

```text
TypeAdded
TypeRemoved

FieldAdded
FieldRemoved
FieldTypeChanged
FieldNullabilityChanged

ArgumentAdded
ArgumentRemoved
ArgumentTypeChanged

EnumValueAdded
EnumValueRemoved

InputFieldAdded
InputFieldRemoved
InputFieldTypeChanged
```

The implementation should be extensible so new change types can be added later.

---

# 9. Severity

Each detected change must have a severity.

Use:

```text
Info
Warning
Breaking
```

## Info

Examples:

* New field added
* New type added
* New query added
* New mutation added

Example:

```text
Campaign.description added
```

If no existing client is negatively affected:

```text
Severity: Info
```

## Warning

Examples:

* Field deprecated
* Enum value deprecated
* Migration recommended

Example:

```graphql
videoUrl: String @deprecated(reason: "Use video")
```

## Breaking

Examples:

* Field removed
* Type removed
* Query removed
* Mutation removed
* Field changed incompatibly
* Required argument introduced
* Enum value removed
* Existing field changed to an incompatible type
* Existing field becomes non-null in an incompatible situation

The system must distinguish between "schema changed" and "client is actually affected".

---

# 10. Client Operation Analysis

The analyzer must parse GraphQL operation files.

For each operation, extract:

* Operation type
* Operation name
* Selected fields
* Nested fields
* Arguments
* Variables
* Fragments
* Fragment references

Example:

```graphql
query GetCampaign {
    campaign {
        id
        title
        videoUrl
    }
}
```

Should produce an internal representation similar to:

```json
{
  "client": "Android",
  "operation": "GetCampaign",
  "fields": [
    "Campaign.id",
    "Campaign.title",
    "Campaign.videoUrl"
  ]
}
```

Do not rely only on string matching.

Use GraphQL parsing/AST analysis so nested selections and fragments can be handled correctly.

---

# 11. Client Types

Initially support:

```text
Android
Frontend
```

Represent clients using a model similar to:

```json
{
  "name": "Android",
  "path": "./demo/android"
}
```

The design should allow future clients:

```text
iOS
Backend
Partner API
Other applications
```

without redesigning the analyzer.

---

# 12. Impact Analysis

The impact analyzer receives:

```text
Schema Changes
+
Client Operations
```

and determines which operations are affected.

Example:

```text
Schema Change:

Campaign.videoUrl
REMOVED
```

Operation:

```text
Android/GetCampaign
```

contains:

```text
Campaign.videoUrl
```

Therefore:

```text
Android/GetCampaign
Severity: Breaking
```

Another operation:

```text
Frontend/CampaignCard
```

does not use the field.

Therefore:

```text
Frontend/CampaignCard
Status: Unaffected
```

---

# 13. Impact Model

The result should contain information similar to:

```json
{
  "client": "Android",
  "operation": "GetCampaign",
  "affectedPath": "Campaign.videoUrl",
  "severity": "Breaking",
  "reason": "Field was removed from the GraphQL schema",
  "suggestion": "Replace Campaign.videoUrl with Campaign.video"
}
```

---

# 14. Migration Suggestions

The tool should provide suggestions where possible.

For example:

```text
Removed:
Campaign.videoUrl

Added:
Campaign.video
```

Possible report:

```text
Possible migration:

Campaign.videoUrl
        ↓
Campaign.video

Developer verification required.
```

IMPORTANT:

Do not automatically assume that two fields are renamed.

A GraphQL schema diff normally sees:

```text
REMOVE videoUrl
ADD video
```

The system may suggest a possible rename based on similarity, but it must clearly label it as:

```text
Possible migration
```

and not as a confirmed rename.

---

# 15. Report Model

Create a central `ChangeReport` model.

Example:

```json
{
  "service": "Cerberus",
  "generatedAt": "2026-10-05T10:00:00Z",
  "summary": {
    "breaking": 1,
    "warning": 0,
    "info": 2
  },
  "changes": [],
  "affectedClients": []
}
```

The report should contain:

```text
Service
Generated timestamp
Schema comparison source
Summary
Schema changes
Affected clients
Affected operations
Migration suggestions
```

---

# 16. Markdown Report

Generate:

```text
reports/graphql-change-report.md
```

Example:

```markdown
# GraphQL Schema Change Report

## Service

Cerberus

## Summary

- 🔴 Breaking Changes: 1
- 🟠 Warnings: 0
- 🟢 Informational Changes: 2

---

## 🔴 Breaking Changes

### Removed Field

`Campaign.videoUrl`

Previous type:

`String`

Status:

Removed

---

## 🟢 Added Fields

### Campaign.video

Type:

`CampaignVideo`

---

## 📱 Android Impact

### GetCampaign

Affected field:

`Campaign.videoUrl`

Reason:

The field was removed from the schema.

Recommended action:

Replace `Campaign.videoUrl` with `Campaign.video`.

### GetCampaignDetails

Affected field:

`Campaign.videoUrl`

---

## 🌐 Frontend Impact

### CampaignDetails

Affected field:

`Campaign.videoUrl`

---

## ✅ Unaffected Operations

### Android

- PublishCampaign
- SubscriptionGift

### Frontend

- CampaignCard

---

## Recommendation

The affected Android and frontend operations should be updated before the new schema is consumed by those clients.
```

---

# 17. JSON Report

Also generate:

```text
reports/graphql-change-report.json
```

Example:

```json
{
  "service": "Cerberus",
  "summary": {
    "breaking": 1,
    "warning": 0,
    "info": 2
  },
  "changes": [
    {
      "path": "Campaign.videoUrl",
      "changeType": "Removed",
      "oldType": "String",
      "newType": null,
      "severity": "Breaking"
    },
    {
      "path": "Campaign.video",
      "changeType": "Added",
      "oldType": null,
      "newType": "CampaignVideo",
      "severity": "Info"
    }
  ],
  "impacts": [
    {
      "client": "Android",
      "operation": "GetCampaign",
      "path": "Campaign.videoUrl",
      "severity": "Breaking"
    },
    {
      "client": "Android",
      "operation": "GetCampaignDetails",
      "path": "Campaign.videoUrl",
      "severity": "Breaking"
    },
    {
      "client": "Frontend",
      "operation": "CampaignDetails",
      "path": "Campaign.videoUrl",
      "severity": "Breaking"
    }
  ]
}
```

---

# 18. CLI

The tool should be executable from the command line.

Example:

```bash
dotnet run -- analyze \
  --old-schema ./demo/old-schema.graphql \
  --new-schema ./demo/new-schema.graphql \
  --android ./demo/android \
  --frontend ./demo/frontend
```

Expected output:

```text
GraphQL Schema Intelligence
===========================

Loading old schema...
Loading new schema...

Comparing schemas...

Changes detected:
  Added:     2
  Removed:   1
  Modified:  0
  Deprecated: 0

Analyzing Android operations...
Analyzing frontend operations...

Impact analysis complete.

Breaking changes: 1
Affected Android operations: 2
Affected Frontend operations: 1

Reports generated:

reports/graphql-change-report.md
reports/graphql-change-report.json
```

---

# 19. CLI Options

Support options similar to:

```text
--old-schema
--new-schema

--android
--frontend

--output

--service

--format

--webhook
```

Example:

```bash
dotnet run -- analyze \
  --old-schema ./old.graphql \
  --new-schema ./new.graphql \
  --android ./android \
  --frontend ./frontend \
  --service Cerberus \
  --output ./reports
```

---

# 20. Notification System

Create an abstraction:

```csharp
public interface INotificationSender
{
    Task SendAsync(ChangeReport report);
}
```

The first implementation should be:

```text
WebhookNotificationSender
```

Do not couple the analyzer directly to Slack, ClickUp, Teams, etc.

Architecture:

```text
Analyzer
   |
   v
ChangeReport
   |
   v
INotificationSender
   |
   +---- Webhook
   +---- Future Slack
   +---- Future ClickUp
   +---- Future Teams
```

The report is the source of truth.

---

# 21. Webhook Notification

Webhook payload should be JSON.

Example:

```json
{
  "service": "Cerberus",
  "severity": "Breaking",
  "summary": {
    "breaking": 1,
    "warning": 0,
    "info": 2
  },
  "affectedClients": [
    "Android",
    "Frontend"
  ]
}
```

The notification should summarize the important information rather than sending the entire raw schema.

---

# 22. CI/CD Integration

The eventual CI flow should be:

```text
Developer creates PR
        |
        v
GitHub Actions
        |
        v
Checkout repository
        |
        v
Generate GraphQL schema
        |
        v
Get base/main schema
        |
        v
Run Schema Intelligence
        |
        v
Analyze Android operations
        |
        v
Analyze Frontend operations
        |
        v
Generate reports
        |
        +------------------+
        |                  |
        v                  v
   PR Comment          Webhook
```

---

# 23. CI Behavior

The default behavior should be:

### Informational change

```text
PASS
```

### Warning

```text
PASS + notification
```

### Breaking change affecting clients

```text
PASS + prominent notification
```

Do not fail the PR by default.

Later add configuration:

```yaml
policy:
  failOnBreakingChange: false
```

This allows teams to decide whether breaking changes should block merges.

---

# 24. Future Configuration

Support a configuration file in the future:

```yaml
service: Cerberus

clients:
  android:
    path: ../VeelApp
    graphqlPath: src/main/graphql

  frontend:
    path: ../frontend
    graphqlPath: src/graphql

notifications:
  webhook:
    enabled: true
    url: ${GRAPHQL_CHANGE_WEBHOOK_URL}

policy:
  failOnBreakingChange: false
```

Do not hard-code client paths.

---

# 25. Unit Testing Requirements

The POC must have unit tests.

At minimum test:

## Schema Diff

```text
Field added
Field removed
Field type changed
Nullability changed
Type added
Type removed
Enum value added
Enum value removed
```

## Operation Analysis

```text
Simple query
Nested fields
Fragments
Variables
Mutation
Subscription
Multiple operations
```

## Impact Analysis

```text
Affected operation
Unaffected operation
Multiple affected clients
Multiple affected operations
```

## Reporting

```text
Markdown report generation
JSON report generation
Empty changes
Multiple changes
Breaking changes
Warnings
```

---

# 26. Important Design Principle

Do not mix these responsibilities:

```text
Schema parsing
Schema diffing
Client parsing
Impact analysis
Reporting
Notification
```

They must remain separate.

Preferred architecture:

```text
SchemaLoader
     |
     v
SchemaDiffer
     |
     v
ChangeClassifier
     |
     v
OperationAnalyzer
     |
     v
ImpactAnalyzer
     |
     v
ChangeReport
     |
     +---- MarkdownReportGenerator
     |
     +---- JsonReportGenerator
     |
     +---- WebhookNotificationSender
```

This makes the POC easy to extend.

---

# 27. Error Handling

The CLI must provide clear errors.

Examples:

```text
ERROR: Old schema file does not exist.

ERROR: New schema is invalid GraphQL.

ERROR: Android GraphQL directory does not exist.

ERROR: Failed to parse operation:
GetCampaign.graphql

Line: 8
Column: 12
```

Do not expose raw stack traces during normal CLI execution.

Use verbose logging/debug mode when needed.

---

# 28. Logging

Use structured logging where appropriate.

Example:

```text
INFO  Loading old schema
INFO  Loading new schema
INFO  Comparing schemas
INFO  Found 3 schema changes
INFO  Analyzing Android operations
INFO  Found 2 affected Android operations
INFO  Analyzing frontend operations
INFO  Found 1 affected frontend operation
INFO  Generating reports
INFO  Analysis complete
```

Avoid excessive logs.

---

# 29. Security

Never commit:

```text
Webhook URLs
API keys
GitHub tokens
ClickUp tokens
Slack tokens
```

Use environment variables:

```text
GRAPHQL_CHANGE_WEBHOOK_URL
GITHUB_TOKEN
```

The POC should not log secrets.

---

# 30. Demo Scenario

The POC must include a reproducible demonstration.

## Step 1

Run:

```bash
dotnet run -- analyze \
  --old-schema ./demo/old-schema.graphql \
  --new-schema ./demo/old-schema.graphql \
  --android ./demo/android \
  --frontend ./demo/frontend
```

Expected:

```text
No schema changes detected.
```

## Step 2

Change `new-schema.graphql`:

```graphql
type Campaign {
    id: ID!
    title: String!
    video: CampaignVideo
}
```

## Step 3

Run analysis again.

Expected:

```text
Breaking changes: 1

Android:
  GetCampaign
  GetCampaignDetails

Frontend:
  CampaignDetails
```

## Step 4

Open:

```text
reports/graphql-change-report.md
```

## Step 5

Show JSON:

```text
reports/graphql-change-report.json
```

## Step 6

Run webhook notification.

Expected:

```text
GraphQL Schema Change Detected

Cerberus

Campaign.videoUrl was removed.

Affected:
Android:
  GetCampaign
  GetCampaignDetails

Frontend:
  CampaignDetails
```

---

# 31. Integration With Veel Cerberus

After the standalone POC works, integrate it with:

```text
VeelBackend/cerberus
```

The integration should:

1. Generate/export the Cerberus GraphQL schema
2. Compare it against the previous/main schema
3. Run schema intelligence
4. Analyze Android GraphQL operations
5. Analyze frontend GraphQL operations
6. Generate report
7. Publish report to the PR
8. Send notification

Do not modify Cerberus business logic.

The POC should operate externally.

---

# 32. Future OpenTelemetry Integration

This is NOT required for the initial POC.

However, the architecture should leave room for runtime usage information.

Current Veel observability architecture includes:

```text
VeelApp.Android
        |
        v
Veel.Janus
        |
        v
GraphQL
        |
        v
Cerberus
        |
        v
OpenTelemetry
        |
        v
Tempo
```

Future runtime information could tell the system:

```text
Operation:
GetCampaign

Client:
VeelApp.Android

Field:
Campaign.videoUrl

Last seen:
5 minutes ago
```

Then the report can become:

```text
Removed:
Campaign.videoUrl

Static consumers:
Android/GetCampaign
Frontend/CampaignDetails

Runtime consumers:
Android/GetCampaign
Frontend/CampaignDetails

⚠️ Active clients are still using this field.
```

This should be considered a future enhancement.

---

# 33. Future Dashboard

A future dashboard could show:

```text
GraphQL Schema Health
=====================

Cerberus

Schema changes this month: 17

Breaking changes: 3
Warnings: 5
Informational: 9

Affected clients:

Android       4
Frontend      3
iOS           0

Most frequently changed areas:

Campaign
User
Payment
Subscription
```

This is outside the POC.

---

# 34. Future Features

Potential future versions:

### Version 1

```text
Schema diff
Client impact
Markdown
JSON
CLI
```

### Version 2

```text
GitHub PR integration
Webhook notifications
CI/CD
```

### Version 3

```text
Runtime OpenTelemetry usage
Tempo integration
```

### Version 4

```text
Dashboard
Historical schema versions
Change history
Client adoption tracking
```

### Version 5

```text
Migration suggestions
Automated compatibility recommendations
```

---

# 35. Non-Goals

The system is NOT:

* A replacement for GraphQL
* A GraphQL gateway
* A GraphQL server
* A client code generator
* A deployment system
* A monitoring system
* A tracing system
* A replacement for Apollo
* A replacement for HotChocolate

It is a:

> **GraphQL schema change impact and communication tool.**

---

# 36. Definition of Done

The POC is complete when the following scenario works:

```text
1. Old GraphQL schema exists.
2. New GraphQL schema exists.
3. Tool detects differences.
4. Tool classifies differences.
5. Tool assigns severity.
6. Tool reads Android operations.
7. Tool reads frontend operations.
8. Tool identifies affected operations.
9. Tool identifies unaffected operations.
10. Tool generates Markdown report.
11. Tool generates JSON report.
12. Tool provides migration suggestions where possible.
13. Tool can send a webhook notification.
14. Unit tests pass.
15. CLI works with documented commands.
16. Demo can be executed by another developer.
```

---

# 37. Expected Final User Experience

A backend developer changes:

```graphql
Campaign.videoUrl
```

to:

```graphql
Campaign.video
```

They do NOT need to manually message:

> "Hey frontend/mobile, I changed the schema."

Instead:

```text
PR
 |
 v
Schema Intelligence
 |
 v
Detects change
 |
 v
Finds affected operations
 |
 +----------------------+
 |                      |
 v                      v
Android              Frontend
GetCampaign          CampaignDetails
 |
 v
Report + Notification
```

Developers receive:

```text
🚨 GraphQL Schema Change

Campaign.videoUrl
        ↓
removed

Replacement:
Campaign.video

Affected Android:
- GetCampaign
- GetCampaignDetails

Affected Frontend:
- CampaignDetails

Action Required:
Update client operations before consuming the new schema.
```

The backend team can still make the schema change.

The system simply makes the impact **visible, traceable, and actionable**.

---

# 38. Implementation Order

Implement in this exact order:

```text
Phase 1
Project setup
        ↓
Phase 2
Schema loader
        ↓
Phase 3
Schema diff engine
        ↓
Phase 4
Change classification
        ↓
Phase 5
GraphQL operation parser
        ↓
Phase 6
Client impact analyzer
        ↓
Phase 7
Markdown report
        ↓
Phase 8
JSON report
        ↓
Phase 9
Webhook notification
        ↓
Phase 10
Unit tests
        ↓
Phase 11
GitHub Actions
        ↓
Phase 12
Cerberus integration
```

Do not jump directly to GitHub Actions or notifications before the core analyzer works.

---

# 39. Development Guidelines for Claude

When implementing this project:

1. Work incrementally.
2. Do not implement the entire system in one step.
3. Complete and test one phase before moving to the next.
4. Prefer simple, maintainable C#.
5. Avoid unnecessary abstractions.
6. Keep domain logic independent from CLI and notification code.
7. Write unit tests for important logic.
8. Do not hard-code Veel production paths.
9. Do not add production credentials.
10. Do not modify Cerberus initially.
11. Use the `demo/` directory to prove functionality before real Veel integration.
12. Keep reports deterministic so tests are reliable.
13. Provide useful CLI error messages.
14. Preserve backward compatibility in the report JSON structure where practical.
15. Do not assume that an added and removed field are a rename.
16. Label rename/migration detection as a suggestion.
17. Never automatically modify client source code.
18. Do not automatically block schema changes.
19. The primary goal is impact visibility and developer communication.
20. Keep the architecture extensible for future OpenTelemetry/runtime integration.

---

# 40. First Implementation Task

Start with **Phase 1 only**.

Create:

```text
Veel.GraphQL.SchemaIntelligence.sln
```

with:

```text
src/
├── Veel.GraphQL.SchemaIntelligence/
└── Veel.GraphQL.SchemaIntelligence.Tests/
```

Then create:

```text
demo/
├── old-schema.graphql
├── new-schema.graphql
├── android/
└── frontend/
```

Do not implement notifications, GitHub Actions, Tempo, Grafana, or production Cerberus integration yet.

After the project builds successfully, proceed to Phase 2: **Schema Loading**.

At every phase:

1. Implement the smallest working version.
2. Build the project.
3. Run tests.
4. Demonstrate the result.
5. Only then move to the next phase.

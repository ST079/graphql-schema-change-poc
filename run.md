# How to Run the Demo

This guide walks through the GraphQL Schema Change Intelligence POC step by step. It takes about 10 minutes.

**The story:** a backend developer replaces `Campaign.videoUrl` with a new `Campaign.video` field. The tool works out what changed, which Android and frontend operations break, and what the client developers should do, without anyone writing a Slack message.

---

## 0. Prerequisites

- [.NET SDK 10.0](https://dotnet.microsoft.com/download) or later. Check with `dotnet --version`.
- Python 3, only for the optional webhook demo in step 7.

All commands below are run from the repository root. The expected outputs assume the **original** demo files. If you've edited `demo/`, restore it first (see the end of step 8).

```bash
git clone https://github.com/ST079/graphql-schema-change-poc.git
cd graphql-schema-change-poc
```

---

## 1. Build and test

```bash
dotnet build
dotnet test
```

Expected:

```text
Build succeeded.
Passed!  - Failed: 0, Passed: 253, ...
```

---

## 2. Look at the demo inputs

```text
demo/
├── old-schema.graphql      # the schema clients use today
├── new-schema.graphql      # the schema after the backend change
├── android/                # Android client operations
│   ├── GetCampaign.graphql
│   └── GetCampaignDetails.graphql
└── frontend/               # Web client operations
    ├── CampaignCard.graphql
    ├── CampaignDetails.graphql
    └── GetUser.graphql
```

The backend change:

```graphql
# old-schema.graphql               # new-schema.graphql
type Campaign {                    type Campaign {
    id: ID!                            id: ID!
    title: String!                     title: String!
    videoUrl: String       ──►         video: CampaignVideo
}                                  }
                                   type CampaignVideo { id: ID!  url: String! }
```

Some operations still request `videoUrl`. For example, `demo/android/GetCampaign.graphql`:

```graphql
query GetCampaign {
    campaign {
        id
        title
        videoUrl
    }
}
```

`CampaignCard` and `GetUser` don't use `videoUrl`, so they should **not** be flagged.

---

## 3. Baseline: no changes

Compare the old schema with itself:

```bash
dotnet run --project src/Veel.GraphQL.SchemaIntelligence -- analyze \
  --old-schema ./demo/old-schema.graphql \
  --new-schema ./demo/old-schema.graphql \
  --android ./demo/android \
  --frontend ./demo/frontend \
  --service Cerberus
```

Expected:

```text
No schema changes detected.
```

---

## 4. Run the real analysis

```bash
dotnet run --project src/Veel.GraphQL.SchemaIntelligence -- analyze \
  --old-schema ./demo/old-schema.graphql \
  --new-schema ./demo/new-schema.graphql \
  --android ./demo/android \
  --frontend ./demo/frontend \
  --service Cerberus
```

Expected (shortened):

```text
FieldRemoved
  Severity: Breaking
  Type: Campaign
  Field: videoUrl
  Old Type: String
...
3 change(s) detected.
  Breaking: 1
  Warning:  0
  Info:     2

Client Impact
=============

BREAKING  FieldRemoved  Campaign.videoUrl
  Android:
    - GetCampaign  (campaign.videoUrl)
    - GetCampaignDetails  (campaign.videoUrl)
  Frontend:
    - CampaignDetails  (campaign.videoUrl)
  Action: Update affected client operations to stop requesting Campaign.videoUrl.
  Possible migration candidate (developer verification required): Campaign.videoUrl → Campaign.video

INFO  FieldAdded  Campaign.video
  No affected client operations detected.
...
Breaking changes: 1
Affected Android operations: 2
Affected Frontend operations: 1

Reports generated:

reports/schema-change-report.md
reports/schema-change-report.json
```

**Talking points:**

- Only the 3 operations that actually select `videoUrl` are flagged. `CampaignCard` and `GetUser` are not.
- `videoUrl` → `video` is shown as a *possible* migration that a developer must verify. It is never claimed to be a rename.
- The run still **exits 0**. The tool informs; it never blocks a schema change.

---

## 5. Open the Markdown report

Open `reports/schema-change-report.md`. It renders nicely in VS Code's Markdown preview (`Cmd+Shift+V`) or on GitHub. It contains:

1. A summary table (3 changes · 1 breaking · 2 clients · 3 operations)
2. 🔴 Breaking Changes, with affected clients, the recommended action and the possible migration
3. 🟢 Informational Changes
4. Client Impact, per client
5. ✅ Unaffected Operations
6. A table of all changes

This is the text a PR comment or a notification would contain.

---

## 6. Show the JSON report

```bash
cat reports/schema-change-report.json
```

This is the machine-readable version of the same report, for CI, dashboards and other tools. Key parts:

```json
"summary": { "totalChanges": 3, "breakingChanges": 1, "warningChanges": 0, "infoChanges": 2,
             "affectedClients": 2, "affectedOperations": 3, ... },
"changes": [ { "changeType": "FieldRemoved", "severity": "Breaking", "path": "Campaign.videoUrl",
               "oldType": "String", "newType": null, ... } ],
"clientImpacts": [ { "clientName": "Android", "affectedOperations": ["GetCampaign", "GetCampaignDetails"], ... } ]
```

---

## 7. Webhook notification (optional)

The tool can POST the JSON report to any HTTP endpoint. To demo it, start a tiny local receiver in **terminal 1**:

```bash
python3 -c '
import http.server, json
class H(http.server.BaseHTTPRequestHandler):
    def do_POST(self):
        body = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
        print("Received", self.headers["X-Schema-Intelligence-Event"], "for", body["serviceName"], "-", body["summary"]["breakingChanges"], "breaking change(s)", flush=True)
        self.send_response(200); self.end_headers()
    def log_message(self, *args): pass
http.server.HTTPServer(("127.0.0.1", 8080), H).serve_forever()'
```

In **terminal 2**, run the analysis with `--webhook`:

```bash
dotnet run --project src/Veel.GraphQL.SchemaIntelligence -- analyze \
  --old-schema ./demo/old-schema.graphql \
  --new-schema ./demo/new-schema.graphql \
  --android ./demo/android \
  --frontend ./demo/frontend \
  --service Cerberus \
  --webhook http://127.0.0.1:8080/schema-changes
```

Terminal 2 ends with `Webhook notification sent.`. Terminal 1 prints:

```text
Received graphql.schema.changed for Cerberus - 1 breaking change(s)
```

Press `Ctrl+C` in terminal 1 to stop the receiver. The webhook is generic, so ClickUp, Slack, n8n or an internal service can consume the same payload. See the README for configuration through environment variables.

---

## 8. Try your own change: a deprecation (Warning)

Make a copy of the new schema in which `Campaign.title` is deprecated:

```bash
sed 's/title: String!/title: String! @deprecated(reason: "Use name")/' demo/new-schema.graphql > /tmp/deprecated-schema.graphql

dotnet run --project src/Veel.GraphQL.SchemaIntelligence -- analyze \
  --old-schema ./demo/new-schema.graphql \
  --new-schema /tmp/deprecated-schema.graphql \
  --android ./demo/android \
  --frontend ./demo/frontend \
  --service Cerberus
```

Expected:

```text
WARNING  DeprecationAdded  Campaign.title
  Android:
    - GetCampaign  (campaign.title)
    - GetCampaignDetails  (campaign.title)
  Frontend:
    - CampaignCard  (campaign.title)
    - CampaignDetails  (campaign.title)
  Action: Plan migration away from Campaign.title before it is removed. Deprecation reason: Use name

Breaking changes: 0
```

This time `CampaignCard` *is* listed, because it selects `title`. Feel free to edit the files under `demo/` to try other changes, such as removing a type, removing an enum value, or adding a required argument. The tests use their own copy, so they won't break. To restore the original demo files:

```bash
cp -R src/Veel.GraphQL.SchemaIntelligence.Tests/Fixtures/* demo/
```

---

## 9. CI demo on GitHub (optional)

The workflow `.github/workflows/graphql-schema-intelligence.yml` runs on every pull request that touches GraphQL files or code.

1. Create a branch and change the schema, for example by removing `title` from `demo/new-schema.graphql`:

   ```bash
   git checkout -b demo/remove-title
   # edit demo/new-schema.graphql: delete the line "    title: String!" from type Campaign
   git commit -am "Demo: remove Campaign.title"
   git push -u origin demo/remove-title
   ```

2. Open a pull request into `main`.
3. In the PR, open **Checks → GraphQL Schema Intelligence**. You will see:
   - **Job summary:** the full Markdown report, showing `Campaign.title` removed as Breaking and the affected operations
   - **Annotation:** `⚠ 1 breaking schema change(s) detected`
   - **Artifact:** `graphql-schema-change-report`, containing both reports
   - **Status:** ✅ passing. Breaking changes are reported but don't fail the build.

---

## Command reference

```text
analyze --old-schema <path> --new-schema <path>
        [--android <dir>] [--frontend <dir>]
        [--service <name>]                  default: GraphQL API
        [--output <dir>]                    default: reports
        [--format markdown|json|all]        default: all
        [--config <file>] [--webhook <url>]
```

| Exit code | Meaning |
|---|---|
| `0` | Analysis completed. Breaking changes may still have been found; read the report. |
| `1` | Analysis could not run: invalid schema or operation, missing file, bad option, or webhook failure. A single `ERROR:` line explains why. |

Try an error case:

```bash
dotnet run --project src/Veel.GraphQL.SchemaIntelligence -- analyze \
  --old-schema ./missing.graphql \
  --new-schema ./demo/new-schema.graphql
# ERROR: Schema file does not exist: ./missing.graphql
```

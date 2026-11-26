# Lead CRM

A small ASP.NET Core 8 lead-management app with a browser interface and JSON-file persistence. It uses PdfPig for local PDF structure validation and Google Gemini for PDF OCR and full-name comparison; other storage is file-based and requires no database server.

## Run

From this directory:

```powershell
dotnet run --urls http://localhost:5092
```

Open <http://localhost:5092>. Add an optional PDF, JPG, or PNG document while creating a lead. Lead data is stored in `App_Data\leads.json`.
Uploaded documents are stored outside the public web root in `App_Data\documents` and are limited to PDF, JPG, and PNG files up to 10 MB. PdfPig checks PDF structure and page count locally; structurally invalid PDFs are rejected. PDFs of up to 10 pages are sent directly to the Google Gemini Interactions API for OCR and comparison against the lead's first and last name. The upload UI asks for confirmation before sending a PDF. A full-name match automatically marks the PDF Verified; mismatches, encrypted PDFs, and PDFs over the page limit remain Pending for human review. JPG and PNG uploads remain Pending for human review. Do not upload documents to Google Gemini unless external processing is authorized.
The project pins a PdfPig prerelease because a stable package version was not available when the dependency was selected.

## Automated tests

Run a selected CRM integration suite from the project directory:

```sh
dotnet test FunctionalTestCases/LeadCrm.Tests.csproj -p:TestSuite=Quick
dotnet test FunctionalTestCases/LeadCrm.Tests.csproj -p:TestSuite=Sanity
dotnet test FunctionalTestCases/LeadCrm.Tests.csproj -p:TestSuite=Regression
```

These are functional integration tests. `Quick` runs the first two smoke tests, `Sanity` runs those plus core lead, agent, and image workflow tests, and `Regression` runs the full suite. Running `dotnet test FunctionalTestCases/LeadCrm.Tests.csproj` without a flag defaults to `Regression`. GitHub Actions runs `Quick` on every push and pull request; use the workflow's manual **Run workflow** option to choose a different suite. The suite uses isolated temporary storage and a fake Gemini HTTP response, so it does not call Gemini or require an API key. See [FunctionalTestCases/CRM-Test-Cases.md](./FunctionalTestCases/CRM-Test-Cases.md) for case coverage and UI-only cases that still require manual testing.

## Unit tests

Pure agent logic has a separate unit-test project:

```sh
dotnet test UnitTestCases/LeadCrm.UnitTests.csproj
```

These tests exercise lead-readiness and document-review findings directly, without hosting the web app, accessing storage, or calling Gemini.

## Change gate

Before editing files for a change, run the unit tests and Quick functional suite against the current code. If either baseline fails, stop and report the failure before editing. After making the change, run both suites again. GitHub Actions repeats these checks on pushes and pull requests. Configure the repository's branch protection to require the **CRM tests** check before merging; this repository-level setting cannot be enabled from the workflow file.

```sh
dotnet test UnitTestCases/LeadCrm.UnitTests.csproj
dotnet test FunctionalTestCases/LeadCrm.Tests.csproj -p:TestSuite=Quick
```

Set the key locally with ASP.NET Core User Secrets before uploading PDFs:

```sh
dotnet user-secrets set GOOGLE_GEMINI_API_KEY "your-gemini-api-key" --project LeadCrm.csproj
```

The secret is not stored in the codebase. The model defaults to `gemini-3.6-flash` and can be changed in `AgentRuntime:PdfValidation:GoogleGemini:Model`. PDF OCR uses Google's Interactions API with interaction storage disabled. If the key is missing or Google Gemini cannot validate a PDF, the upload returns an error and does not mark the file Verified.

## API

- `GET /api/leads?search=...&status=...` — list and filter leads
- `GET /api/leads/{id}` — retrieve one lead
- `POST /api/leads` — create a lead (`firstName`, `lastName`, and `email` are required); if the email already exists (case-insensitive), update that lead instead
- `PUT /api/leads/{id}/status` — change status to `New`, `Contacted`, `Qualified`, `Converted`, or `Lost`
- `POST /api/leads/{id}/documents` — submit a multipart `file` for a lead; PDFs with a full-name match are automatically marked `Verified`, while other accepted uploads start as `Pending`
- `GET /api/leads/{id}/documents/{documentId}/file` — download an uploaded document
- `PUT /api/leads/{id}/documents/{documentId}/review` — set `status` to `Pending`, `Verified`, or `Rejected`, with an optional review `note`
- `GET /api/agents` — list local agent tasks
- `POST /api/agents/run` — run an advisory task with `{ "leadId": "<guid>", "task": "lead-assessment" }`; tasks are `lead-assessment` and `document-review`
- PDF uploads automatically run the `pdf-validation` agent through the orchestrator before storage

New leads return `201 Created`. The form asks for confirmation before updating a lead with a matching email. Confirmed updates return `200 OK`; the lead's ID, status, and original creation date are preserved.

This is a local MVP. Add authentication, access controls, and a production database before exposing it to a network or using it for sensitive customer data.
The current app does not authenticate reviewers or scan uploads for malware. A full-name match is only a basic content check, not identity or authenticity verification.

## Agentic runtime

The app has an in-process orchestrator, advisory local agents, and an AI-assisted PDF upload workflow. Agent implementation and runtime wiring are grouped under [Agentic/](./Agentic/). The CRM screen does not expose agent controls. Agent workflows and PDF parsing limits are loaded from `AgentRuntime` in [appsettings.json](./appsettings.json). The advisory API uses `local-rules`; the PDF verification specialist sends the PDF and lead's full name to Google Gemini for OCR and comparison. A full-name match automatically marks the document Verified; this is a basic content check, not identity verification. PDF contents are not logged. `rules.yaml` is a policy reference, not a dynamically loaded policy engine. The following files describe the runtime and its guardrails:

- [agents.md](./agents.md) — agent roles and orchestration boundaries
- [rules.yaml](./rules.yaml) — machine-readable autonomy, approval, and data-handling rules
- [SOP.md](./SOP.md) — operating procedures and failure handling
- [skills.md](./skills.md) — CRM-specific agent skill definitions

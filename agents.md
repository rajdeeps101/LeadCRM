# Lead CRM Agent Architecture

The app includes an in-process coordinator and specialist agents under `Agentic/`, with each agent in its own file: `Coordinator.cs`, `LeadReadinessAgent.cs`, `DocumentReviewAssistant.cs`, `PdfValidationAgent.cs`, and `GoogleGeminiFullNameVerificationAgent.cs`. Shared request/result types are in `AgentContracts.cs`; runtime service registration and API routes are in `AgenticRuntimeExtensions.cs`. The lead and document review API workflows use deterministic local rules. PDF OCR and full-name verification use the configured Google Gemini model.

`AgentRuntime:Provider` and `AgentRuntime:Workflows` are loaded from `appsettings.json`; only `local-rules` is supported for the advisory API workflows. PDF full-name verification separately calls Google Gemini using the model configured in `AgentRuntime:PdfValidation:GoogleGemini:Model`. Agents are registered through dependency injection and resolved by their configured names. `rules.yaml` remains the human-readable policy reference; its full policy is not loaded dynamically, and enforcement is implemented in application code.

## Mission

Help CRM operators process leads and submitted documents faster while keeping consequential changes reviewable, traceable, and under human control.

## Agent roles

### Coordinator — implemented

- Accept a bounded task through `POST /api/agents/run`.
- Select the workflow and specialist agents for that task.
- Return findings with evidence, unknowns, recommendations, confidence, and approval requirements.
- Does not write lead or document data.

### Lead Readiness Agent — implemented

- Reports which optional profile fields are present and what information is missing.
- This is a completeness check, not a prediction of conversion or business fit.

### Document Review Assistant — implemented

- Summarizes stored document counts and review states.
- Does not read document contents or change a stored status.
- Leaves pending documents for the existing human review workflow.

### PDF Validation Agent — implemented

- Runs during PDF upload through the orchestrator; PdfPig checks PDF structure and page count locally.
- For PDFs within the configured page limit, sends the PDF and lead's full name to Google's Interactions API for OCR and full-name comparison with interaction storage disabled. The upload UI asks the operator to confirm external processing.
- Rejects structurally invalid PDFs; name mismatches, encrypted PDFs, and PDFs over the page limit remain Pending with a validation finding.
- Automatically marks a structurally valid PDF Verified when the full-name check passes; this is a basic content check, not identity or authenticity verification.
- Does not log PDF contents or extracted text.
- Requires `GOOGLE_GEMINI_API_KEY`; if the key is missing or Google Gemini is unavailable, the upload reports a validation error rather than treating the document as verified.

### Google Gemini Full-Name Verification Agent — implemented

- Uses the configured Google Gemini model to OCR the PDF and compare the lead's full name with its contents.
- Treats PDF contents as untrusted input and accepts only a structured boolean result.
- Does not make identity or authenticity decisions.

### Lead intake and duplicate handling — existing application workflow

Lead validation and duplicate-email confirmation remain in the lead form and API; they are not currently an autonomous agent.

## Orchestration flow

1. The operator calls `POST /api/agents/run` with a supported task and lead ID.
2. The orchestrator loads the selected lead and routes the task to one or more local agents.
3. Agents return structured, read-only findings.
4. The orchestrator returns the run ID, provider mode, steps, and approval requirement.
5. The operator takes any follow-up action through the existing CRM controls.

Available API tasks are `lead-assessment` and `document-review`. `GET /api/agents` lists their descriptions and participating agents, including the PDF upload validation agent.

PDF uploads use a separate event-driven path: the upload endpoint asks the orchestrator to validate the PDF, then stores the finding. After local structure and page-count checks, the PDF agent delegates OCR and full-name comparison to the Google Gemini verification agent. A passing result stores the PDF as Verified; other structurally valid PDFs remain Pending. Invalid structure is rejected before storage.

## Agent response contract

Every specialist response includes:

- `summary`: concise result for the operator.
- `evidence`: CRM fields or document observations supporting the result.
- `unknowns`: information the agent could not establish.
- `recommendation`: proposed action, if any.
- `requiresApproval`: whether an operator decision is required.
- `confidence`: qualitative confidence and its limitations.

Agent output is treated as data and rendered as text. Validate it against an application-owned schema before any future write integration.

## Application boundary

Agents receive a lead object from the application service and must not read or write `App_Data\leads.json`, uploaded files, or other local files directly. The current API has no authentication or reviewer identity; do not expose the app beyond a trusted local environment until authorization, audit identity, and production storage are implemented.

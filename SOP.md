# Lead CRM Agent Operating Procedure

> **Runtime note:** Agent API workflows use local deterministic agents. PDF validation checks structure and page count locally, then sends PDFs up to the configured limit and the lead's full name to Google Gemini through the Interactions API, with interaction storage disabled. The upload UI asks for confirmation. Set `GOOGLE_GEMINI_API_KEY` in the app process environment or ASP.NET Core User Secrets.
> `AgentRuntime:Workflows` and the `local-rules` mode are loaded from `appsettings.json`. `rules.yaml` remains a policy reference and is not dynamically loaded.

## 1. Start a task

1. Identify the operator's requested outcome and the relevant lead.
2. Choose `lead-assessment` or `document-review`.
3. Submit the task to `POST /api/agents/run` with the lead ID and task.
4. Review the structured findings, evidence, unknowns, and approval requirement.
5. Use existing CRM controls for any follow-up; agents do not write records.

## 2. Intake a lead

1. Validate required fields and normalize whitespace.
2. Search for an exact, case-insensitive email match.
3. If a match exists, show the existing lead and the fields that would change.
4. Request explicit operator confirmation before updating it. On rejection, make no changes.
5. On approval, update the existing record without changing its ID, status, or creation time.
6. Read the record back and verify the saved fields.

## 3. Qualify a lead

1. The lead-readiness agent reports optional profile fields present or missing.
2. Treat this as completeness guidance, not a sales score or qualification decision.
3. Use business criteria and operator judgment for any qualification decision.

## 4. Review a document

1. On PDF upload, the PDF-validation agent checks structure and page count locally. After operator confirmation, it sends the PDF and lead's full name to Google Gemini for OCR and comparison.
2. Structurally invalid PDFs are rejected before storage.
3. If Google Gemini confirms the full name, the PDF is automatically stored as Verified without human intervention.
4. Name mismatches, encrypted PDFs, and PDFs over the configured page limit are stored as Pending with a validation finding for human review.
5. If the API key is missing or Google Gemini is unavailable, the upload returns an explicit error and is not marked Verified. A full-name match is not identity or authenticity verification. JPG and PNG uploads remain Pending for human review.

## 5. Handle failures

- On validation, authorization, provider, or persistence errors, report the error; do not claim success.
- Retry transient Google Gemini HTTP 503 responses at most twice; rate-limit/quota errors are not retried automatically.
- Retry other operations only when idempotent and within the limit configured in `rules.yaml`.
- If an update may have partially succeeded, read the current record before retrying.
- Escalate conflicting records, ambiguous identity, suspected malicious files, or policy conflicts to a human.

## 6. Audit and privacy

Record task ID, agent/skill, lead or document ID, proposed action, approval decision and actor, outcome, and timestamps. Never log secrets or PDF/OCR contents. Follow the organization's retention policy for prompts, outputs, and audit records.

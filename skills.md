# CRM Agent Skills

> The coordinator exposes two local advisory task workflows. The PDF upload-validation workflow sends PDFs within the configured page limit and the lead's full name to Google Gemini through the Interactions API for OCR and comparison.

## `lead-intake`

**Use when:** creating a lead or handling a possible duplicate.

**Inputs:** proposed lead fields; existing lead results for the supplied email.

**Procedure:**

1. Validate required fields and normalize whitespace.
2. Compare email addresses case-insensitively.
3. If a duplicate exists, summarize the existing lead and proposed changes.
4. Ask the operator before updating; cancellation means no write.
5. After approval, update through the application service and verify the returned record.

**Output:** validation result, duplicate match (if any), proposed changes, approval state, and final result.

## `lead-assessment` — orchestrated

**Use when:** an operator calls the `lead-assessment` task through the agent API.

**Agents:** `lead-readiness`, then `document-review-assistant`.

**Procedure:** report optional lead profile fields present or missing and summarize document review states. Do not score sales likelihood, inspect file content, or mutate CRM data.

**Output:** structured summaries, evidence, unknowns, recommendations, confidence, and approval requirement.

## `document-review` — orchestrated

**Use when:** an operator requests help reviewing a lead document.

**Procedure:** summarize document metadata and saved review states only. The local agent does not open or analyze the file.

**Guardrail:** never change document status. Human verification remains authoritative.

## `pdf-validation` — upload workflow

**Use when:** a PDF is submitted for a lead.

**Procedure:** check PDF structure and page count locally, then ask for operator confirmation before sending a PDF within the configured page limit and the lead's full name to Google Gemini for OCR and comparison.

**Outcome:** reject structurally invalid PDFs; automatically mark PDFs Verified when Google Gemini confirms the lead's full name; keep mismatches, encrypted PDFs, PDFs over the page limit, and Google Gemini failures unverified or Pending with an explicit finding/error.

**Guardrail:** full-name matching is a basic content check, not identity or authenticity verification. Send PDF contents only when external processing is authorized; do not log OCR text or persist it in lead data.

## `lead-intake` — existing application workflow

**Use when:** a lead is submitted through the lead form.

**Procedure:** validate required fields, find a case-insensitive email match, and ask the operator before updating an existing lead.

**Guardrail:** cancellation means no update. Preserve the existing lead ID, status, and creation time.

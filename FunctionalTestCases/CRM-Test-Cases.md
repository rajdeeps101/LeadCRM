# Lead CRM Test Cases

All automated cases in this list are functional test cases, tagged `TestType=Functional` in the test runner. Choose a functional suite from the project directory:

```sh
dotnet test FunctionalTestCases/LeadCrm.Tests.csproj -p:TestSuite=Quick
dotnet test FunctionalTestCases/LeadCrm.Tests.csproj -p:TestSuite=Sanity
dotnet test FunctionalTestCases/LeadCrm.Tests.csproj -p:TestSuite=Regression
```

`Quick` runs CRM-001 and CRM-002. `Sanity` runs Quick plus the core lead, agent, and image workflows. `Regression` runs every automated test and is the default when no `TestSuite` is specified. GitHub Actions runs Quick on every push and pull request; choose Quick, Sanity, or Regression from **Actions → CRM tests → Run workflow** to run a different suite manually.

The suite uses isolated temporary storage and a fake Gemini HTTP response, so it does not send PDFs to Google, need an API key, or incur provider charges. Cases marked **Manual** involve browser confirmation handling or encrypted PDFs and are not currently automated.

## Test setup

- Run the app locally at `http://localhost:5092`.
- Use disposable test leads and documents; do not use real customer data.
- For PDF OCR cases, configure `GOOGLE_GEMINI_API_KEY` through ASP.NET Core User Secrets or the process environment.
- PDF validation sends the PDF and lead's full name to Google Gemini after the UI confirmation. Use only synthetic or externally authorized PDFs.
- The PDF validation limit is 10 pages. JPG and PNG uploads do not use Gemini validation.

## Cases

| ID | Area | Steps | Expected result | Execution |
|---|---|---|---|---|
| CRM-001 | Page load | Open the CRM root URL. | The CRM page loads successfully. | Automated |
| CRM-002 | Create lead | Submit a unique valid first name, last name, and email, with optional fields blank. | The lead is created with status `New` and can be retrieved. | Automated |
| CRM-003 | Required fields | Submit the API with required fields missing or an invalid email. | Validation is returned; no lead is created. | Automated |
| CRM-004 | Duplicate email | Create a lead, change its status, then submit an update using the same email with different casing. | Existing fields update; ID, status, and creation date are preserved. | Automated |
| CRM-005 | Decline duplicate update | Submit a lead with an existing email and cancel the browser confirmation. | No changes are made to the existing lead. | Manual |
| CRM-006 | Search and status filter | Search using a lead company, then filter by status. | Only matching leads are returned. | Automated |
| CRM-007 | Change lead status | Change a lead to each supported status: `New`, `Contacted`, `Qualified`, `Converted`, `Lost`. | Each supported status is saved. | Automated |
| CRM-008 | Agent task listing | Request `GET /api/agents`. | Configured advisory tasks and the PDF validation agent are listed. | Automated |
| CRM-009 | Lead assessment | Send `POST /api/agents/run` with a valid lead ID and task `lead-assessment`. | Structured findings return; no CRM data is changed. | Automated |
| CRM-010 | Invalid agent task/lead | Submit an unsupported task, then a supported task with a nonexistent lead ID. | Unsupported task returns validation errors; nonexistent lead returns `404`. | Automated |
| CRM-011 | Upload image | Upload a PNG with a valid signature. | The document is saved as `Pending`; Gemini is not called. | Automated |
| CRM-012 | Reject unsupported or oversized upload | Attempt unsupported type, mismatched extension/signature, and file over 10 MB. | Each upload is rejected and not saved. | Automated |
| CRM-013 | Upload matching PDF | Upload a synthetic PDF containing the lead's full name. | The fake Gemini response is interpreted as a full-name match and the PDF is saved `Verified`. | Automated |
| CRM-014 | Upload non-matching PDF | Upload a valid PDF while the fake Gemini response reports no full-name match. | The PDF is saved as `Pending` with a mismatch finding. | Automated |
| CRM-015 | Decline PDF processing | Select a PDF and cancel the browser confirmation. | The PDF is not sent or uploaded; a new lead form is not submitted. | Manual |
| CRM-016 | PDF page limit | Upload a valid PDF with more than 10 pages. | Gemini is not called; the document remains `Pending` with a page-limit finding. | Automated |
| CRM-017 | Invalid/encrypted PDF | Upload a malformed PDF, then an encrypted PDF. | Malformed structure is rejected before storage. Encrypted PDF handling remains a manual case. | Partial |
| CRM-018 | Gemini unavailable | Simulate a temporary Gemini `503` while uploading a synthetic PDF. | The app retries at most twice, then fails closed without verifying the document. | Automated |
| CRM-019 | Gemini key unavailable | Run the PDF upload test with no Gemini key configured. | Validation returns unavailable; the document is not marked `Verified`. | Automated |
| CRM-020 | Review document | Upload an image, then change its status and note through the review API. | Status and note are saved. | Automated |
| CRM-021 | Download document | Download an uploaded image through its file endpoint. | The original uploaded bytes are returned. | Automated |

## Notes

- `Verified` from PDF validation only means Gemini found the lead's full name in the document. It is not identity or authenticity verification.
- Gemini interaction storage is disabled. The application must not log PDF contents or OCR text.
- For API upload tests, obtain an antiforgery token from `GET /api/antiforgery` and send it as the `X-CSRF-TOKEN` header.
- Automated Gemini cases replace the provider with a deterministic fake response; they verify request construction, application handling, and fail-closed behavior, not Gemini's live OCR accuracy.

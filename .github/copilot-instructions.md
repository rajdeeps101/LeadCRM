# Change workflow

For every requested repository change, follow this sequence:

1. Before editing any files, run the unit test project and the Quick functional suite against the current code:
   - `dotnet test UnitTestCases/LeadCrm.UnitTests.csproj`
   - `dotnet test FunctionalTestCases/LeadCrm.Tests.csproj -p:TestSuite=Quick`
2. If either baseline command fails, do not edit files. Report the failing test output and ask whether to proceed.
3. Before editing, create a unique `feature/<change-slug>` branch from `origin/main`. Never make feature changes directly on `main`.
4. Include a new or updated functional test in `FunctionalTestCases/` for every application behavior change.
5. After editing, rerun the unit tests and Quick functional suite. Do not push if either fails.
6. Commit only files required for the change and push the feature branch. The GitHub workflow runs unit and Quick tests, creates a pull request, then runs Sanity tests and requests Copilot review.
7. Let GitHub auto-merge the PR only after the required checks and an approving review pass. Never bypass branch protection or force-push.
8. Do not deploy from a local edit. Deployment must wait for successful required pull-request checks and the configured approval.

Never commit `App_Data`, build outputs, local environment files, or secrets.

# Change validation

For every requested repository change, follow this sequence:

1. Before editing any files, run the unit test project and the Quick functional suite against the current code:
   - `dotnet test UnitTestCases/LeadCrm.UnitTests.csproj`
   - `dotnet test FunctionalTestCases/LeadCrm.Tests.csproj -p:TestSuite=Quick`
2. If either baseline command fails, do not edit files. Report the failing test output and ask whether to proceed.
3. After the requested changes, run both commands again. Report the result and do not describe the change as validated unless both pass.
4. After both post-change checks pass, commit only the files required for the requested change and push to `origin/feature/crm`. If the remote, branch, or authentication is unavailable, stop and report the blocker; never force-push.
5. Do not deploy from a local edit. Deployment must wait for successful required pull-request checks and the configured human approval.

Never commit `App_Data`, build outputs, local environment files, or secrets.

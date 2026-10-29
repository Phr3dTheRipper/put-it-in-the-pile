# Putting another release in the pile

1. Update the changelog, `src/AssemblyInfo.cs`, the client version in `src/App.cs`, and the version in its About dialog.
2. Run `Build.ps1`, `scripts/Test.ps1`, and `scripts/Package.ps1` on Windows.
3. Commit the source changes and push them. Confirm the build checks pass.
4. Tag the checked commit and push the tag, for example:

   ```powershell
   git tag -a v1.0.1 -m "Another historic achievement"
   git push origin v1.0.1
   ```

The build workflow compiles the tagged source, runs the parser checks, packages an explicit list of distributable files, and publishes a GitHub Release containing `PutItInThePile-Windows.zip` and `SHA256SUMS.txt`. It does not include local pile settings, usage checks, or account information.

The workflow token can write releases only in the release job. Pull-request builds have read-only repository access. Official GitHub actions are pinned to commit hashes.

The executable is unsigned. Do not describe it as signed, Microsoft-certified, or officially affiliated with OpenAI. If code signing is added later, sign before packaging so the published checksum covers the signed executable.

Do not move a published version tag or overwrite published assets. Fixes should get a new version.

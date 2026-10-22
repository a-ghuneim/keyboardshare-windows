# Local audit and first publication

These steps are manual. No script creates a GitHub repository, pushes code, changes visibility, or publishes a release.

## Before your first commit

1. Replace `[COPYRIGHT HOLDER - REPLACE BEFORE PUBLICATION]` in `LICENSE` with the copyright holder's chosen public attribution. A filesystem username was not assumed to be a legal identity. The year is 2026.
2. Rotate any token exposed in screenshots or shared files; set the same fresh random token on both laptops. Never commit operational tokens.
3. Review source, documentation, and staged changes. Run automated checks and the README's manual two-laptop acceptance checks before claiming device-level validation.
4. After completing license attribution, rerun the packaging script. Keep runtime notices with the binary distribution.

## Audit scope and findings

All hand-written source, project files, manifest, documentation, test harness, and the generated-file inventory were reviewed. No Git metadata/history existed at the time of inspection. No hard-coded operational token, password, API key, personal computer name, personal IP configuration, or absolute user path was found in source. Test strings `secret`, `other`, and repeated characters are deliberately non-operational fixtures; session-test tokens are random at runtime.

Address literals have specific roles: `192.168.1.2` is the existing editable peer-IP placeholder, `0.0.0.0` is the IPv4 wildcard bind address, `127.0.0.1` is test loopback, and README `192.168.1.10`/`.20` addresses are clearly marked setup examples. None is your deployed personal configuration.

Generated `obj/` restore metadata, file lists, and editor configuration contain absolute local profile paths. Framework-dependent outputs include debug symbols. Existing published executables were local artifacts. These files remain on disk so working copies are not removed; `.gitignore` excludes them from normal staging. Public-release publishing disables separate debug symbols and enables deterministic source-path mapping. Packaging uses an explicit allowlist so incidental settings/logs are not archived. This review cannot guarantee arbitrary future files contain no secrets; inspect every staged diff.

The actual per-user settings/logs live outside the repository in `%LOCALAPPDATA%\KeyboardShare`; they were not copied into publication files. Tokens remain plaintext there, as documented. Never upload this folder or unmasked settings screenshots.

Source references only .NET/Windows APIs and the local test project, with no explicit third-party NuGet application dependencies or copied-source license notices found. Examined .NET runtime packs contain MIT licenses and runtime third-party notices. The release script preserves the provided notices separately. No source-license conflict was identified; this was not an independent legal-provenance or security audit.

## Build and package

From the repository root with a .NET 8 SDK on Windows:

```powershell
dotnet build KeyboardShare/KeyboardShare.csproj -c Release --warnaserror
dotnet run --project KeyboardShare.Tests/KeyboardShare.Tests.csproj -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File ./scripts/Publish.ps1
```

The version is held in the application project's `<Version>` property. Initial assets:

- `publish/release/KeyboardShare-1.0.0-win-x64.zip`
- `publish/release/KeyboardShare-1.0.0-win-x64.zip.sha256`

Inspect the archive before uploading. It should contain the EXE, README, LICENSE, SECURITY policy, and `LICENSES/` runtime notices. Never distribute personal configuration, logs, PDBs, or a raw workspace ZIP. The executable is unsigned.

## Recommended GitHub presentation

- **Name:** `keyboardshare-windows`
- **Description:** Share one physical keyboard between two Windows PCs over a trusted LAN, with hotkey switching, authenticated TLS, and a system-tray app.
- **Topics:** `windows`, `keyboard`, `keyboard-sharing`, `lan`, `dotnet`, `csharp`, `winforms`, `productivity`, `tcp`, `sendinput`

Static Windows/.NET/MIT badges are provided. Add repository-specific latest-release/build badges only after the repository, release, and hosted build actually exist.

## Initialize Git and review the first commit

Run from the project root. If Git asks for identity, configure your chosen public name and GitHub-provided no-reply email. No machine-specific identity is supplied here.

```powershell
git init -b main
git add .gitattributes .gitignore README.md LICENSE CONTRIBUTING.md SECURITY.md PUBLICATION.md KeyboardShare KeyboardShare.Tests .github scripts
git status --short
git diff --cached --stat
git diff --cached --check
git diff --cached
```

Review the staged diff: source, docs, workflow, and script only. There should be no `bin/`, `obj/`, `publish/`, operational settings, or binary/debug output. `.gitignore` does not remove tracked files or files deliberately added with `-f`.

When satisfied:

```powershell
git commit -m "Prepare KeyboardShare for initial public release"
```

## Create GitHub repository and push manually

1. Sign into GitHub and choose **New repository**.
2. Name it **keyboardshare-windows** and use the description above. Choose **Private** for an initial review or **Public** when ready; no script makes this choice for you.
3. Do **not** initialize it with a README, .gitignore, or license: these exist locally. Create the empty repository.
4. Copy its HTTPS URL and replace `YOUR_GITHUB_USERNAME` below:

```powershell
git remote add origin https://github.com/YOUR_GITHUB_USERNAME/keyboardshare-windows.git
git remote -v
git push -u origin main
```

Authenticate through GitHub's supported credential flow. Do not embed a password/token in the URL. See [GitHub's existing-project guide](https://docs.github.com/en/migrations/importing-source-code/using-the-command-line-to-import-source-code/adding-locally-hosted-code-to-github).

5. Review hosted source and the **Windows build and checks** Actions result. This workflow never publishes a release.
6. Add the topics in the repository's About section.
7. Before distributing publicly, enable **private vulnerability reporting** in repository security settings and confirm the Security / Security and quality tab offers **Report a vulnerability**. Alternatively configure a real private contact in `SECURITY.md`. Adding that file alone does not enable private reports. See [GitHub's reporting guide](https://docs.github.com/en/code-security/how-tos/report-and-fix-vulnerabilities/report-privately).
8. If starting Private, review everything before manually changing visibility to Public in repository Settings.

## Publish the first release manually

1. Complete license attribution and run the build/tests/manual checks. Rerun packaging from the exact source you commit.
2. Commit/push the reviewed source and confirm Windows CI succeeds.
3. Choose **Releases → Draft a new release** on GitHub.
4. Create tag **`v1.0.0`**, targeting the reviewed `main` commit, and title it **KeyboardShare v1.0.0**. Future tags should match project metadata.
5. Attach the ZIP and `.zip.sha256` listed above from `publish/release/`.
6. Explain that users extract the ZIP and run the same EXE on both computers: Host where the keyboard is attached, Client on the receiver. Include unsigned-binary, plaintext-token, secure-desktop, and competing-keyboard-forwarder limitations. State which real-device checks you actually performed.
7. Review the draft and publish when ready.

Do not upload old `publish/win-x64` builds by accident. The canonical public-release output is `publish/release/`, produced by `scripts/Publish.ps1` from reviewed source.

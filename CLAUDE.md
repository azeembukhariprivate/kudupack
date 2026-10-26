# KUDUPACK

A lightweight archive tool for Windows: open, extract and create ZIP, 7Z, RAR, TAR.GZ and more.
Part of the KUDU family of free, open-source Windows apps by Azeem Bukhari.

## Working with Azeem (the owner)

- **Branches:** work on `main` and push to `main`. Ask before creating any other branch.
- **New features:** first say what you understood, and ask your doubts and suggestions, one question at a time.
  Only start building when Azeem says "build".
- **Look:** keep the app's current look. No redesigns unless asked.
- **About window credit:** "Made by Azeem, with Claude and ♥" (the heart in the accent colour). Keep it.
- **Commits:** author `Azeem Bukhari <12558389+azeembukhariprivate@users.noreply.github.com>`. Clear messages that say
  what changed and why. Don't put AI model names or versions in commits, code or docs.
- **Words:** the app's text, README and release notes use plain everyday words, not technical jargon.
- **Before pushing:** build and run the tests locally, and re-read the diff. After pushing, watch the GitHub Actions
  build until it's green; fix failures, never skip or disable a test to get green.

## How the app is built

- .NET Framework 4.8 WPF app (XAML views in `src/KUDUPACK/Views`, themes in `Themes/`). It builds on Windows.
- Archive work is done by the bundled 7-Zip; its licences are in `licenses/` and THIRD-PARTY-NOTICES.txt. Keep those
  up to date when changing the 7-Zip version.
- It doesn't use kudu-common (it came first), and has no `--self-test`.
- Extracting must never write outside the chosen folder (blocked "zip-slip" names like `..\..\evil.exe`) and must ask
  before replacing files already there. `tests/KUDUPACK.Tests` checks this with real harmful archives on Windows.

```
dotnet run --project tests/KUDUPACK.Tests -c Release
dotnet build src/KUDUPACK/KUDUPACK.csproj -c Release -o out/KUDUPACK
```

## Releasing

1. Raise `<Version>` in `src/KUDUPACK/KUDUPACK.csproj`.
2. Commit and push to `main`.
3. GitHub Actions (`.github/workflows/build.yml`, Windows) tests and builds the app. When no release exists yet for that
   version it creates the `v` tag and publishes the installer and portable zip on the Releases page, plus a copy named
   `KUDUPACK-setup.exe` that the README's download button links to (`releases/latest/download/KUDUPACK-setup.exe`).
   Pushes that keep the same version only build.

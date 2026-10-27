# Traditional Chinese UI localization

The first localization increment covers the Settings window. On Windows UI cultures `zh-TW`, `zh-HK`, `zh-MO`, and `zh-Hant`, OrbitDrop loads the embedded `src/OrbitDrop.UI/Localization/zh-Hant.json` resource. Every missing key falls back to the existing English source text. Other cultures continue to use English.

The JSON keys are the English UI strings. `UiText.Initialize()` translates literal WPF text when controls load; `UiText.Apply(window)` covers offscreen review renders. Text generated in code should use `UiText.T` for fixed strings or `UiText.Format` for strings with values. Preserve `{0}`, `{1}`, and other placeholders in translations. Operation identifiers, saved settings, file paths, and user-entered action names must not be translated.

This PR intentionally focuses on Settings and its wheel editor. New or untranslated Settings text remains English. Tray, onboarding, the live wheel, notifications, and provider diagnostics belong in follow-up PRs.

To review locally on a Traditional Chinese Windows installation:

```powershell
dotnet test tests/OrbitDrop.Tests/OrbitDrop.Tests.csproj -c Release
dotnet build src/OrbitDrop.App/OrbitDrop.App.csproj -c Release
src/OrbitDrop.App/bin/Release/net10.0-windows10.0.19041.0/OrbitDrop.exe --render-ui artifacts/zh-Hant-review
```

The renderer writes screenshots without touching an installed Microsoft Store package. Review Settings pages at 100% and 150% display scale before merging.

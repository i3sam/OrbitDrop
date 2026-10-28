<p align="center"><img src="src/OrbitDrop.UI/Assets/orbitdrop-256.png" width="96" alt="OrbitDrop icon"></p>

<h1 align="center">OrbitDrop</h1>
<p align="center"><b>Your files. Your flow.</b><br>Convert, edit and manage files without opening an app. For Windows 10 (19041+) and 11.</p>

<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="docs/guide/guide-hero-dark.gif">
    <img src="docs/guide/guide-hero.gif" width="600" alt="Dragging photo.png, tapping Shift, the OrbitDrop wheel opening, releasing on JPG and photo.jpg appearing next to the original">
  </picture>
</p>

## Download

<a href="https://apps.microsoft.com/detail/9NJQRMQLL2SZ?mode=direct"><img src="https://get.microsoft.com/images/en-us%20dark.svg" height="52" alt="Get it from Microsoft"></a>&nbsp;&nbsp;<a href="https://github.com/i3sam/OrbitDrop/releases/download/v0.4.7-alpha/OrbitDrop-Setup.exe"><img src="docs/design/download-installer.svg" height="52" alt="Download the installer"></a>

**The Microsoft Store is the easiest way:** it's free, installs without a security warning, and keeps OrbitDrop up to date.

<sub>The installer needs no admin rights or .NET. It isn't code-signed yet, so if Windows says "Windows protected your PC", click **More info → Run anyway**. Also available: [portable .zip](https://github.com/i3sam/OrbitDrop/releases/download/v0.4.7-alpha/OrbitDrop-Portable.zip) · [all releases](https://github.com/i3sam/OrbitDrop/releases)<br>Already using the GitHub version? **Settings › About › Switch to the Store version** moves you over, settings included.</sub>

## How it works

**1. Drag a file, then tap Shift.** Start dragging one or more files in File Explorer or on the desktop and keep holding. Tap Shift once: the wheel opens around the pointer.

<picture><source media="(prefers-color-scheme: dark)" srcset="docs/guide/guide-drag-dark.gif"><img src="docs/guide/guide-drag.gif" width="600" alt="Dragging a photo and tapping Shift opens the wheel"></picture>

**2. Let go on what you want.** The result appears **next to the original**, which is never changed. Let go on the centre or press **Esc** to cancel.

<picture><source media="(prefers-color-scheme: dark)" srcset="docs/guide/guide-release-dark.gif"><img src="docs/guide/guide-release.gif" width="600" alt="Releasing on JPG creates photo.jpg next to photo.png"></picture>

**3. Tap Shift again for more.** Each tap switches between **Convert**, **Tools** and **Actions**.

<picture><source media="(prefers-color-scheme: dark)" srcset="docs/guide/guide-modes-dark.gif"><img src="docs/guide/guide-modes.gif" width="600" alt="Tapping Shift cycles the wheel through Convert, Tools and Actions"></picture>

**Options with dots open into more choices.** Rest on one (like Compress) to see them; rest on the centre to go back, or let go straight away for the most common choice.

<picture><source media="(prefers-color-scheme: dark)" srcset="docs/guide/guide-groups-dark.gif"><img src="docs/guide/guide-groups.gif" width="600" alt="Resting on Compress opens sizes; choosing 1 MB saves a compressed copy"></picture>

**No dragging?** Select files, press **Ctrl + Alt + Space** and click an option. Click the centre of the wheel (or tap Shift, or press the shortcut again) for **Tools** and **Actions**; **Esc** closes it.

**Or right-click.** Right-click files or folders in File Explorer and choose **OrbitDrop**. Copied a screenshot? Press **Ctrl + Alt + Space** with nothing selected and the wheel opens for the image. Every result can be **undone** for a few seconds (it goes to the Recycle Bin).

<picture><source media="(prefers-color-scheme: dark)" srcset="docs/guide/guide-keyboard-dark.gif"><img src="docs/guide/guide-keyboard.gif" width="600" alt="With a file selected, Ctrl+Alt+Space opens the wheel and a click converts it"></picture>

**Make it yours.** Settings → **Wheel** shows your wheel: click a slice to hide it (click again to bring it back), drag a slice to move it, then **Save**.

<picture><source media="(prefers-color-scheme: dark)" srcset="docs/guide/guide-customize-dark.gif"><img src="docs/guide/guide-customize.gif" width="600" alt="In Settings, hiding BMP and TIFF and dragging PDF to the top, then Save, changes the real wheel to match"></picture>

The app includes the same guide with a hands-on practice step (tray menu → **How to use OrbitDrop**). These animations are rendered from the real app: `OrbitDrop.exe --render-guide <folder>`.

| Mode | 0.2 capabilities |
|---|---|
| Convert | **Images:** PNG, JPG, WebP, JFIF, BMP, TIFF, GIF, PDF; from SVG too (also reads HEIC, AVIF, JPEG XL and camera RAW when Windows has the codec) · **PDF:** pages to PNG or JPG · **Video:** MP4, WMV, animated GIF, or extract audio as MP3 / M4A / WAV / WMA / FLAC · **Audio:** MP3, M4A, WAV, WMA, FLAC · **Documents:** Word, Excel and PowerPoint to PDF (with Microsoft Office or LibreOffice installed) · **CorelDRAW:** CDR and CMX to PDF, SVG, PNG, JPG, WebP, TIFF, BMP (built in, nothing else to install) |
| Tools | **Images:** edit (crop, adjust, markup, redact), compress (by % or under 1 MB / 500 KB / 200 KB), resize, rotate & flip, grayscale, copy text (OCR), combine into PDF · **PDF:** merge, split into pages, compress, rotate pages · **Video:** trim, compress (720p / 1080p / 480p / half size), remove audio, snapshot, join · **Audio:** trim, compress, normalize, mono / stereo · **Archives:** extract ZIP, TAR, TAR.GZ, GZ · **Any file:** info, edit details, remove metadata or location |
| Actions | Copy path or name, show in folder, copy to / move to (recent and common folders), float (a shelf to drag files out later), share (Nearby Share), zip, terminal, set as wallpaper. Works on folders too |

**Private by design:** everything runs locally. No uploads, no account, no telemetry. The only network request is the update check against GitHub Releases, every few hours. Updates install automatically when you are not using OrbitDrop, after their checksum is verified (Settings → About: turn either off any time).

## Need help?

- **Questions, a problem with a purchase, or something not working:** [orbitdrop.essam.biz/feedback](https://orbitdrop.essam.biz/feedback). It goes straight to the developer, and you get a reply by email. In the app: tray menu → **Help & feedback**, or Settings → About → **Get help**.
- **Bugs and ideas in public:** [open an issue](https://github.com/i3sam/OrbitDrop/issues).
- **Bought the upgrade in the Store version, but it shows as locked?** Settings → Upgrade → **Restore purchase**. Make sure the Microsoft Store app is signed in with the account you bought it with.

## Build from source

Requirements: Windows 10/11 and the [.NET 10 SDK](https://dotnet.microsoft.com/download). For the installer, add [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`winget install JRSoftware.InnoSetup`).

```powershell
dotnet build OrbitDrop.slnx -c Release
dotnet test tests/OrbitDrop.Tests
dotnet run --project src/OrbitDrop.App          # runs from source (lives in the notification area)
./build/package.ps1                             # → artifacts/: setup.exe, portable.zip, SHA256SUMS.txt
```

More detail: [docs/development/building.md](docs/development/building.md).

## Repository map

| Path | What |
|---|---|
| `src/OrbitDrop.App` | Composition root, tray lifecycle, interaction controller |
| `src/OrbitDrop.UI` | Design system, radial wheel, toasts, Settings, onboarding |
| `src/OrbitDrop.Windows` | Win32/COM: drag detection, OLE drop target, tray, theme, shell |
| `src/OrbitDrop.Core` | Operation contracts, registry, collision-safe atomic output |
| `src/OrbitDrop.Conversion` / `Tools` | Operation providers (add capabilities here) |
| `src/OrbitDrop.Jobs` / `Infrastructure` | Background jobs, settings, local logs |
| `experiments/OrbitProbe` | Feasibility probe, kept as a regression and diagnostics tool |
| `docs/` | Research, architecture, design system, milestones |

## Documentation

**Status: 0.4.7-alpha.** The core gesture is validated on Windows 11 (single monitor, 100% scale). Multi-monitor, mixed-DPI and Windows 10 sign-off are still pending; see [the feasibility report](docs/research/02-feasibility-report.md).

- [Initial assessment](docs/research/00-initial-assessment.md) · [Probe test sheet](docs/research/01-orbit-probe-test-sheet.md) · [Feasibility report](docs/research/02-feasibility-report.md)
- [Architecture & milestones](docs/architecture/03-architecture-and-milestones.md) · [Design system](docs/design/design-system.md) · [Milestone 1 summary](docs/milestones/M1-foundation.md)
- [Privacy policy](docs/PRIVACY.md): your files never leave your PC, and OrbitDrop collects no personal data.

## License

MIT. See [LICENSE](LICENSE) and [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). OrbitDrop is inspired by the interaction of Tangerine for Mac; it shares no code, artwork or branding with it.

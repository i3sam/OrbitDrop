## What's new in 0.4.8-alpha

- **Get help from inside OrbitDrop.** Settings › About › **Get help**, or the tray menu › **Help & feedback**, opens a short form that goes straight to the developer, with your OrbitDrop and Windows versions already filled in. Leave your email and you get a reply.
- **Report a failed conversion in one click.** When something doesn't work, its card now has a **Report** button. It copies what went wrong (the kind of file and the error) to your clipboard, so you can paste it into the form, and nothing is sent without you seeing it.
- **Copy details** (Settings › About) copies OrbitDrop's version and the latest problems from its log, ready to paste into any message or GitHub issue.

## In 0.4.7-alpha

- **Fixed: the Your actions cards overlapped in a narrow Settings window.** The action's name no longer runs into the "Works on" box. Names and descriptions now shorten neatly, and Works on and Advanced move onto a row of their own when there isn't room.
- **Rename an action under Advanced.** The name is now the first row there, which keeps each card clean: app icon, name, what it does.

## In 0.4.6-alpha

- **Your own actions.** Settings › Your actions: put your own programs and commands on the wheel (Actions › Your actions), with the dropped files filled in for you: `{file}`, `{files}`, `{folder}` (already quoted, so names with spaces or & just work), `{name}` and `{ext}`. Ready-made ones to start from: Open in VS Code, Open in Paint, Run a Python script, Open in Photoshop, a command. Two are free; more come with the Microsoft Store version. Thanks to [pixo2000](https://github.com/pixo2000) for the idea ([#4](https://github.com/i3sam/OrbitDrop/issues/4)).
- **Print.** Actions › Print sends photos and PDFs to your printer through Windows' own print dialog (printer, pages, copies). Photos turn to fill the paper; long PDFs print without filling the memory.
- **A tidier wheel.** It keeps one size for the whole drag (no more growing when you move to a busy page), Actions is grouped into **Open** (Show in folder, Copy path, Copy name, Terminal) and **Share**, labels are bigger and always stay inside their slice, and Windows' own drag picture no longer covers the centre.
- **Work only in File Explorer** (Settings › General): the wheel opens only for files dragged from File Explorer or the desktop, so Shift-dragging in Photoshop, Illustrator or Figma never opens it by mistake.
- **See what the Store version adds.** Its Premium tools (remove background, upscale, stickers and more) now show on the wheel with a small crown, and choosing one explains how to get them. This version stays free and open source; turn them off in Settings › General › Show Premium tools.

## In 0.4.5-alpha

- **Fixed: Compress video failed on some PCs** with "Windows couldn't read this file", often 60 fps game recordings (like NVIDIA Instant Replay clips) on PCs with an NVIDIA graphics card. When the graphics card refuses a video, OrbitDrop now finishes it with Windows' own software encoder instead of failing. This works for every video and audio tool, not only Compress. Thanks to [teecil](https://github.com/teecil) for finding the cause ([#3](https://github.com/i3sam/OrbitDrop/issues/3)).
- **Videos keep their frame rate.** Compressed and trimmed videos stay at 60 fps (Trim used to make them 30).

## In 0.4.4-alpha

- **Fixed: updates that didn't install, and OrbitDrop not coming back afterwards.** The installer could stop because it thought OrbitDrop was still open, or because File Explorer was holding the right-click menu. Now OrbitDrop is closed for the update the standard Windows way, the right-click menu gets a new file name with every version, and if an update ever can't be installed, the version you had starts again by itself.
- Updates keep a log next to OrbitDrop's own (Settings → Advanced → Open logs), so any problem can be explained.

## In 0.4.3-alpha

- **Clearer clipboard setting.** "Use the clipboard" works through Ctrl + Alt + Space, so when that shortcut is turned off, the clipboard setting now greys out and says so, instead of seeming to do nothing.

## In 0.4.2-alpha

- **CorelDRAW, built in.** OrbitDrop now reads CorelDRAW drawings (.cdr, and Corel's .cmx) itself: nothing else to install. Drag one, tap Shift, and convert it to PDF, SVG, PNG, JPG, WebP, TIFF or BMP. Pictures come out sharp, and a drawing with several pages becomes a folder with one file per page.
- **Safe with any drawing.** Each drawing is read in a separate, time- and memory-limited helper, so a damaged or unusual file can only fail its own conversion, never OrbitDrop.

## In 0.4.1-alpha

- **CorelDRAW files, made easy.** Converting .cdr files needs the free app Inkscape (or LibreOffice). If neither is on your PC, dropping a .cdr now offers **Get Inkscape**, which opens its free Microsoft Store page, instead of showing nothing. As soon as it's installed, OrbitDrop picks it up: no restart needed.
- **Finds Inkscape and LibreOffice wherever they are:** the Microsoft Store versions, per-user installs, scoop and anything on your PATH, not only Program Files.

## In 0.4.0-alpha

The wheel works exactly as before. Everything new is optional, and the extras that change the wheel are off until you turn them on.

- **Right-click menu.** Right-click files or folders in File Explorer and choose **OrbitDrop**: the wheel opens for them, ready to click. On Windows 11 it's in the main menu with the Microsoft Store version (in "Show more options" with the GitHub version). Turn it off in Settings → Shortcuts.
- **Undo.** Every finished conversion offers **Undo** for a few seconds. It moves what was just made to the Recycle Bin, so even an undo can be undone. It never touches your originals, or files you moved. Turn it off in Settings → General.
- **The clipboard.** Copied a screenshot or an image from a website? Press **Ctrl + Alt + Space** with nothing selected, and the wheel opens for it: save it as PNG, convert it to JPG or WebP, compress it… The result lands in the folder you're in, or in Pictures. Copied files work too. Settings → Shortcuts.
- **Estimated size** *(Settings → Wheel → Extras, off by default)*. Rest on a format, Compress or Resize and the centre of the wheel shows about how big the result will be, like "≈ 640 KB · −73%", before you let go.
- **Put my last choice first** *(Settings → Wheel → Extras, off by default)*. The option you used last for a kind of file moves to the top of its wheel.

## In 0.3.9-alpha

- **SVG files.** Drag an SVG, tap Shift, and convert it to PNG, JPG, WebP, PDF and more, or rotate, resize and compress it. Small icons come out 1024 px wide, so they stay sharp; transparency is kept.
- **Safe with any SVG.** An SVG can point to images on the internet or to other files. OrbitDrop never follows those links: it only draws what is inside the file, so opening an SVG never contacts anyone.

## In 0.3.8-alpha

- **PDF to Word.** Drag a PDF, tap Shift, let go on DOCX: you get an editable Word document. Uses the Word (or LibreOffice) on your PC.
- **Fixed: documents and CorelDRAW files with long names.** Files with very long names (common for downloads from design sites) failed with "That didn't work". They convert now.
- **Dragging from inside a ZIP** now tells you to extract it first, instead of "No files": files inside a ZIP aren't on disk until they're extracted.

## In 0.3.7-alpha

- **Fixed: long file names in the wheel.** A long name no longer spills over the centre of the wheel. It's shortened in the middle, so you still see how it starts and its extension ("Filee is a free,… computer.mp4").

## In 0.3.6-alpha

- **Click instead of holding.** Turn it on in Settings → Shortcuts. Then drag files, tap Shift, and let go in the centre of the wheel: it stays open, and you click what you want. Click outside or press Esc to close. Off by default.
- **Documents to PDF.** Drag Word, Excel or PowerPoint files (DOC, DOCX, RTF, ODT, XLS, XLSX, ODS, PPT, PPTX, ODP), tap Shift, let go on PDF. Old formats also convert to DOCX, XLSX or PPTX. OrbitDrop uses the Microsoft Office or LibreOffice already on your PC, invisibly; your file is only read.
- **CorelDRAW files.** With Inkscape or LibreOffice installed, .cdr drawings convert to PDF, SVG, EPS, PNG, JPG, TIFF or BMP.

## In 0.3.5-alpha

- **Convert to WebP.** Drag any image, tap Shift, let go on WEBP. Transparency is kept, so PNG logos and stickers stay see-through.
- **Convert to and from JFIF.** Got a .jfif a website won't take? Drop it on JPG. Going JPG ↔ JFIF only renames the file, so the picture isn't re-compressed at all.
- **WebP files open everywhere.** Rotating, resizing, compressing and converting WebP pictures no longer needs Microsoft's WebP extension.
- Don't need the new formats? Click them away in Settings → Wheel.

## In 0.3.4-alpha

- **Windows accent colour works in dark mode.** OrbitDrop now uses the same shade of your accent that Windows itself uses: lighter in dark mode, darker in light mode. Dark accents no longer fall back to black and white.

## In 0.3.3-alpha

- **Microsoft Store version: updates install right away.** OrbitDrop checks the Store every few hours and installs a new version by itself as soon as you're not using it, then starts again, instead of waiting for your next restart.

## In 0.3.2-alpha

- **Rate OrbitDrop from Settings.** Settings → General now has an "Enjoying OrbitDrop?" row: rate it in the Microsoft Store, or star it on GitHub.

## In 0.3.1-alpha

- **Ctrl + Alt + Space is more reliable in File Explorer.** With several tabs open it now always reads the tab you're looking at, and it tries again if File Explorer is busy for a moment, instead of saying "Select files first".

## In 0.3.0-alpha

- **PDF tools.** Drag PDFs, tap Shift twice for Tools:
  - **Merge** several PDFs into one, in the order you picked.
  - **Split** a PDF into a folder with one PDF per page.
  - **Compress**: the photos inside get smaller; text stays sharp, selectable text.
  - **Rotate pages** right, left or 180°.
- **Ctrl + Alt + Space in VS Code.** Select files in VS Code's Explorer (or just have a file open) and press the shortcut. OrbitDrop uses VS Code's own "Copy Path" to find them, then puts your clipboard back exactly as it was. Also works in Cursor, Windsurf and VSCodium.
- After you've used OrbitDrop a few times, it asks once if you'd like to rate it. It never asks again.

## In 0.2.10-alpha

- **Fixed: dragging from VS Code.** In 0.2.9 the wheel opened but said "No files". It now reads the real file and folder paths VS Code sends, so drag from its Explorer sidebar, tap Shift, and let go on what you want. Results land next to your original, not in a temporary folder.

## In 0.2.9-alpha

- **Works with VS Code:** drag a file or folder from VS Code's Explorer sidebar, tap Shift, and the wheel opens, just like from File Explorer. The same goes for other apps that drag files their own way (many are built like VS Code).

## In 0.2.8-alpha

- **Trimming works on more PCs:** if your graphics hardware can't encode the video, OrbitDrop now finishes the trim in software instead of failing.
- **The guide's demos are easier to read:** the pointer now rests beside each option instead of on top of its label.
- Getting ready for the **Microsoft Store**: the same app can now run as a Store package, where the Store keeps it up to date and Windows' Startup apps handle "Start OrbitDrop when I sign in". The GitHub version works as before.
- A [privacy policy](https://github.com/i3sam/OrbitDrop/blob/main/docs/PRIVACY.md): your files never leave your PC, and OrbitDrop collects no personal data.

## In 0.2.7-alpha

- **Rearrange your wheel:** in Settings → Wheel, drag a slice around the ring. The others make room as you move, and **Save** puts your order on the real wheel. **Reset** brings back the original wheel.
- **The guide keeps one size:** the window no longer jumps between steps. Its "Make the wheel yours" demo now shows dragging too.

## In 0.2.6-alpha

- **A simpler Wheel page.** Settings → Wheel now shows your actual wheel. Pick the kind of file and mode at the top, click a slice to hide it (it turns into a dashed outline), click again to bring it back, then press **Save**. **Undo changes** and **Show all** are right there.
- The guide's "Make the wheel yours" step shows the new page.

## In 0.2.5-alpha

- **Make the wheel yours:** Settings → **Wheel** lists everything OrbitDrop can do, by kind of file. Click what you don't use to turn it off: whole groups (like Resize) or single choices (like Compress 25%). The wheel shows only what's left; **Show everything** brings it all back.
- **Ctrl + Alt + Space, then switch modes:** click the centre of the wheel (it says where it leads, e.g. "Click for Tools"), press the shortcut again, or tap Shift. Esc or a click outside closes it.
- **The guide** shows both: the keyboard step now demonstrates switching modes, and a new step shows customizing the wheel.
- Fix: in the guide, the option under the pointer now lights up correctly (white in dark mode).

## In 0.2.4-alpha

- **Automatic updates.** OrbitDrop now downloads updates quietly, checks them against their published checksum, and installs them the next time you're not using it (no wheel open, nothing converting, no OrbitDrop windows open). It restarts in a few seconds with your settings. Prefer to be asked? Turn off **Install updates automatically** in Settings → About.
- Update checks no longer fail when GitHub limits requests from your network (common on shared office or school connections).

> Coming from 0.2.3 or earlier: click **Install** on the update card once. From then on, updates install themselves.

## In 0.2.3-alpha

- **A brand-new guide** (tray menu → How to use OrbitDrop, or Settings → General): animated, step-by-step demonstrations built from the real wheel, covering dragging and tapping Shift, choosing an option, switching modes, grouped options and the Ctrl + Alt + Space shortcut. It ends with a **hands-on practice**: drag a sample photo, tap Shift, let go on JPG, and the guide confirms when you've done it.
- You can now tap Shift while dragging files **out of the Float shelf** too.

## In 0.2.2-alpha

- **Trim** video and audio: drag the start and end of the part to keep, play it, save a copy.
- **Float**: park files on a small always-on-top shelf and drag them out later, one at a time or all at once (great for email attachments and uploads). Drop more files on it any time.

## In 0.2.1-alpha

- **Ctrl + Alt + Space**: select files in File Explorer or on the desktop, press the shortcut, and click an option in the wheel. No dragging needed. Turn it off in Settings → Shortcuts.
- **Video to GIF**: a looping animated GIF of the first 10 seconds.
- **Normalize audio**: makes quiet recordings as loud as they can be without clipping.

## In 0.2.0-alpha

The biggest update yet: OrbitDrop now does a lot more than convert.

**A smarter wheel**
- Related options are grouped. Segments with three dots (like **Compress**) open their choices when you rest on them for a moment; rest on the centre to go back. Dropping straight on the segment runs the most common choice.

**Photos**
- **Edit**: crop (free or fixed shapes), adjust brightness, contrast, saturation and warmth, mark up with pen, arrows, boxes and text, and redact with black boxes or pixelation. Saves a copy; your original is untouched.
- **Compress** to 50 / 75 / 25 % of the size, or under 1 MB / 500 KB / 200 KB.
- **Resize** to 50 %, 1920, 1280 or 800 px.
- **Rotate & flip** (now with vertical flip and 180°), **Copy text** from an image (offline text recognition), **Set as wallpaper**.
- **PDF**: turn images into a PDF, or combine several into one; turn PDF pages into PNG or JPG.
- Reads **WebP, HEIC, AVIF, JPEG XL and camera RAW** if Windows has the codec. Phone photos now always come out upright.

**Metadata**
- **Info** for any file: camera, exposure, location, size, length and more. Edit the title, tags, rating, artist, album and so on, into a copy.
- **Remove all metadata** (photos, PNG, MP3, FLAC) or just the **location** from photos, without re-compressing.

**Video and audio**
- Compress video to **720p, 1080p, 480p or half size**; take a **snapshot**; **join** clips into one video.
- Compress audio (128 / 96 / 64 kbps), switch to **mono or stereo**, convert to **FLAC**.

**Actions** (work on folders too)
- **Copy to / Move to** a folder you choose or a recent one (never overwrites), **Share** (Nearby Share and apps), **Zip**, **Terminal**, plus copy path/name and show in folder.
- **Extract** ZIP, TAR, TAR.GZ and GZ archives.

> **Updating:** if you have 0.1.2 or later, OrbitDrop will offer this update itself (or choose **Check for updates** in the tray menu). From 0.1.0 or 0.1.1, download **OrbitDrop-Setup.exe** below once.

## Download

**Easiest: [get OrbitDrop from the Microsoft Store](https://apps.microsoft.com/detail/9NJQRMQLL2SZ).** Free, no security warning, updated by the Store. (Already have the version from here? Uninstall it first, so only one OrbitDrop runs.)

Or download it here:

| File | What it is |
|---|---|
| **OrbitDrop-Setup.exe** | **Recommended.** Installer: no admin rights, adds a Start Menu shortcut, optional start at sign-in, clean uninstall, in-app updates |
| **OrbitDrop-Portable.zip** | No install: unzip and run `OrbitDrop.exe` (the app tells you about new versions; you download them yourself) |
| SHA256SUMS.txt | Checksums for both |

Nothing else to install: .NET is included. Windows 10 (version 2004 or later) or Windows 11, 64-bit.

> **Windows SmartScreen:** this alpha isn't code-signed yet, so Windows may show "Windows protected your PC". Click **More info → Run anyway**. Updates installed from inside OrbitDrop don't show this.

## How to use

Start dragging any file in File Explorer or on the desktop, **tap Shift**, and release on an option in the wheel. The result appears next to the original, which is never changed. Tap Shift again for Tools and Actions; press Esc to cancel.

This is an early alpha, tested on Windows 11 with a single monitor at 100% scale. Please report problems in [Issues](../../issues).

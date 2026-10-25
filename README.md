<p align="center">
  <img src="docs/logo-512.png" alt="KUDUPACK logo" width="128">
</p>

<h1 align="center">KUDUPACK</h1>

<p align="center">A lightweight archive tool for Windows. Open, extract and create ZIP, 7Z, RAR, TAR.GZ and more.</p>

<p align="center">
  <a href="https://github.com/azeembukhariprivate/kudupack/releases/latest/download/KUDUPACK-setup.exe"><img src="https://img.shields.io/badge/Download-KUDUPACK%20for%20Windows-2ea44f?style=for-the-badge" alt="Download KUDUPACK for Windows"></a>
  <br>
  <sub>The installer for Windows 10 and 11, always the latest version · <a href="https://github.com/azeembukhariprivate/kudupack/releases/latest">portable zip and what's new</a></sub>
</p>

---

## Features

- **Open and extract** ZIP, 7Z, RAR, TAR, GZ, TGZ, BZ2, XZ, ISO, CAB, ARJ, LZH, WIM, CPIO, DEB, RPM and split archives (`.001`, `.part1.rar`).
- **Create** ZIP, 7Z, TAR, TAR.GZ, TAR.BZ2 and TAR.XZ archives, with five compression levels.
- **Passwords**: AES-256 for ZIP and 7Z, with the option to hide file names in 7Z.
- **Split archives** into 100 MB, 700 MB or 4 GB parts.
- **Browse before extracting**: folder tree, search, sorting, and quick preview of images and text files.
- **Drag files out** of an archive straight into any folder, or drop files in to add them (ZIP and 7Z).
- **Extract many archives at once**, each into its own folder.
- **File Explorer integration**: *Extract here*, *Extract to folder* and *Add to archive* in the right-click menu, and double-click to open archives.
- **Safe extraction**: files never land outside the folder you chose, even from archives with names like `..\..\Startup\evil.exe` made to trick extractors; KUDUPACK leaves those out and tells you.
- **Keeps Windows' download warning**: files extracted from an archive you downloaded keep the "downloaded from the internet" mark, so Windows SmartScreen and Office's Protected View still warn before opening them, as with File Explorer.
- **Never overwrites silently**: when files with the same names are already there, choose to replace them, keep both (the new copy gets a number, like `report (2).pdf`) or skip them.
- **Optional clean-up**: move the archive to the Recycle Bin after a successful extract.
- **Light and dark mode**, following your Windows setting.
- **Update check** against this repository's releases. Nothing is downloaded without asking.

RAR archives can be opened and extracted, but not created.

## Download

**[Download the installer](https://github.com/azeembukhariprivate/kudupack/releases/latest/download/KUDUPACK-setup.exe)** (`KUDUPACK-setup.exe`, always the latest version), or get any of these from the [Releases page](https://github.com/azeembukhariprivate/kudupack/releases/latest):

- `KUDUPACK-x.y.z-setup.exe`: installs for your user account, adds a Start menu entry, and can add the right-click menu.
- `KUDUPACK-x.y.z-portable.zip`: unzip anywhere and run `KUDUPACK.exe`. Nothing is installed.

Requires Windows 10 or 11. The .NET Framework 4.8 it uses is already part of Windows.

Windows SmartScreen may warn about a new app from an unknown publisher. Choose **More info → Run anyway**.

## No administrator rights needed

KUDUPACK runs as a normal user. The installer, settings, right-click menu and file associations are all stored for your Windows account only.

If you need to extract into a protected location such as `C:\Program Files`, KUDUPACK will tell you. Close it and start it with **Run as administrator** for that one task.

## Command line

```
KUDUPACK.exe "archive.zip"                    open an archive
KUDUPACK.exe --extract-here "archive.zip"     extract next to the archive
KUDUPACK.exe --extract-folder "archive.zip"   extract into a new folder named after the archive
KUDUPACK.exe --add "file or folder"           open the Create archive window
```

## Build from source

You need Windows with the [.NET SDK](https://dotnet.microsoft.com/download) (8 or later) installed.

```
git clone https://github.com/azeembukhariprivate/kudupack.git
cd kudupack
dotnet run --project tests/KUDUPACK.Tests
dotnet build src/KUDUPACK/KUDUPACK.csproj -c Release -o out/KUDUPACK
```

The tests check that extracting never writes outside the chosen folder (archives with names like `..\..\evil.exe` or `\Windows\…`) and that files already there are replaced, kept or skipped as chosen. They build real harmful ZIP files and extract them, which needs Windows; on other systems only the name checks run.

The app is in `out/KUDUPACK`. To build the installer, install [Inno Setup 6](https://jrsoftware.org/isinfo.php) and compile `installer/KUDUPACK.iss`.

## Publishing a new version

1. Change `<Version>` in `src/KUDUPACK/KUDUPACK.csproj`.
2. Commit and push to `main`.
3. GitHub Actions tests and builds the app. When no release exists yet for that version, it creates the `v` tag and publishes the installer and portable zip on the Releases page. Pushes that keep the same version only build.

## License

KUDUPACK is released under the [MIT License](LICENSE).

It uses the 7-Zip engine (GNU LGPL, with the unRAR restriction) and SevenZipSharp (GNU LGPL 3.0). See [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).

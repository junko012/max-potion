# Max Potion

A Pokémon save file editor, native to Linux and also available on other platforms.

> Built on [PKHeX.Core](https://github.com/kwsch/PKHeX) **25.11.07** with a native UI by [Avalonia UI](https://avaloniaui.net/) 11.3.8, running on **.NET 9** (bundled, nothing to install).
> No Wine, no .NET install. **Works offline.**

[Install](#install) · [Features](#features) · [Languages](#languages) · [Report issues](#reporting-issues-and-suggestions)

---

## Why Max Potion?

Max Potion started from a personal need. On Linux I was using web-based PKHeX implementations and never quite liked them, so I set out to build a native version. Drawing on what I learned in my student years, I aimed for a coherent, easy-to-use interface..

- **Works offline.** The data ships inside the app, in a SQLite database ([max-potion-database](https://github.com/junko012/max-potion-database)) that other projects can reuse.
- **Built-in movesets and learnsets**, with a move picker and recommended sets.
- **Legality that explains itself:** diagnosis, automatic fixes and "Recreate with a valid encounter".
- **13 interface languages**, with the data language selectable independently.
- **Linux first:** `.rpm`, `.deb`, AppImage and portable packages.

---

## Features

- Party and boxes: Pokémon with IVs/EVs, moves, nature, ability, item, ribbons and origin; release Pokémon.
- Bag by pocket, trainer and Pokédex.
- **Legal** tab: real-time legality check, step-by-step diagnosis and automatic fixes.
- **Recreate with a valid encounter** for an illegal Pokémon, and an **IV solver**.
- Move picker with recommended sets and Hidden Power; per-game forms.
- **Review before export:** see what will be written to your save and untick changes.
- 13 interface languages; species, move and item names can use a different language.
- Numeric and date fields limited to valid types and ranges.
- The database is opened read-only; the app checks its compatibility at startup.

Supports the games and generations supported by PKHeX.Core 25.11.07. Support for newer PKHeX.Core versions is in progress.

---

## Install

Download the file for your system from [Releases](https://github.com/junko012/max-potion/releases):

| File | For |
|---|---|
| `maxpotion-<version>-1.x86_64.rpm` | openSUSE, Fedora |
| `maxpotion_<version>_amd64.deb` | Debian, Ubuntu |
| `MaxPotion-<version>-x86_64.AppImage` | Any Linux distro, single file |
| `MaxPotion-<version>-linux-x64.tar.gz` | Any Linux distro, portable |
| `MaxPotion-<version>-win-x64.zip` | Windows |
| `MaxPotion-<version>-osx-arm64.dmg` / `osx-x64.dmg` | macOS (Apple Silicon / Intel) |
| `SHA256SUMS.txt` / `SHA256SUMS.txt.asc` | Checksums and their GPG signature |
| `maxpotion-signing-key.asc` | Public signing key |

Packages include the app, the database and all 13 interface languages (there is no separate language pack to download).

### Fedora / openSUSE
```bash
sudo rpm --import maxpotion-signing-key.asc
sudo dnf install ./maxpotion-1.0.1-1.x86_64.rpm      # openSUSE: sudo zypper install ./maxpotion-1.0.1-1.x86_64.rpm
```

### Ubuntu / Debian
```bash
sudo apt install ./maxpotion_1.0.1_amd64.deb
```

### AppImage (any distro)
```bash
chmod +x MaxPotion-1.0.1-x86_64.AppImage
./MaxPotion-1.0.1-x86_64.AppImage
```
If it does not start because FUSE is missing, run it with `--appimage-extract-and-run`.

### Portable (any distro)
```bash
tar -xzf MaxPotion-1.0.1-linux-x64.tar.gz
./MaxPotion-1.0.1-linux-x64/maxpotion
```

### Windows
Unzip `MaxPotion-<version>-win-x64.zip` and run `maxpotion.exe`.

### macOS
Open the `.dmg` and drag Max Potion to Applications.

### Verify your download
```bash
sha256sum -c SHA256SUMS.txt --ignore-missing

# Verify the signature of the checksums
gpg --import maxpotion-signing-key.asc
gpg --verify SHA256SUMS.txt.asc SHA256SUMS.txt
```
Signing key fingerprint: `A8CB AB68 574E 799D 3B89  416E D945 B459 30CA 9B88`

The `.rpm` is signed with the same key: after `rpm --import maxpotion-signing-key.asc`, `rpm -K <file>.rpm` should report the signature as OK.

### Notes
- **Linux is the main platform.** The Windows and macOS builds are offered as-is and have seen less testing; reports are welcome.
- The `.rpm` and the checksums are GPG-signed. The macOS build is not notarized (right-click → Open the first time) and the Windows build has no code signature, so SmartScreen may show a warning.
- **Always back up your save before editing it.**

---

## System requirements

| Platform | Requirement |
|---|---|
| Linux | x86_64, a graphical desktop (GNOME, KDE, XFCE…), glibc 2.31 or newer (Ubuntu 20.04+, Debian 11+, current Fedora/openSUSE) |
| Windows | Windows 10 or 11, 64-bit |
| macOS | macOS 13 or newer, Apple Silicon or Intel |

`libicu` is recommended on Linux (if it is missing, the AppImage starts in invariant mode). The .NET 9 runtime is included in every package; you do not need to install it.

---

## Languages

Max Potion handles **two independent languages**, both selectable in the **Preferences** menu:

- **Interface language** (menus, buttons, messages, legality text, tips).
- **Data language** (species, moves, items, abilities, natures, locations, ribbons…), which comes from PKHeX. By default it follows the interface language.

Changes apply live, even with a Pokémon open and pending edits (edits are kept by id, not by name).

**Which language is used at startup:** the one saved in `~/.config/maxpotion/settings.json` (keys `uiLocale` / `dataLocale`) → the system language → **English**.

### Included languages

**Interface (13):** English and Spanish are embedded in the binary; the other eleven ship as language files in `lang/`, included in every package.

| Code | Language |
|---|---|
| `en`, `es` | English, Spanish (embedded) |
| `de`, `fr`, `it`, `pt-BR` | German, French, Italian, Portuguese (Brazil) |
| `ja`, `ko`, `zh-CN` | Japanese, Korean, Simplified Chinese |
| `ru`, `id`, `hi` | Russian, Indonesian, Hindi |
| `ar` | Arabic *(RTL, mirrored interface)* |

**Data (from PKHeX):** `de`, `en`, `es`, `fr`, `it`, `ja`, `ko`, `zh-Hans`, `zh-Hant`. If the interface language has no PKHeX data (ru, ar, hi, id, pt-BR…), the **data is shown in English** and the status bar says so (amber pill, e.g. `UI RU · PK EN`). The long descriptions in the Pokémon database are English only.

The interface translations were produced with AI assistance and have not been reviewed by native speakers; corrections are welcome by Pull Request.

### Language files (`lang`)

The app looks for `{code}.json` in:

1. `/usr/share/maxpotion/lang/` (installed with the package)
2. `./lang/` next to the executable (portable builds)

Format: a flat JSON object `{"Key": "Text"}`. Texts with `{0}`, `{1}`… are templates: **keep the same placeholders** in the translation. **Missing keys fall back to English**, so a pack can translate only part of the UI.

### Adding a new language

1. Copy `src/Exxeguttor.UI/i18n/en.json` as a base and rename it with the language code (e.g. `it.json`). Translate only the values; do not touch the keys or the `{0}` placeholders.
2. Put it in `lang/`.
3. Add the code to `KnownLangPackLocales` in `LocalizationService.cs`.
4. If it is RTL, add it to `RtlLocales` in the same file.
5. If PKHeX has data in that language, add the mapping to the `DataLanguageService` table; otherwise the data stays in English.
6. Open a Pull Request: new languages are always welcome.

`dotnet test` checks that `es.json` and `en.json` have the same keys and placeholders.

---

## Database

Max Potion uses `pokemon.db`, published as a separate project at
[max-potion-database](https://github.com/junko012/max-potion-database) so other applications can use it.
Each app version carries its own copy (`pokemon-database/database/pokemon.db`), so the package is complete and works offline.
App/database compatibility is controlled by the database *schema*; see [docs/VERSIONING.md](docs/VERSIONING.md).

---

## Building from source

```bash
# Requirement: .NET 9 SDK
git clone https://github.com/junko012/max-potion.git
cd max-potion
dotnet build exxeguttor.sln
dotnet run --project src/Exxeguttor.UI
```

---

## License

GPL-3.0-or-later — see [LICENSE](LICENSE).

This project uses PKHeX.Core (GPL-3.0) as its save engine and Avalonia UI (MIT) as its UI framework.
Notices and license texts for all third-party components are in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) (and inside the app, under **Help → About → Licenses**).

---

## Credits

- **Creator and developer:** [junko012](https://github.com/junko012)
- [kwsch](https://github.com/kwsch) and the PKHeX contributors, for PKHeX.Core
- [AvaloniaUI](https://github.com/AvaloniaUI/Avalonia), for the UI framework
- [PokéAPI](https://github.com/PokeAPI/pokeapi), for species, move, item and ability data
- **Interface translations (13 languages):** produced with Claude, an AI assistant from Anthropic, without review by native speakers. Corrections are welcome (see "Adding a new language" above).

## Legal notice

Pokémon and its names are trademarks of Nintendo, Creatures and Game Freak. Max Potion is a fan project with no affiliation with those companies. Sprites, names and game data belong to their respective owners. Back up your save before editing it.

## Reporting issues and suggestions

From the app: **Help → Report a bug…** (opens a pre-filled GitHub form; the app sends nothing by itself) or **Help → Suggest a feature…**. You can also open an issue directly in this repository.

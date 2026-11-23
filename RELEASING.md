# Releasing Max Potion

Two repos, two version lines (see `docs/VERSIONING.md`):

| Repo | What | Tag |
|---|---|---|
| `max-potion` | the app (this repo) | `vX.Y.Z` |
| `max-potion-database` | `pokemon.db` | `db-vS.AAAA.MM.R` |

## App release

1. The app repo keeps its own copy of the DB at `pokemon-database/database/pokemon.db` (versioned here, so the repo is a
   complete package). If the DB changed, release it first in `max-potion-database` (see that repo's `RELEASING.md`), then
   bring the identical file here: `python scripts/sync-db.py <S>.<AAAA.MM.R>` — downloads that release, verifies SHA-256 and
   internal version, replaces the file and rewrites `db.lock`. Commit both.
2. Make sure `DbCompatibility.RequiredSchema` / `MinData` (in `src/Exxeguttor.App/Services/DbCompatibility.cs`) match the pinned DB.
   `RequiredSchema` only changes together with a DB schema bump.
3. Bump `<Version>` in `src/Exxeguttor.UI/Exxeguttor.UI.csproj` (the tag's version wins in CI; keep them equal).
4. Update `CHANGELOG.md`, commit, then `git tag vX.Y.Z && git push origin master vX.Y.Z`.
5. The `Release` workflow builds one complete package per platform (app + the repo's `pokemon.db` + all language packs):

   | File | Platform |
   |---|---|
   | `MaxPotion-X.Y.Z-linux-x64.tar.gz` | Linux (primary) |
   | `maxpotion_X.Y.Z_amd64.deb` / `maxpotion-X.Y.Z-1.x86_64.rpm` | Debian-Ubuntu / Fedora-openSUSE (texts in `packaging/`) |
   | `MaxPotion-X.Y.Z-win-x64.zip` | Windows |
   | `MaxPotion-X.Y.Z-osx-arm64.dmg` / `osx-x64.dmg` | macOS (ad-hoc signed, not notarized) |
   | `SHA256SUMS.txt` | checksums |

## Local development

Nothing extra: the DB is in the repo. `python scripts/sync-db.py --check` verifies the bundled DB still equals
the published release recorded in `db.lock` (CI runs it before building).

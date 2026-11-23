# Versioning

Three numbers, each with its own owner:

- **App** `X.Y.Z` (SemVer). One version for every platform. Tag `vX.Y.Z` in `max-potion`.
- **Database** `S.AAAA.MM.R`. Tag `db-vS.AAAA.MM.R` in `max-potion-database` (details in that repo's `VERSIONING.md`).
- **Compatibility contract**: the DB *schema* `S`. The app declares `DbCompatibility.RequiredSchema` and the oldest
  data it accepts (`MinData`). At startup the app reads `Metadata` (fallback `PRAGMA user_version`) and:

  | DB vs app | Result | Status bar message |
  |---|---|---|
  | same schema, data ≥ `MinData` | works | — |
  | DB schema > app's | **update the app** | `Status_Db_AppTooOld` |
  | DB schema < app's, or data < `MinData` | **update the DB** | `Status_Db_DbTooOld` |
  | no DB / unreadable | data missing | `Status_Db_Missing` |

Within one schema the DB only grows (new rows / tables / nullable columns), so any app built for schema `S` keeps
working with every later DB release of schema `S`. A breaking change raises `S`; app and DB then ship together.

The app repo carries its own copy of `pokemon.db` (a complete package). `db.lock` records which published release of
`max-potion-database` that copy is (version + SHA-256); CI refuses to build if the copy was edited or drifted, and
`scripts/sync-db.py` brings in a newer release.
The version also appears in *About → System* and in bug reports (`Database` line).

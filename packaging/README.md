# Linux packages (.deb / .rpm)

One package, `maxpotion`, with the app, `pokemon.db` and all language packs (no separate language package).

- **Edit the text people see on install** in `package.conf` (name, one-line summary, homepage, maintainer, dependencies)
  and `description.txt` (long description; blank line = new paragraph; lines starting with `- ` are shown as a list).
  `maxpotion.desktop` holds the menu entry text (and its translations).
- Preview exactly what `apt show` / `rpm -qi` will display, without building:
  `python3 scripts/build_linux_packages.py --preview 1.0.0`
- Build (needs `dpkg-deb` for the .deb and `rpmbuild` for the .rpm; each is skipped if its tool is missing):
  `python3 scripts/build_linux_packages.py 1.0.0 build/publish dist`

Installed layout: `/usr/lib/maxpotion/maxpotion` (+ symlink `/usr/bin/maxpotion`), `/usr/share/maxpotion/pokemon.db`,
`/usr/share/maxpotion/lang/*.json`, menu entry, 256 px icon, `/usr/share/doc/maxpotion/` (LICENSE, THIRD-PARTY-NOTICES.md).

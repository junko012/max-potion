#!/usr/bin/env python3
"""Arma MaxPotion-<versión>-x86_64.AppImage a partir de la salida de `dotnet publish` (linux-x64).

Uso:  python3 scripts/build_appimage.py <versión> <carpeta_publish> <carpeta_salida>
      python3 scripts/build_appimage.py --appdir-only <versión> <carpeta_publish> <carpeta_salida>
      (solo deja <salida>/MaxPotion.AppDir, sin llamar a appimagetool: sirve para revisar el contenido)

appimagetool se busca en la variable APPIMAGETOOL o en el PATH. En el CI se descarga antes (ver release.yml).
La app ya sabe encontrar pokemon.db y lang/ junto al ejecutable (AppContext.BaseDirectory), así que el AppDir
lleva la carpeta publish tal cual dentro de usr/lib/maxpotion/.
"""
import datetime, os, pathlib, shutil, subprocess, sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
PK = ROOT / "packaging"
APPRUN = r'''#!/bin/sh
# AppRun de Max Potion. Si el sistema no trae ICU (libicu), .NET arrancaría con error: en ese caso se usa el modo invariante.
HERE="$(dirname "$(readlink -f "$0")")"
if ! ldconfig -p 2>/dev/null | grep -q 'libicuuc\.so'; then
    export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
fi
exec "$HERE/usr/lib/maxpotion/maxpotion" "$@"
'''

def main(argv):
    only = "--appdir-only" in argv
    args = [a for a in argv[1:] if a != "--appdir-only"]
    if len(args) != 3:
        print(__doc__); return 2
    version, pub, out = args[0], pathlib.Path(args[1]).resolve(), pathlib.Path(args[2]).resolve()
    for need in ("maxpotion", "pokemon.db"):
        if not (pub / need).exists():
            print(f"falta {need} en {pub}"); return 1
    out.mkdir(parents=True, exist_ok=True)
    appdir = out / "MaxPotion.AppDir"
    if appdir.exists(): shutil.rmtree(appdir)
    lib = appdir / "usr/lib/maxpotion"
    shutil.copytree(pub, lib)
    os.chmod(lib / "maxpotion", 0o755)

    (appdir / "AppRun").write_text(APPRUN, encoding="utf-8"); os.chmod(appdir / "AppRun", 0o755)
    shutil.copy(PK / "maxpotion.desktop", appdir / "maxpotion.desktop")
    icon = ROOT / "src/Exxeguttor.UI/Assets/maxpotion.png"
    shutil.copy(icon, appdir / "maxpotion.png"); shutil.copy(icon, appdir / ".DirIcon")
    meta = appdir / "usr/share/metainfo"; meta.mkdir(parents=True)
    xml = (PK / "io.github.junko012.MaxPotion.metainfo.xml").read_text(encoding="utf-8")
    today = datetime.date.today().isoformat()
    xml = xml.replace("@VERSION@", version).replace("@DATE@", today)
    (meta / "io.github.junko012.MaxPotion.metainfo.xml").write_text(xml, encoding="utf-8")
    doc = appdir / "usr/share/doc/maxpotion"; doc.mkdir(parents=True)
    for f in ("LICENSE", "THIRD-PARTY-NOTICES.md"):
        if (ROOT / f).exists(): shutil.copy(ROOT / f, doc / f)
    print("AppDir listo:", appdir)
    if only: return 0

    tool = os.environ.get("APPIMAGETOOL") or shutil.which("appimagetool")
    if not tool:
        print("appimagetool no encontrado (defina APPIMAGETOOL o póngalo en el PATH)"); return 1
    target = out / f"MaxPotion-{version}-x86_64.AppImage"
    env = dict(os.environ, ARCH="x86_64")
    # --appimage-extract-and-run: el propio appimagetool es un AppImage y los runners de CI no tienen FUSE
    cmd = [tool, "--appimage-extract-and-run", str(appdir), str(target)] if tool.endswith(".AppImage") else [tool, str(appdir), str(target)]
    subprocess.run(cmd, check=True, env=env)
    shutil.rmtree(appdir, ignore_errors=True)
    print("OK ", target)
    return 0

if __name__ == "__main__":
    sys.exit(main(sys.argv))

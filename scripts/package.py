#!/usr/bin/env python3
"""Empaqueta la salida de `dotnet publish` (más pokemon.db) en el archivo de distribución de una plataforma.

Uso:  python scripts/package.py <rid> <versión> <carpeta_publish> <carpeta_salida>
  rid: linux-x64 | win-x64 | osx-arm64 | osx-x64
Salida: MaxPotion-<versión>-<rid>.tar.gz (linux), .zip (win), .dmg (macOS, requiere hdiutil/codesign: solo en macOS).
La carpeta publish ya debe traer pokemon.db y lang/ junto al ejecutable (el workflow los copia).
"""
import os, pathlib, shutil, subprocess, sys, tarfile, zipfile

EXE = {"linux-x64": "maxpotion", "win-x64": "maxpotion.exe", "osx-arm64": "maxpotion", "osx-x64": "maxpotion"}

PLIST = """<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleName</key><string>Max Potion</string>
  <key>CFBundleDisplayName</key><string>Max Potion</string>
  <key>CFBundleIdentifier</key><string>io.github.junko012.maxpotion</string>
  <key>CFBundleVersion</key><string>{v}</string>
  <key>CFBundleShortVersionString</key><string>{v}</string>
  <key>CFBundleExecutable</key><string>maxpotion</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>LSMinimumSystemVersion</key><string>11.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict></plist>
"""

def main(argv):
    if len(argv) != 5:
        print(__doc__); return 2
    rid, ver, pub, out = argv[1], argv[2], pathlib.Path(argv[3]), pathlib.Path(argv[4])
    if rid not in EXE:
        print("rid desconocido:", rid); return 2
    for need in (EXE[rid], "pokemon.db"):
        if not (pub / need).exists():
            print(f"falta {need} en {pub}"); return 1
    out.mkdir(parents=True, exist_ok=True)
    base = f"MaxPotion-{ver}-{rid}"
    stage = out / base
    if stage.exists(): shutil.rmtree(stage)

    if rid == "linux-x64":
        shutil.copytree(pub, stage)
        os.chmod(stage / "maxpotion", 0o755)
        target = out / f"{base}.tar.gz"
        with tarfile.open(target, "w:gz") as t: t.add(stage, arcname=base)
    elif rid == "win-x64":
        shutil.copytree(pub, stage)
        target = out / f"{base}.zip"
        with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED) as z:
            for p in sorted(stage.rglob("*")):
                if p.is_file(): z.write(p, p.relative_to(out))
    else:
        app = stage / "Max Potion.app"
        macos = app / "Contents" / "MacOS"
        shutil.copytree(pub, macos)
        os.chmod(macos / "maxpotion", 0o755)
        (app / "Contents" / "Info.plist").write_text(PLIST.format(v=ver))
        subprocess.run(["codesign", "--force", "--deep", "-s", "-", str(app)], check=True)  # firma ad-hoc (sin Apple Developer ID)
        target = out / f"{base}.dmg"
        subprocess.run(["hdiutil", "create", "-volname", "Max Potion", "-srcfolder", str(stage),
                        "-ov", "-format", "UDZO", str(target)], check=True)
    shutil.rmtree(stage, ignore_errors=True)
    print("OK ", target)
    return 0

if __name__ == "__main__":
    sys.exit(main(sys.argv))

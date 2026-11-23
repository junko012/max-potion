#!/usr/bin/env python3
"""Mantiene la pokemon.db que viaja DENTRO de este repo idéntica a una versión publicada en max-potion-database.

Uso:
  python scripts/sync-db.py <S.AAAA.MM.R>   descarga el release db-v<versión>, verifica SHA-256 y versión interna,
                                            reemplaza pokemon-database/database/pokemon.db y actualiza db.lock
  python scripts/sync-db.py --check         verifica que la DB del repo coincide con db.lock (lo corre el CI)
  --source-dir <carpeta>                    (con una versión) usa pokemon.db y pokemon.db.sha256 locales en vez de descargar
"""
import gzip, hashlib, json, pathlib, shutil, sqlite3, sys, urllib.request

ROOT = pathlib.Path(__file__).resolve().parent.parent
DB = ROOT / "pokemon-database" / "database" / "pokemon.db"
LOCK = ROOT / "db.lock"

def declared_version(path):
    con = sqlite3.connect(f"file:{path}?mode=ro", uri=True)
    meta = dict(con.execute("SELECT Key, Value FROM Metadata").fetchall()); con.close()
    p = meta["DataVersion"].split(".")
    return f"{meta['SchemaVersion']}.{p[0]}.{p[1]}.{p[2] if len(p) > 2 else 0}"

def sha256(path): return hashlib.sha256(pathlib.Path(path).read_bytes()).hexdigest()

def check():
    lock = json.loads(LOCK.read_text())
    if "TBD" in (lock["version"], lock["sha256"]):
        print("db.lock sin completar: publica la DB y corre  python scripts/sync-db.py <versión>"); return 1
    if sha256(DB) != lock["sha256"]:
        print("pokemon.db del repo NO coincide con db.lock (¿editada a mano? corre sync-db.py <versión>)"); return 1
    v = declared_version(DB)
    if v != lock["version"]:
        print(f"la DB declara {v} pero db.lock dice {lock['version']}"); return 1
    print(f"OK  pokemon.db {v} coincide con {lock['repo']} db-v{v}"); return 0

def sync(ver, src_dir):
    lock = json.loads(LOCK.read_text())
    tmp = DB.with_suffix(".db.new")
    if src_dir:
        shutil.copy(src_dir / "pokemon.db", tmp)
        sums = (src_dir / "pokemon.db.sha256").read_text()
    else:
        base = f"https://github.com/{lock['repo']}/releases/download/db-v{ver}/"
        urllib.request.urlretrieve(base + "pokemon.db.gz", str(tmp) + ".gz")
        with gzip.open(str(tmp) + ".gz", "rb") as f, open(tmp, "wb") as o: shutil.copyfileobj(f, o)
        pathlib.Path(str(tmp) + ".gz").unlink()
        sums = urllib.request.urlopen(base + "pokemon.db.sha256").read().decode()
    expected = next(l.split()[0] for l in sums.splitlines() if l.strip().endswith("pokemon.db"))
    if sha256(tmp) != expected:
        tmp.unlink(); print("SHA-256 no coincide con el publicado"); return 1
    v = declared_version(tmp)
    if v != ver:
        tmp.unlink(); print(f"la DB declara {v} pero pediste {ver}"); return 1
    tmp.replace(DB)
    lock["version"], lock["sha256"] = ver, expected
    LOCK.write_text(json.dumps(lock, indent=2) + "\n")
    print(f"OK  pokemon-database/database/pokemon.db = {ver}; db.lock actualizado"); return 0

def main(argv):
    if len(argv) < 2: print(__doc__); return 2
    if argv[1] == "--check": return check()
    src = pathlib.Path(argv[argv.index("--source-dir") + 1]) if "--source-dir" in argv else None
    return sync(argv[1], src)

if __name__ == "__main__":
    sys.exit(main(sys.argv))

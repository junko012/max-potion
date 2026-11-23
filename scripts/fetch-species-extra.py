#!/usr/bin/env python3
"""
fetch-species-extra.py
Trae de PokeAPI los datos que hoy NO están en pokemon.db: género y descripción de
Pokédex, grupos huevo, cadena de evolución completa, y ubicaciones de encuentro en la
naturaleza (donde PokeAPI las tenga — no hay datos de encuentro para Gen 8/9, ver nota
abajo). Compatible con Python 3.6+, no requiere librerías externas (mismo criterio que
scripts/download-sprites.py).

Uso:
    python3 scripts/fetch-species-extra.py
    python3 scripts/fetch-species-extra.py --start 1 --end 151   # solo Kanto, para probar rápido
    python3 scripts/fetch-species-extra.py --resume              # sigue donde quedó si se cortó

Salida:
    pokemon-database/resources/species_extra.json

Nota sobre cobertura:
    - Género/descripción/grupos huevo/evolución: PokeAPI los tiene para todas las
      generaciones soportadas por este proyecto.
    - Texto en español: PokeAPI solo tiene flavor text en "es" desde X/Y (Gen 6) en
      adelante — confirmado con datos reales (Bulbasaur no tiene "es" para
      emerald/ruby/sapphire/diamond/pearl/platinum/heartgold/soulsilver/black/white,
      pero sí para x/omega-ruby/alpha-sapphire/sword). Para juegos anteriores a Gen 6,
      el campo "es" de flavor_text va a estar vacío para esa versión — hay que armar el
      fallback a "en" en la app, no en este script (este script solo trae lo que hay).
    - Ubicaciones de encuentro: PokeAPI NO tiene datos de encuentro para Espada/Escudo
      (Gen 8) ni Escarlata/Púrpura (Gen 9) — confirmado contra un issue abierto en el
      repo de PokeAPI, no es un hueco temporal, hay que asumir que esos juegos van a
      quedar con lista vacía siempre. El resto de las generaciones (1-7) sí traen datos,
      en algunos casos bastante detallados (zona, método, nivel, probabilidad).

No decide nada sobre CÓMO cargar esto a pokemon.db — ese es un paso aparte,
deliberadamente, para poder revisar el JSON primero.
"""

import argparse
import json
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

API = "https://pokeapi.co/api/v2"
OUT = Path("pokemon-database/resources/species_extra.json")
MAX_SPECIES_ID = 1025          # especies base — mismo límite que ya usa el proyecto
                                 # (ver PokemonDatabase.GetAllSpeciesSummaries: SpeciesId < 10000
                                 # excluye formas alternativas, que viven en 10001+)
REQUEST_DELAY_SECONDS = 0.15    # buena práctica con una API pública gratuita sin auth —
                                 # no hay límite documentado de PokeAPI, pero tampoco hace
                                 # falta martillarla; ~1000 especies a este ritmo son ~5-10 min


def fetch_json(url):
    """GET simple con reintento único — devuelve None si falla dos veces (no aborta todo el
    script por una especie puntual con problemas de red transitorios)."""
    for attempt in range(2):
        try:
            req = urllib.request.Request(url, headers={"User-Agent": "max-potion-data-fetch/1.0"})
            with urllib.request.urlopen(req, timeout=15) as r:
                return json.loads(r.read().decode())
        except urllib.error.HTTPError as e:
            if e.code == 404:
                return None
            if attempt == 0:
                time.sleep(1)
                continue
            print(f"\n  HTTPError {e.code} en {url}")
            return None
        except Exception as e:
            if attempt == 0:
                time.sleep(1)
                continue
            print(f"\n  Error en {url}: {e}")
            return None
    return None


def bar(i, total, label):
    pct = int(i * 100 / total) if total else 0
    filled = pct // 5
    b = "#" * filled + "-" * (20 - filled)
    sys.stdout.write(f"\r  [{b}] {pct:3d}% {label[:30]:<30}")
    sys.stdout.flush()


def extract_genus_and_flavor(species_json):
    """Género y descripción — solo se guardan es/en (el resto de idiomas de PokeAPI no
    se usa en ningún lado de la app hoy, no hace falta cargar el JSON con 9 idiomas)."""
    genus = {}
    for g in species_json.get("genera", []):
        lang = g["language"]["name"]
        if lang in ("es", "en"):
            genus[lang] = g["genus"]

    flavor_by_lang = {}
    for entry in species_json.get("flavor_text_entries", []):
        lang = entry["language"]["name"]
        if lang not in ("es", "en"):
            continue
        version = entry["version"]["name"]
        text = entry["flavor_text"].replace("\n", " ").replace("\f", " ").strip()
        flavor_by_lang.setdefault(lang, {})
        # Quedarse con la primera entrada por versión (PokeAPI a veces repite la misma
        # versión más de una vez con variantes menores de texto)
        flavor_by_lang[lang].setdefault(version, text)

    return genus, flavor_by_lang


def flatten_evolution_chain(chain_node, from_id=None):
    """Aplana el árbol anidado de PokeAPI (chain -> evolves_to -> evolves_to -> ...) a una
    lista plana de aristas (de especie, a especie, condición). Recursivo porque una especie
    puede tener más de una evolución posible (ramas, ej. Eevee)."""
    edges = []
    species_id = int(chain_node["species"]["url"].rstrip("/").split("/")[-1])

    for detail in chain_node.get("evolution_details", []):
        edges.append({
            "from_species_id": from_id,
            "to_species_id": species_id,
            "trigger": (detail.get("trigger") or {}).get("name"),
            "min_level": detail.get("min_level"),
            "item": (detail.get("item") or {}).get("name"),
            "held_item": (detail.get("held_item") or {}).get("name"),
            "min_happiness": detail.get("min_happiness"),
            "known_move": (detail.get("known_move") or {}).get("name"),
            "time_of_day": detail.get("time_of_day") or None,
            "location": (detail.get("location") or {}).get("name"),
        })

    for child in chain_node.get("evolves_to", []):
        edges.extend(flatten_evolution_chain(child, from_id=species_id))

    return edges


def extract_encounters(encounters_json):
    """Lista de encuentros silvestres — vacía para Gen 8/9 (ver nota al inicio del
    archivo), no es un bug si sale así para esos juegos."""
    out = []
    for loc in encounters_json or []:
        location_area = loc["location_area"]["name"]
        for vd in loc.get("version_details", []):
            version = vd["version"]["name"]
            for detail in vd.get("encounter_details", []):
                out.append({
                    "version": version,
                    "location_area": location_area,
                    "method": (detail.get("method") or {}).get("name"),
                    "min_level": detail.get("min_level"),
                    "max_level": detail.get("max_level"),
                    "chance": detail.get("chance"),
                })
    return out


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--start", type=int, default=1)
    parser.add_argument("--end", type=int, default=MAX_SPECIES_ID)
    parser.add_argument("--resume", action="store_true",
                         help="si species_extra.json ya existe, no vuelve a pedir las especies que ya están")
    args = parser.parse_args()

    OUT.parent.mkdir(parents=True, exist_ok=True)

    data = {"species_extra": {}, "evolution_chains": {}, "encounters": {}}
    if args.resume and OUT.exists():
        data = json.loads(OUT.read_text(encoding="utf-8"))
        print(f"Reanudando — {len(data['species_extra'])} especies ya en el archivo.")

    chain_cache = {}  # url de cadena de evolución -> ya procesada (varias especies comparten cadena)
    species_range = range(args.start, args.end + 1)
    total = len(species_range)

    for i, species_id in enumerate(species_range, start=1):
        key = str(species_id)
        if args.resume and key in data["species_extra"]:
            bar(i, total, f"#{species_id} (ya estaba)")
            continue

        species_json = fetch_json(f"{API}/pokemon-species/{species_id}")
        if species_json is None:
            bar(i, total, f"#{species_id} sin datos")
            time.sleep(REQUEST_DELAY_SECONDS)
            continue

        genus, flavor = extract_genus_and_flavor(species_json)
        egg_groups = [g["name"] for g in species_json.get("egg_groups", [])]
        habitat = (species_json.get("habitat") or {}).get("name")

        data["species_extra"][key] = {
            "genus": genus,
            "flavor_text": flavor,
            "egg_groups": egg_groups,
            "habitat": habitat,
        }

        # Cadena de evolución — una sola vez por cadena, no por especie
        chain_url = (species_json.get("evolution_chain") or {}).get("url")
        if chain_url and chain_url not in chain_cache:
            chain_json = fetch_json(chain_url)
            if chain_json:
                chain_id = int(chain_url.rstrip("/").split("/")[-1])
                edges = flatten_evolution_chain(chain_json["chain"])
                data["evolution_chains"][str(chain_id)] = edges
                chain_cache[chain_url] = chain_id
            time.sleep(REQUEST_DELAY_SECONDS)

        # Encuentros — vacío para Gen 8/9, esperado (ver nota al inicio del archivo)
        encounters_json = fetch_json(f"{API}/pokemon/{species_id}/encounters")
        data["encounters"][key] = extract_encounters(encounters_json)

        bar(i, total, species_json.get("name", f"#{species_id}"))
        time.sleep(REQUEST_DELAY_SECONDS)

        # Guardar cada 25 especies — si se corta la conexión a mitad de camino, no se
        # pierde todo el progreso (se puede seguir con --resume)
        if i % 25 == 0:
            OUT.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")

    OUT.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"\n\nListo — {len(data['species_extra'])} especies, "
          f"{len(data['evolution_chains'])} cadenas de evolución únicas.")
    print(f"Guardado en: {OUT}")


if __name__ == "__main__":
    main()

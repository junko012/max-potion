#!/usr/bin/env bash
# Firma con GPG los artefactos de un release de Max Potion.
#
# Uso:  bash scripts/sign_release.sh rpm       <carpeta_dist>   # firma los .rpm (rpmsign)
#       bash scripts/sign_release.sh checksums <carpeta_dist>   # firma SHA256SUMS.txt y publica la clave pública
#
# Variables de entorno (secrets de GitHub):
#   GPG_PRIVATE_KEY   clave privada en base64 (gpg --export-secret-keys --armor <id> | base64 -w0)
#   GPG_PASSPHRASE    contraseña de la clave
# Si no están definidas, el script avisa y sale con éxito SIN firmar (ensayos con workflow_dispatch).
# Con REQUIRE_SIGNING=1 (releases reales, tags v*) falla en su lugar, y en modo rpm además comprueba que cada .rpm
# quedó realmente firmado (`rpmkeys --checksig` también da OK para un rpm sin firma: solo comprueba los hashes).
# Ver docs/SIGNING.md.
set -euo pipefail

mode="${1:-}"; dist="${2:-dist}"
if [[ -z "${GPG_PRIVATE_KEY:-}" || -z "${GPG_PASSPHRASE:-}" ]]; then
  # En un release real (tag) REQUIRE_SIGNING=1: sin secrets el job FALLA en vez de publicar paquetes sin firmar.
  if [[ "${REQUIRE_SIGNING:-}" == "1" ]]; then
    echo "::error::GPG_PRIVATE_KEY / GPG_PASSPHRASE no definidos y REQUIRE_SIGNING=1: no se publica sin firma."
    exit 1
  fi
  echo "::notice::GPG_PRIVATE_KEY / GPG_PASSPHRASE no definidos: se omite la firma."
  exit 0
fi

work="$(mktemp -d)"; trap 'rm -rf "$work"' EXIT
export GNUPGHOME="$work/gnupg"; mkdir -m 700 "$GNUPGHOME"
printf '%s' "$GPG_PASSPHRASE" > "$work/pass"; chmod 600 "$work/pass"

printf '%s' "$GPG_PRIVATE_KEY" | base64 -d | gpg --batch --quiet --import
fpr="$(gpg --batch --with-colons --list-secret-keys | awk -F: '/^fpr:/ {print $10; exit}')"
[[ -n "$fpr" ]] || { echo "No se pudo importar la clave privada"; exit 1; }
echo "Clave de firma: $fpr"
gpg --batch --armor --export "$fpr" > "$work/pub.asc"

case "$mode" in
  rpm)
    shopt -s nullglob; rpms=("$dist"/*.rpm)
    (( ${#rpms[@]} )) || { echo "No hay .rpm en $dist"; exit 1; }
    # Se deja el comando de firma POR DEFECTO de rpm (cada versión lo arma a su manera: nombre del archivo o entrada
    # estándar, un «gpg» repetido como argv[0], etc.) y solo se sustituye el programa `gpg` por un envoltorio que
    # descarta ese «gpg» sobrante y añade las opciones no interactivas (contraseña por archivo, sin pinentry).
    cat > "$work/gpgwrap" <<WRAP
#!/bin/sh
[ "\$1" = "gpg" ] && shift
exec $(command -v gpg) --batch --pinentry-mode loopback --passphrase-file "$work/pass" "\$@"
WRAP
    chmod 700 "$work/gpgwrap"
    for f in "${rpms[@]}"; do
      rpmsign \
        --define "_gpg_name $fpr" \
        --define "_gpg_digest_algo sha256" \
        --define "__gpg $work/gpgwrap" \
        --addsign "$f"
    done
    # Verificación con una base de datos de rpm temporal
    mkdir -p "$work/rpmdb"
    rpmkeys --dbpath "$work/rpmdb" --import "$work/pub.asc"
    for f in "${rpms[@]}"; do
      rpmkeys --dbpath "$work/rpmdb" --checksig "$f"
      sig="$(rpm -qp --qf '%{RSAHEADER:pgpsig}' "$f" 2>/dev/null || true)"
      if [[ -z "$sig" || "$sig" == "(none)" ]]; then
        echo "::error::$f quedó SIN firma (rpmsign no añadió la firma RSA)."
        exit 1
      fi
      echo "Firma del rpm: $sig"
    done
    ;;
  checksums)
    [[ -f "$dist/SHA256SUMS.txt" ]] || { echo "Falta $dist/SHA256SUMS.txt"; exit 1; }
    gpg --batch --yes --pinentry-mode loopback --passphrase-file "$work/pass" \
        --armor --detach-sign -u "$fpr" -o "$dist/SHA256SUMS.txt.asc" "$dist/SHA256SUMS.txt"
    gpg --batch --verify "$dist/SHA256SUMS.txt.asc" "$dist/SHA256SUMS.txt"
    cp "$work/pub.asc" "$dist/maxpotion-signing-key.asc"
    ;;
  *) echo "Uso: $0 rpm|checksums <carpeta_dist>"; exit 2 ;;
esac
echo "Firma completada ($mode)."

# Firma de los paquetes (GPG)

Los releases pueden firmarse con una clave GPG del proyecto:

- los **`.rpm`** se firman con `rpmsign` (así el gestor de paquetes los reconoce una vez importada la clave);
- **`SHA256SUMS.txt`** se firma con una firma separada (`SHA256SUMS.txt.asc`), y la clave pública se adjunta al release como `maxpotion-signing-key.asc`.

Si los secrets no están definidos, `scripts/sign_release.sh` no firma y el release sale igual (sin firma).

## 1. Crear la clave (una sola vez, en tu máquina)

```bash
gpg --full-generate-key
```
Elige **ECC (ed25519)** o RSA 4096, caducidad de 2 a 3 años y como nombre `Max Potion Release Signing` con tu correo `noreply` de GitHub. Pon una contraseña larga.

Anota el ID largo (fingerprint) con `gpg --list-secret-keys --keyid-format LONG`.

## 2. Copias de seguridad (importante)

```bash
gpg --armor --export-secret-keys <ID> > maxpotion-privada.asc     # guárdala FUERA del repo y fuera de línea
gpg --gen-revoke <ID> > maxpotion-revocacion.asc                  # para anular la clave si se pierde o se filtra
gpg --armor --export <ID> > maxpotion-signing-key.asc             # la pública (esta sí es pública)
```
Nunca subas la privada ni la contraseña al repositorio ni las pegues en ningún chat.

## 3. Secrets en GitHub

Settings → Secrets and variables → Actions → New repository secret:

| Nombre | Valor |
|---|---|
| `GPG_PRIVATE_KEY` | salida de `gpg --export-secret-keys --armor <ID> \| base64 -w0` |
| `GPG_PASSPHRASE` | la contraseña de la clave |

## 4. Workflow (`.github/workflows/release.yml`)

En el job `build`, tras «Linux packages (.deb / .rpm)» (y antes de subir el artifact):

```yaml
      - name: Sign .rpm
        if: matrix.rid == 'linux-x64'
        env:
          GPG_PRIVATE_KEY: ${{ secrets.GPG_PRIVATE_KEY }}
          GPG_PASSPHRASE: ${{ secrets.GPG_PASSPHRASE }}
        run: bash scripts/sign_release.sh rpm dist
```

En el job `release`, justo después del paso «Checksums»:

```yaml
      - name: Sign checksums
        env:
          GPG_PRIVATE_KEY: ${{ secrets.GPG_PRIVATE_KEY }}
          GPG_PASSPHRASE: ${{ secrets.GPG_PASSPHRASE }}
        run: bash scripts/sign_release.sh checksums dist
```

`gh release create dist/*` ya incluye los nuevos archivos (`.asc` y la clave pública).

## 5. Qué ven los usuarios

Añadir al README (Instalación):

```markdown
### Verificar las firmas
Descarga `maxpotion-signing-key.asc` del release y comprueba la huella: `gpg --show-keys maxpotion-signing-key.asc`
(la huella correcta es: <PEGA AQUÍ LA HUELLA DE TU CLAVE>).

- **Paquete .rpm firmado:** importa la clave una vez con `sudo rpm --import maxpotion-signing-key.asc` y el aviso de «sin firma» desaparece.
- **Checksums:** `gpg --import maxpotion-signing-key.asc && gpg --verify SHA256SUMS.txt.asc SHA256SUMS.txt`
```
Publica también la huella en otro sitio que controles (el README, tu perfil de GitHub), para que quien verifique pueda comparar.

## 6. Mantenimiento

- **Caducidad:** antes de que venza, extiende la fecha (`gpg --edit-key <ID>` → `expire`), exporta la pública de nuevo y actualiza el secret si cambia la privada.
- **Si se filtra o se pierde:** publica el certificado de revocación, crea una clave nueva y avisa en el README.
- Con un repositorio de **OBS** el servicio firma con su propia clave del proyecto; esa clave será la que importen los usuarios de ese repo, no esta.

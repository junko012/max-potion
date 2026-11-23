# Changelog — Max Potion (app)

Formato: [SemVer](https://semver.org/). La base de datos tiene su propio changelog en
[max-potion-database](https://github.com/junko012/max-potion-database).

## [1.0.0] — primera versión pública

- Editor de save files de Pokémon nativo de Linux (Avalonia / .NET 9 / PKHeX.Core); también para Windows y macOS.
- Equipo, cajas, Pokémon (IV/EV, movimientos, naturaleza, habilidad, objeto, cintas, origen…), mochila y entrenador.
- Interfaz en 13 idiomas.
- Base de datos incluida (`pokemon.db`); la app comprueba su compatibilidad al arrancar y funciona en solo lectura.
- Campos numéricos y de fecha limitados al tipo y rango válidos.
- Paquetes: `.tar.gz`, `.deb`, `.rpm` (Linux), `.zip` (Windows) y `.dmg` (macOS, sin notarizar).

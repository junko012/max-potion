# Sprites

Sprites de Pokémon, ítems y tipos incluidos como parte permanente del proyecto.
Embebidos en el binario final durante el build.

## Contenido

| Carpeta | Contenido | Cantidad |
|---|---|---|
| `pokemon/normal/` | Sprites normales por ID | 1025 |
| `pokemon/shiny/` | Sprites shiny por ID | 1025 |
| `pokemon/female/` | Sprites variante femenina | 101 |
| `pokemon/shiny-female/` | Sprites shiny femeninos | 100 |
| `pokemon/forms/` | Formas guardables con sprite propio (regionales, Giratina Origen, Deoxys, Rotom... y cosméticas: Unown, Arceus, Vivillon...), `{especie}_{forma}.png` | 267 |
| `pokemon/forms/shiny/` | Las mismas formas, shiny | 267 |
| `items/` | Sprites de ítems por nombre | 1093 |
| `types/` | Badges de tipos | 18 |
| `games/` | Portadas por juego (Pantalla 1 — Recientes) | 38 — ver abajo |

## Formas (`pokemon/forms/`)

Una por cada forma que `Exxeguttor.App.Services.SpeciesFormResolver` resuelve a una fila propia de
forma en pokemon.db (las 57 de `RegionalFormCatalog.All` + 64 más: Giratina Origen, Deoxys, Rotom,
Therian, Basculin, Meowstic hembra...), nombrada `{id de especie}_{índice de forma de PKHeX}.png`
(ej. `105_1.png` = Marowak de Alola, `487_1.png` = Giratina Origen). Se copian de PokeAPI/sprites
(`pokemon/{id de fila}.png`).

Además hay 146 formas cosméticas (Unown, Arceus y Silvally por tipo, Vivillon, Alcremie, Furfrou,
Flabébé/Floette/Florges, Deerling/Sawsbuck, Burmy, Shellos/Gastrodon, Genesect, gorras de Pikachu,
Tatsugiri, Squawkabilly, Dudunsparce, Maushold...) copiadas de `pokemon/{id}-{nombre}.png` o de la
fila de forma de la DB. Sin sprite en PokeAPI y por eso sin archivo propio (usan el de la especie):
Scatterbug y Spewpa por patrón, Mothim por manto, Koraidon y Miraidon por modo. El sprite base
de Maushold (`925.png`) es la Familia de Cuatro, y en PKHeX la forma 0 es la de Tres: por eso
`925_0.png` existe y `SpriteService` lo busca aunque la forma sea 0.

Si falta el sprite de una forma, `SpriteService` cae al de la especie base. La lista del catálogo es fija:
si una versión nueva de PKHeX.Core o de la DB agrega formas, hay que agregar sus sprites aquí (y,
si son regionales, sumarlas al catálogo) (los tests de `RegionalFormCreationTests` avisan si los índices se
desalinean).

## Portadas por juego (`games/`)

Un `.png` por juego, nombrado con el código crudo en minúscula de
`SaveFile.Version.ToString()` en PKHeX.Core (ej. `rd.png`, `si.png`, `sv.png`, `za.png`) — la
lista completa de códigos está documentada en
`Exxeguttor.UI.ViewModels.TrainerViewModel.ExpandGameName`. Hoy hay 38 portadas (`za.png` se generó con `scripts/gen-game-cover-za.py`, el resto son
de la carpeta tal como está); mientras falte el `.png` de un juego, `GameCoverService` dibuja un fallback
procedural (gradiente + glyph por generación) en vez de dejar la fila sin portada — agregar el
archivo correspondiente alcanza para que se use automáticamente, no hace falta tocar código
(`SpriteService.GetGameCoverSprite` ya busca en esta carpeta por convención).

## Fuente

- URL: https://github.com/PokeAPI/sprites
- Sprites: © Nintendo / Creatures Inc. / GAME FREAK Inc.

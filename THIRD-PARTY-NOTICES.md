# Third-party notices

Max Potion (repository: `max-potion`) is released under the **GPL-3.0-or-later** (see [LICENSE](LICENSE)).
It includes or depends on the third-party components below. This file is provided for attribution; it is not legal advice.

The same information is available inside the app under **Help → About → Credits / Licenses**.

## Components

| Component | Used for | License |
|---|---|---|
| [PKHeX.Core](https://github.com/kwsch/PKHeX) — Kurt (kwsch) and contributors | Save file reading/writing, legality checks, game data | GPL-3.0 |
| [Avalonia UI](https://github.com/AvaloniaUI/Avalonia) (`Avalonia`, `.Desktop`, `.Themes.Fluent`, `.Fonts.Inter`, `.ReactiveUI`) — AvaloniaUI OÜ and contributors | User interface framework | MIT (Inter font: SIL OFL 1.1) |
| [Microsoft.Data.Sqlite](https://github.com/dotnet/efcore) and the .NET runtime — .NET Foundation and contributors | Local Pokémon database access; runtime | MIT |
| [PokéAPI](https://github.com/PokeAPI/pokeapi) — Paul Hallett and PokéAPI contributors | Species, moves, abilities, items, evolution and machine data (`pokemon.db`); item descriptions | BSD-3-Clause |
| [Press Start 2P](https://fonts.google.com/specimen/Press+Start+2P) — The Press Start 2P Project Authors | Splash screen font | SIL OFL 1.1 (see `src/Exxeguttor.UI/Assets/fonts/PressStart2P-LICENSE.txt`) |

## Game content and data (not covered by the software license)

- **Pokémon** and its names, sprites, artwork and game data are trademarks and/or copyrighted works of Nintendo, Creatures Inc. and GAME FREAK inc. Max Potion is a fan project with no affiliation or endorsement.
- **Sprites and item/type icons** under `src/Exxeguttor.UI/Assets/sprites/` are fetched from [PokeAPI/sprites](https://github.com/PokeAPI/sprites) (`scripts/download-sprites.py`). They remain the property of their owners; the GPL of this project does **not** relicense them.
- **Item descriptions** that PokéAPI does not provide were gathered from community references (Serebii, Bulbapedia, GameGuides) with AI assistance, and are English-only (see `pokemon-database/DATA_SOURCES.md`). Their original licenses/terms apply.
- **Competitive movesets** (`movesets_all_combined.json`) come from community competitive resources; see `pokemon-database/resources/`.
- **UI translations** (13 languages) were produced by Claude (Anthropic, AI assistant) without native-speaker review.

## License texts

The full GPL-3.0 text is in [LICENSE](LICENSE). The licenses of the permissive components follow.

### Avalonia UI — MIT

The MIT License (MIT)

Copyright (c) AvaloniaUI OÜ
All Rights Reserved

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

### .NET / Microsoft.Data.Sqlite — MIT

The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors

All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
### PokéAPI — BSD-3-Clause

Copyright (c) © 2013–2023 Paul Hallett and PokéAPI contributors (https://github.com/PokeAPI/pokeapi#contributing). Pokémon and Pokémon character names are trademarks of Nintendo.

All rights reserved.

Redistribution and use in source and binary forms, with or without modification, are permitted provided that the following conditions are met:

* Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer.

* Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimer in the documentation and/or other materials provided with the distribution.

* Neither the name of PokéAPI nor the names of its contributors may be used to endorse or promote products derived from this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.


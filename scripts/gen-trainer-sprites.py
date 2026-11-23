#!/usr/bin/env python3
"""
Genera los íconos de entrenador/entrenadora (tarjeta "Entrenador" del modal de exportación).

Arte ORIGINAL dibujado por código (formas planas, sin sprites ni arte oficial de ningún juego):
un busto estilizado por juego/familia de juegos — mismos tonos de ropa y accesorios de cabeza
que identifican a cada protagonista, pero sin copiar ilustraciones. Salida: PNG 192×192 con
transparencia en src/Exxeguttor.UI/Assets/sprites/trainers/<clave>.<male|female>.png
(csproj los embebe solos: Assets\\sprites\\**\\*.png) — SpriteService.GetTrainerSprite los resuelve.

Géneros que existen en el juego (verificado: PKHeX guarda 0/1; no hay un tercer valor):
  - RBY y Oro/Plata: solo masculino (no se elige género) → solo se genera "male".
  - Cristal en adelante: masculino/femenino. SV y Z-A siguen siendo binarios (look masculino/femenino).

Uso:  python3 scripts/gen-trainer-sprites.py
"""
import os
from PIL import Image, ImageDraw

OUT = os.path.join(os.path.dirname(__file__), "..", "src", "Exxeguttor.UI", "Assets", "sprites", "trainers")
S = 960            # lienzo de trabajo (supersampling x5) → se reduce a 192
FINAL = 192
K = S / 100.0      # unidades 0..100 → píxeles

def px(v): return v * K
def box(x0, y0, x1, y1): return [px(x0), px(y0), px(x1), px(y1)]
def poly(pts): return [(px(x), px(y)) for x, y in pts]

SKIN = {"fair": ("#F6D2B5", "#E3B692"), "tan": ("#E2B089", "#C9936C"), "dark": ("#A87550", "#8C5D3D")}

def draw_figure(p):
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    skin, skin_sh = SKIN[p.get("skin", "fair")]
    hair, top = p["hair"], p["top"]
    style = p.get("style", "short")

    # ── pelo trasero ──
    if style in ("long", "twin", "ponytail", "bob", "buns"):
        if style == "long":
            d.rounded_rectangle(box(24, 24, 76, 82), radius=px(14), fill=hair)
        elif style == "bob":
            d.rounded_rectangle(box(24, 22, 76, 60), radius=px(12), fill=hair)
        elif style == "twin":
            for x0, x1 in ((13, 31), (69, 87)):
                d.rounded_rectangle(box(x0, 40, x1, 80), radius=px(9), fill=hair)
        elif style == "ponytail":
            d.ellipse(box(62, 26, 86, 64), fill=hair)
        elif style == "buns":
            for cx in (27, 73):
                d.ellipse(box(cx - 9, 10, cx + 9, 28), fill=hair)

    # ── torso ──
    d.ellipse(box(10, 70, 90, 150), fill=top)
    if p.get("vest"):
        d.polygon(poly([(34, 75), (50, 100), (66, 75), (72, 100), (28, 100)]), fill=p["vest"])
    if p.get("stripe"):
        d.rectangle(box(22, 90, 78, 95), fill=p["stripe"])
    d.polygon(poly([(40, 70), (50, 86), (60, 70)]), fill=skin_sh)          # escote
    if p.get("collar"):
        d.polygon(poly([(36, 70), (50, 90), (44, 70)]), fill=p["collar"])
        d.polygon(poly([(64, 70), (50, 90), (56, 70)]), fill=p["collar"])
    if p.get("straps"):
        for x in (30, 70):
            d.line([px(x), px(76), px(x - 2 if x < 50 else x + 2), px(110)], fill=p["straps"], width=int(px(3.4)))
    if p.get("scarf"):
        d.rounded_rectangle(box(30, 64, 70, 76), radius=px(6), fill=p["scarf"])
        d.rounded_rectangle(box(56, 70, 66, 92), radius=px(4), fill=p["scarf"])

    # ── cuello, cabeza, orejas ──
    d.rectangle(box(43, 56, 57, 72), fill=skin_sh)
    for x0 in (25, 73):
        d.ellipse(box(x0, 42, x0 + 4, 52), fill=skin_sh)
    d.ellipse(box(27, 20, 73, 66), fill=skin)

    # ── cara ──
    for cx in (40, 60):
        d.ellipse(box(cx - 2.1, 44, cx + 2.1, 50.5), fill="#2B2A33")
        d.ellipse(box(cx - 0.4, 44.8, cx + 0.9, 46.2), fill="#FFFFFF")
    d.arc(box(44, 51, 56, 59), 20, 160, fill="#8A4B3A", width=int(px(0.9)))
    if p.get("blush"):
        for cx in (35, 65):
            d.ellipse(box(cx - 3.5, 52, cx + 3.5, 56.5), fill=(235, 130, 130, 110))

    # ── flequillo / pelo ──
    if style != "none":
        d.polygon(poly([(26, 44), (27, 28), (35, 18), (50, 14), (65, 18), (73, 28), (74, 44),
                        (66, 33), (56, 36), (47, 31), (36, 36)]), fill=hair)
        if style in ("long", "bob", "twin", "buns", "ponytail"):
            d.ellipse(box(26, 26, 36, 50), fill=hair)
            d.ellipse(box(64, 26, 74, 50), fill=hair)

    # ── cabeza: accesorio ──
    hw, c1, c2 = p.get("head"), p.get("hc1", "#D33"), p.get("hc2", "#FFF")
    if hw == "cap":
        d.pieslice(box(25, 8, 75, 64), 180, 360, fill=c1)
        d.ellipse(box(28, 34, 72, 48), fill=p.get("brim", c2))
        d.ellipse(box(46, 22, 54, 30), fill=c2)                     # logo
        d.ellipse(box(48.5, 8, 51.5, 11), fill=c2)
    elif hw == "capback":
        d.pieslice(box(25, 8, 75, 64), 180, 360, fill=c1)
        d.polygon(poly([(70, 36), (82, 30), (86, 38), (72, 42)]), fill=p.get("brim", c2))
    elif hw == "beanie":
        d.pieslice(box(25, 6, 75, 66), 180, 360, fill=c1)
        d.rounded_rectangle(box(25, 30, 75, 41), radius=px(5), fill=c2)
        d.ellipse(box(44, 1, 56, 13), fill=c2)
    elif hw == "bandana":
        d.pieslice(box(25, 10, 75, 62), 180, 360, fill=c1)
        d.polygon(poly([(26, 34), (74, 34), (74, 41), (26, 41)]), fill=c2)
        d.polygon(poly([(72, 36), (86, 30), (84, 44)]), fill=c2)
    elif hw == "hat":
        d.ellipse(box(14, 28, 86, 46), fill=c2)
        d.pieslice(box(30, 6, 70, 56), 180, 360, fill=c1)
        d.rectangle(box(30, 27, 70, 32), fill=p.get("hc3", "#555"))
    elif hw == "visor":
        d.rounded_rectangle(box(25, 28, 75, 36), radius=px(4), fill=c1)
        d.polygon(poly([(34, 34), (66, 34), (74, 46), (26, 46)]), fill=c2)
    elif hw == "beret":
        d.ellipse(box(22, 8, 78, 38), fill=c1)
        d.ellipse(box(47, 5, 53, 11), fill=c2)
    elif hw == "headband":
        d.rounded_rectangle(box(26, 29, 74, 36), radius=px(3), fill=c1)

    return img.resize((FINAL, FINAL), Image.LANCZOS)

# ── Definiciones por juego / familia ──────────────────────────────────────────────
# clave → { "male": params, "female": params | ausente }.  Paleta orientativa por protagonista.
BLUE, RED, WHITE, DARK = "#2F5FA8", "#D6453D", "#F4F4F0", "#2B2B36"
D = {
 "g1":   {"male": dict(skin="fair", hair="#3A2A24", top=BLUE, vest=RED, head="cap", hc1=RED, hc2=WHITE, brim=RED)},
 "g2gs": {"male": dict(skin="fair", hair="#2E2E3A", top="#E4B93C", vest="#C8892B", head="capback", hc1="#E4B93C", brim=RED)},
 "g2c":  {"male": dict(skin="fair", hair="#2E2E3A", top="#3E7CC9", collar=WHITE, head="capback", hc1="#3E7CC9", brim=RED),
          "female": dict(skin="fair", hair="#3A3F6B", top="#3E7CC9", collar=WHITE, style="twin", blush=1, head="headband", hc1="#E0577A")},
 "g3rse":{"male": dict(skin="fair", hair="#7B4A2E", top="#2F9E6E", stripe=WHITE, head="beanie", hc1=WHITE, hc2="#2F9E6E"),
          "female": dict(skin="fair", hair="#5A3A28", top="#D6453D", collar=WHITE, style="bob", blush=1, head="bandana", hc1=RED, hc2=RED)},
 "g3frlg":{"male": dict(skin="fair", hair="#3A2A24", top="#E8E2D0", vest=RED, straps="#8A8A86", head="cap", hc1=RED, hc2=WHITE, brim=RED),
          "female": dict(skin="fair", hair="#6B4328", top="#E8E2D0", vest="#2FA39A", straps="#8A8A86", style="long", blush=1, head="hat", hc1="#F4F0D8", hc2="#F4F0D8", hc3="#D6453D")},
 "g4dppt":{"male": dict(skin="fair", hair="#2E2E3A", top="#E8E8E8", scarf="#D6453D", head="beanie", hc1="#2E2E3A", hc2="#D6453D"),
          "female": dict(skin="fair", hair="#2F3D78", top="#E4568A", scarf="#F4F4F0", style="long", blush=1, head="beanie", hc1=WHITE, hc2="#E4568A")},
 "g4hgss":{"male": dict(skin="fair", hair="#3A2A24", top="#C23A3A", vest="#F0D24A", straps="#555", head="capback", hc1="#C23A3A", brim="#F4F4F0"),
          "female": dict(skin="fair", hair="#7A4A2A", top="#F4F0D8", vest="#E4568A", style="twin", blush=1, head="cap", hc1="#F4F0D8", hc2="#E4568A", brim="#E4568A")},
 "g5bw": {"male": dict(skin="fair", hair="#6A4430", top="#2E2E3A", stripe="#F4F4F0", head="cap", hc1="#2E2E3A", hc2=WHITE, brim="#F4F4F0"),
          "female": dict(skin="fair", hair="#6A4430", top="#F4F4F0", vest="#2E2E3A", style="ponytail", blush=1, head="cap", hc1="#2E2E3A", hc2="#E4568A", brim="#F4F4F0")},
 "g5b2w2":{"male": dict(skin="fair", hair="#5A3A28", top="#3A7CC9", collar=WHITE, head="visor", hc1="#F0D24A", hc2="#3A7CC9"),
          "female": dict(skin="fair", hair="#6A3A24", top="#E8A33C", collar=WHITE, style="buns", blush=1, head="visor", hc1="#E4568A", hc2="#F0D24A")},
 "g6xy": {"male": dict(skin="fair", hair="#7A5A3A", top="#2F5FA8", collar="#F4F4F0", stripe="#8FB8F0"),
          "female": dict(skin="fair", hair="#7A4A2A", top="#C4547A", collar="#F4F4F0", style="ponytail", blush=1, head="beret", hc1="#E4A0B8", hc2="#C4547A")},
 "g7sm": {"male": dict(skin="tan", hair="#2E2E3A", top="#F0A23C", collar="#F4F4F0", stripe="#2F9EC9"),
          "female": dict(skin="tan", hair="#E4C45A", top="#F4F4F0", collar="#E4568A", style="long", blush=1, head="headband", hc1="#E4568A")},
 "g7lgpe":{"male": dict(skin="fair", hair="#4A3022", top="#3A7CC9", vest="#F4F4F0", head="cap", hc1="#3A7CC9", hc2=WHITE, brim="#2F5FA8"),
          "female": dict(skin="fair", hair="#5A3A28", top="#E4568A", vest="#F4F4F0", style="ponytail", blush=1, head="cap", hc1="#E4568A", hc2=WHITE, brim="#C4547A")},
 "g8swsh":{"male": dict(skin="tan", hair="#3A2A24", top="#3E7CC9", vest="#E8E8E8", head="beanie", hc1="#E8E8E8", hc2="#3E7CC9"),
          "female": dict(skin="tan", hair="#3A2A24", top="#C95A8A", vest="#E8E8E8", style="ponytail", blush=1, head="beret", hc1="#E8E8E8", hc2="#C95A8A")},
 "g8la": {"male": dict(skin="fair", hair="#2E2E3A", top="#E8DCC0", scarf="#C23A3A", head="headband", hc1="#C23A3A"),
          "female": dict(skin="fair", hair="#2E2E3A", top="#E8DCC0", scarf="#D6453D", style="ponytail", blush=1, head="headband", hc1="#D6453D")},
 "g9sv": {"male": dict(skin="tan", hair="#2E2E3A", top="#3A2E5C", collar="#F0A23C", stripe="#8F6FE0"),
          "female": dict(skin="tan", hair="#2E2E3A", top="#3A2E5C", collar="#F0A23C", style="bob", blush=1, stripe="#8F6FE0")},
 # Genéricos (respaldo para versiones sin ficha propia)
 "generic": {"male": dict(skin="fair", hair="#3A3A46", top="#4A78B8", collar=WHITE),
             "female": dict(skin="fair", hair="#5A3A32", top="#C9567A", collar=WHITE, style="long", blush=1)},
}

def main():
    os.makedirs(OUT, exist_ok=True)
    n = 0
    sheet_cells = []
    for key, gs in D.items():
        for g, params in gs.items():
            im = draw_figure(params)
            im.save(os.path.join(OUT, f"{key}.{g}.png"), optimize=True)
            sheet_cells.append((key, g, im)); n += 1
    print(f"{n} íconos → {os.path.normpath(OUT)}")
    return sheet_cells

if __name__ == "__main__":
    cells = main()
    if os.environ.get("TRAINER_SHEET"):
        cols = 8; rows = (len(cells) + cols - 1) // cols
        sheet = Image.new("RGBA", (cols * 200, rows * 214), (240, 240, 242, 255))
        sd = ImageDraw.Draw(sheet)
        for i, (k, g, im) in enumerate(cells):
            x, y = (i % cols) * 200 + 4, (i // cols) * 214 + 4
            sheet.alpha_composite(im, (x, y)); sd.text((x + 4, y + 194), f"{k}.{g}", fill=(40, 40, 50, 255))
        sheet.convert("RGB").save(os.environ["TRAINER_SHEET"])

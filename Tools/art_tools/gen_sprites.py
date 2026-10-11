# -*- coding: utf-8 -*-
"""Генератор пиксельных спрайтов игры (нужен только Python 3).

    python Tools/art_tools/gen_sprites.py

Создаёт (существующие .meta с GUID не трогаются):
  Assets/Sprites/Blocks/Wood.png   — дерево 32x32, рамка 2 px (9-slice, режим Tiled);
  Assets/Sprites/Blocks/Stone.png  — камень 32x32, рамка 2 px (9-slice, режим Tiled);
  Assets/Sprites/Mouse/Mouse.png   — серая мышь 20x15 (временная текстура цели);
  Assets/UI/Generated/StarOn.png, StarOff.png — звёзды 96x96 лучом вверх.

Pixels Per Unit = 10: один пиксель текстуры = 0.1 единицы мира, поэтому
доска 0.6 толщиной состоит ровно из 6 пикселей.
"""
import math
import os

from pixel_art import Canvas

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))

# ---------------------------------------------------------------- палитры
WOOD = dict(
    outline=(84, 46, 22),
    dark=(150, 88, 44),
    base=(198, 130, 68),
    light=(222, 160, 92),
    high=(244, 198, 130),
    grain=(170, 104, 52),
    knot=(122, 70, 34),
)
STONE = dict(
    outline=(48, 50, 60),
    dark=(96, 100, 114),
    base=(140, 145, 158),
    light=(170, 175, 188),
    high=(204, 208, 218),
    mortar=(86, 89, 102),
)
MOUSE = dict(
    outline=(46, 46, 58),
    dark=(108, 110, 124),
    base=(150, 152, 166),
    light=(190, 192, 204),
    white=(242, 242, 248),
    black=(22, 22, 30),
    pink=(238, 150, 172),
    pink_dark=(196, 98, 124),
    blush=(222, 140, 160),
    tail=(206, 128, 148),
    whisker=(70, 70, 84),
)


def bevel_frame(c, pal):
    """Рамка 2 px: внешний контур и фаска (свет сверху-слева, тень снизу-справа)."""
    w, h = c.w, c.h
    c.frame(0, 0, w - 1, h - 1, pal["outline"])
    c.hline(1, w - 2, 1, pal["high"])
    c.vline(1, 1, h - 2, pal["high"])
    c.hline(1, w - 2, h - 2, pal["dark"])
    c.vline(w - 2, 2, h - 2, pal["dark"])
    c.set(w - 2, 1, pal["light"])
    c.set(1, h - 2, pal["light"])


def wood_tile():
    """Доски с продольными волокнами; центр 28x28 бесшовно повторяется."""
    c = Canvas(32, 32, WOOD["base"])
    rows = []
    for y in range(28):
        # Волокна: полосы разной яркости, редкие тёмные линии.
        phase = y % 7
        tone = WOOD["base"]
        if phase == 0:
            tone = WOOD["grain"]
        elif phase in (2, 3):
            tone = WOOD["light"]
        rows.append(tone)
    for y in range(28):
        for x in range(28):
            col = rows[y]
            # Короткие штрихи волокон, повторяются по горизонтали без шва.
            if (x * 3 + y * 11) % 17 == 0 and rows[y] != WOOD["grain"]:
                col = WOOD["grain"]
            c.set(2 + x, 2 + y, col)
    # Сучки (не касаются краёв центра, чтобы плитка оставалась бесшовной).
    for kx, ky in ((8, 9), (20, 22)):
        c.ellipse(2 + kx, 2 + ky, 2.2, 1.4, WOOD["knot"])
        c.set(2 + kx, 2 + ky, WOOD["dark"])
        c.set(2 + kx - 2, 2 + ky - 1, WOOD["light"])
    bevel_frame(c, WOOD)
    return c


def stone_tile():
    """Каменная кладка: кирпичи 14x7 со сдвигом рядов; центр 28x28 бесшовный."""
    c = Canvas(32, 32, STONE["base"])
    for y in range(28):
        row = y // 7
        offset = 7 if row % 2 else 0
        yy = y % 7
        for x in range(28):
            xx = (x + offset) % 14
            if yy == 6 or xx == 13:
                col = STONE["mortar"]
            elif yy == 0 or xx == 0:
                col = STONE["light"]
            elif yy == 5 or xx == 12:
                col = STONE["dark"]
            else:
                col = STONE["base"]
                # Лёгкая фактура камня.
                if (x * 7 + y * 5) % 23 == 0:
                    col = STONE["light"]
                elif (x * 5 + y * 3) % 29 == 0:
                    col = STONE["dark"]
            c.set(2 + x, 2 + y, col)
    bevel_frame(c, STONE)
    return c


MOUSE_ART = [
    # 24 x 16, мышь смотрит влево (на катапульту); хвост справа, усы слева.
    "......OOOO..............",
    ".....OGPPGO.............",
    "....OGPRRPGO............",
    "....OGPRRPGO............",
    "....OGGPPGGOOOOOO.......",
    "...OOOGGGGGLLLLLLOO.....",
    "..OGGGGGGGLLLLLLLLLO....",
    ".OGGKKGGGGGGLLLLLLLGO...",
    "wOGGKWGGGGGGGGGGGGGGO...",
    ".OGGGGGGGGGGGGGGGGGDDO..",
    "PGGBGGGGGGGGGGGGGGGDDO..",
    "wOGGGGGGLLLLGGGGGGDDDOT.",
    ".OOGGGGLLLLLLGGGGDDDDOTT",
    "..OODDGGLLLLGGGGDDDDO..T",
    "....OOODDDDDDDDDDOOO..T.",
    ".....OPPO...OPPO....TT..",
]

MOUSE_KEYS = {
    "O": "outline", "D": "dark", "G": "base", "L": "light",
    "W": "white", "K": "black", "P": "pink", "R": "pink_dark",
    "B": "blush", "T": "tail", "w": "whisker",
}


def sprite_from_art(art, pal, keys):
    c = Canvas(len(art[0]), len(art))
    for y, row in enumerate(art):
        assert len(row) == c.w, (y, len(row))
        for x, ch in enumerate(row):
            if ch != ".":
                c.set(x, y, pal[keys[ch]])
    return c


def mouse_sprite():
    return sprite_from_art(MOUSE_ART, MOUSE, MOUSE_KEYS)


# ---------------------------------------------------------------- звёзды
# Повторяет удалённый StarSpriteFactory, но первый луч направлен вверх.
GOLD = (230, 155, 82)
GOLD_EDGE = (186, 110, 42)
EMPTY_FILL = (70, 70, 70)
EMPTY_EDGE = (150, 150, 150)


def _star_points(cx, cy, outer, inner, start=math.pi / 2):
    pts = []
    for i in range(10):
        r = outer if i % 2 == 0 else inner
        # start = +pi/2 в координатах текстуры Unity (Y вверх) — луч смотрит вверх.
        # Старая фабрика использовала -pi/2, поэтому звёзды были перевёрнуты.
        a = start + i * math.pi / 5
        pts.append((cx + math.cos(a) * r, cy + math.sin(a) * r))
    return pts


def _contains(poly, x, y):
    inside = False
    j = len(poly) - 1
    for i in range(len(poly)):
        xi, yi = poly[i]
        xj, yj = poly[j]
        if (yi > y) != (yj > y) and x < (xj - xi) * (y - yi) / (yj - yi) + xi:
            inside = not inside
        j = i
    return inside


def _edge_distance(poly, x, y):
    best = float("inf")
    for i in range(len(poly)):
        ax, ay = poly[i]
        bx, by = poly[(i + 1) % len(poly)]
        dx, dy = bx - ax, by - ay
        t = max(0.0, min(1.0, ((x - ax) * dx + (y - ay) * dy) / (dx * dx + dy * dy)))
        px, py = ax + dx * t, ay + dy * t
        best = min(best, math.hypot(x - px, y - py))
    return best


def star_sprite(filled, size=96, start=math.pi / 2):
    c = Canvas(size, size)
    center = (size - 1) / 2.0
    outer = size * 0.46
    inner = outer * 0.42
    star = _star_points(center, center, outer, inner, start)
    hole = _star_points(center, center, outer * 0.72, inner * 0.72, start)
    fill = GOLD if filled else EMPTY_FILL
    edge = GOLD_EDGE if filled else EMPTY_EDGE
    for ty in range(size):          # ty — строка текстуры Unity (0 — низ)
        for x in range(size):
            if not _contains(star, x, ty):
                continue
            if not filled and _contains(hole, x, ty):
                continue
            near = _edge_distance(star, x, ty) < 1.6
            c.set(x, size - 1 - ty, edge if near else fill)   # строка PNG сверху вниз
    return c


def unity_rect(canvas):
    """Прямоугольник непрозрачной области в координатах Unity (начало — снизу слева)."""
    x0, y0, x1, y1 = canvas.crop_bounds()
    return x0, canvas.h - 1 - y1, x1 - x0 + 1, y1 - y0 + 1


def save(canvas, rel):
    path = os.path.join(ROOT, rel)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    canvas.save_png(path)
    print("записан", rel, "%dx%d" % (canvas.w, canvas.h))


def main():
    save(wood_tile(), "Assets/Sprites/Blocks/Wood.png")
    save(stone_tile(), "Assets/Sprites/Blocks/Stone.png")
    save(mouse_sprite(), "Assets/Sprites/Mouse/Mouse.png")
    save(star_sprite(True), "Assets/UI/Generated/StarOn.png")
    save(star_sprite(False), "Assets/UI/Generated/StarOff.png")


if __name__ == "__main__":
    main()

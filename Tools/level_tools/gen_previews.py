# -*- coding: utf-8 -*-
"""Пиксельные превью карточек уровней в стиле Angry Birds (нужен только Python 3).

    python Tools/level_tools/gen_previews.py          # все уровни
    python Tools/level_tools/gen_previews.py 2 5      # выбранные уровни

Каждая картинка рисуется по раскладке из level_layouts.py: катапульта с котом слева,
постройка из дерева и камня справа, серые пиксельные мыши вместо свиней.
Холст 131x120 пикселей (пропорции окна превью карточки 380x348) увеличивается
в 4 раза «ближайшим соседом» и сохраняется в Assets/Resources/LevelPreviews/level_N.png.
Файлы .meta (GUID и настройки импорта) не трогаются.
"""
import math
import os
import random
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "art_tools"))

import level_layouts as LL  # noqa: E402
from pixel_art import Canvas, mix, shade  # noqa: E402

ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT_DIR = os.path.join(ROOT, "Assets", "Resources", "LevelPreviews")

W, H = 131, 120
UPSCALE = 4
GROUND_Y = 100          # строка верха земли на холсте
STRUCT_LEFT = 36        # область постройки по горизонтали
STRUCT_RIGHT = 129
MAX_SCALE = 7.5         # пикселей холста на единицу мира

OUTLINE = (40, 30, 30)
WOOD = dict(out=(84, 46, 22), base=(198, 130, 68), light=(232, 172, 102), dark=(156, 94, 46))
STONE = dict(out=(48, 50, 60), base=(140, 145, 158), light=(186, 190, 202), dark=(100, 104, 118))
SLAB = dict(out=(44, 46, 54), base=(112, 115, 126), light=(146, 150, 162), dark=(84, 87, 98))

# Мышь 13x9, смотрит влево. O контур, G серый, L светлый, D тёмный, K глаз,
# P розовый (ухо, нос, лапки), R тёмно-розовый, B румянец, T хвост.
MOUSE_BIG = [
    "...OO........",
    "..OPRO.......",
    "..OPPOOOOO...",
    ".OGGGGLLLLO..",
    "OGKGGGGLLGGO.",
    "PGGGGGGGGGDOT",
    "OGBGGLLGGDDOT",
    ".OODDDDDDDOT.",
    "..OP...OP....",
]
# Мышь 10x7 для мелкого масштаба.
MOUSE_SMALL = [
    "..OO......",
    ".OPRO.....",
    ".OGGGOOO..",
    "OKGGGLLGO.",
    "PGGGGGGDOT",
    "OGBGGGDDOT",
    ".OOPOOPOT.",
]
MOUSE_COLORS = {
    "O": (46, 46, 58), "G": (150, 152, 166), "L": (192, 194, 206), "D": (108, 110, 124),
    "K": (20, 20, 28), "P": (238, 150, 172), "R": (196, 98, 124), "B": (222, 140, 160),
    "T": (206, 128, 148),
}

# Рыжий кот 11x10 в ковше катапульты, смотрит вправо.
CAT = [
    ".O.....O...",
    "OAO...OAO..",
    "OAAOOOAAO..",
    "OAAAAAAAAO.",
    "OASAAAAKAAO",
    "OAAAAAWWNWO",
    "OASAAAWWWWO",
    ".OAAAAAAAO.",
    "..OSASASO..",
    "...OOOOO...",
]
CAT_COLORS = {
    "O": (60, 30, 16), "A": (240, 146, 56), "S": (198, 100, 34), "K": (20, 20, 28),
    "W": (250, 240, 228), "N": (232, 110, 120),
}


def sprite(art, colors):
    c = Canvas(len(art[0]), len(art))
    for y, row in enumerate(art):
        for x, ch in enumerate(row):
            if ch != ".":
                c.set(x, y, colors[ch])
    return c


def sky(c, pal, rnd):
    mood = pal["mood"]
    top, bottom = {
        "day": ((96, 160, 222), (178, 218, 242)),
        "dusk": ((92, 72, 140), (236, 150, 140)),
        "night": ((14, 18, 46), (46, 60, 110)),
        "sunset": ((178, 74, 70), (252, 196, 104)),
    }[mood]
    bands = 9
    for y in range(GROUND_Y):
        t = min(1.0, y / float(GROUND_Y - 8))
        band = int(t * bands) / float(bands)      # ступенчатый градиент
        col = mix(top, bottom, band)
        c.hline(0, W - 1, y, col)
        # Дизеринг на стыке полос.
        frac = t * bands - int(t * bands)
        if frac > 0.75:
            nxt = mix(top, bottom, min(1.0, band + 1.0 / bands))
            for x in range((y % 2), W, 2):
                c.set(x, y, nxt)
    if mood == "night":
        for _ in range(42):
            x, y = rnd.randrange(W), rnd.randrange(GROUND_Y - 30)
            c.set(x, y, (255, 250, 220) if rnd.random() < 0.7 else (170, 190, 255))
        for x, y in ((30, 14), (96, 22), (64, 9)):
            c.set(x, y, (255, 255, 255))
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                c.set(x + dx, y + dy, (200, 210, 255))
        # Луна.
        c.ellipse(108, 18, 8, 8, (246, 240, 200))
        c.ellipse(112, 15, 7, 7, top)
        c.ellipse(105, 20, 1.4, 1.4, (220, 214, 170))
    if mood == "sunset":
        c.ellipse(96, GROUND_Y - 14, 14, 14, (255, 222, 120))
        c.ellipse(96, GROUND_Y - 14, 11, 11, (255, 238, 160))
    if mood == "dusk":
        c.ellipse(24, 22, 5, 5, (255, 236, 200))


def cloud(c, x, y, scale, col):
    light = col
    dark = shade(col, 0.86)
    parts = [(0, 3, 5, 3), (5, 1, 6, 5), (11, 2, 5, 4), (15, 4, 4, 2)]
    for px, py, rx, ry in parts:
        c.ellipse(x + px * scale, y + py * scale, rx * scale, ry * scale, dark)
    for px, py, rx, ry in parts:
        c.ellipse(x + px * scale, y + py * scale - 1, rx * scale, ry * scale - 0.6, light)


def hills(c, pal, rnd):
    mood = pal["mood"]
    base = {
        "day": (120, 176, 120), "dusk": (150, 104, 132), "night": (34, 46, 80),
        "sunset": (170, 90, 72),
    }[mood]
    far = mix(base, c.get(0, GROUND_Y - 30), 0.45)
    for (cx, r, col) in ((10, 26, far), (58, 34, far), (118, 28, far)):
        c.ellipse(cx, GROUND_Y + 6, r, r * 0.62, col)
    for (cx, r, col) in ((34, 20, base), (90, 24, base)):
        c.ellipse(cx, GROUND_Y + 6, r, r * 0.5, col)


def ground(c, pal, rnd):
    g = pal["ground"]
    mood = pal["mood"]
    grass = {"day": (96, 176, 64), "dusk": (124, 150, 72), "night": (52, 94, 76),
             "sunset": (128, 140, 52)}[mood]
    grass_dark = shade(grass, 0.72)
    dirt = g
    dirt_dark = shade(g, 0.78)
    dirt_light = shade(g, 1.12)
    c.rect(0, GROUND_Y, W - 1, H - 1, dirt)
    for y in range(GROUND_Y + 4, H):
        for x in range(W):
            v = (x * 13 + y * 7 + (x // 5) * 3) % 19
            if v == 0:
                c.set(x, y, dirt_dark)
            elif v == 7 and y % 3 == 0:
                c.set(x, y, dirt_light)
    # Камешки.
    for _ in range(18):
        x, y = rnd.randrange(W), rnd.randrange(GROUND_Y + 6, H - 1)
        c.set(x, y, dirt_light)
        c.set(x + 1, y, dirt_light)
        c.set(x, y + 1, dirt_dark)
    # Трава с травинками.
    c.rect(0, GROUND_Y, W - 1, GROUND_Y + 2, grass)
    c.hline(0, W - 1, GROUND_Y + 3, grass_dark)
    for x in range(0, W, 2):
        if (x * 7) % 5 < 3:
            c.set(x, GROUND_Y - 1, grass)
        if (x * 11) % 7 == 0:
            c.set(x, GROUND_Y - 2, grass)
            c.set(x, GROUND_Y - 1, grass)


def catapult(c, x, y):
    """Катапульта с котом; (x, y) — точка на земле под центром рамы."""
    wood = (150, 92, 46)
    wood_dark = (98, 56, 26)
    wood_light = (196, 132, 74)
    rope = (220, 200, 150)
    # Рама.
    c.rect(x - 10, y - 4, x + 10, y - 3, wood)
    c.hline(x - 10, x + 10, y - 4, wood_light)
    c.line(x - 6, y - 4, x - 1, y - 16, wood_dark)
    c.line(x - 5, y - 4, x, y - 16, wood)
    c.line(x + 4, y - 4, x - 1, y - 16, wood_dark)
    c.line(x + 5, y - 4, x, y - 16, wood)
    # Ось и рычаг, отведённый назад.
    c.rect(x - 2, y - 17, x + 1, y - 15, wood_dark)
    c.line(x, y - 16, x - 12, y - 22, wood_dark)
    c.line(x, y - 15, x - 12, y - 21, wood)
    c.line(x - 1, y - 16, x - 7, y - 4, rope)
    # Колёса.
    for wx in (x - 7, x + 7):
        c.ellipse(wx, y - 2, 3.2, 3.2, wood_dark)
        c.ellipse(wx, y - 2, 1.8, 1.8, wood_light)
        c.set(int(wx), y - 2, wood_dark)
    # Ковш с котом.
    cat = sprite(CAT, CAT_COLORS)
    cx, cy = x - 18, y - 30
    c.blit(cat, cx, cy)
    c.rect(x - 18, y - 21, x - 8, y - 19, wood_dark)
    c.hline(x - 17, x - 9, y - 21, wood_light)
    return cx + 6, cy + 4   # откуда начинается траектория


def trajectory(c, start, end, peak):
    (x0, y0), (x1, y1) = start, end
    pts = []
    steps = 60
    for i in range(steps + 1):
        t = i / float(steps)
        x = x0 + (x1 - x0) * t
        # Парабола через вершину peak над серединой.
        y = (1 - t) * (1 - t) * y0 + 2 * (1 - t) * t * peak + t * t * y1
        pts.append((x, y))
    dist = 0.0
    last = pts[0]
    next_dot = 3.0
    for p in pts[1:]:
        dist += math.hypot(p[0] - last[0], p[1] - last[1])
        last = p
        if dist >= next_dot:
            next_dot += 6.0
            fade = max(90, int(255 - dist * 1.2))
            c.set(int(p[0]), int(p[1]), (255, 255, 255, fade))
            c.set(int(p[0]) + 1, int(p[1]), (255, 255, 255, fade // 2))


def block(c, x0, y0, x1, y1, pal, vertical, stone):
    if x1 < x0 or y1 < y0:
        return
    c.rect(x0, y0, x1, y1, pal["base"])
    w = x1 - x0 + 1
    h = y1 - y0 + 1
    # Фактура: волокна дерева вдоль длины, швы кладки у камня.
    if stone:
        for yy in range(y0 + 1, y1):
            for xx in range(x0 + 1, x1):
                row = (yy - y0) // 3
                if (yy - y0) % 3 == 0 or (xx - x0 + (row % 2) * 2) % 5 == 0:
                    c.set(xx, yy, pal["dark"])
    else:
        if vertical:
            for xx in range(x0 + 1, x1):
                if (xx - x0) % 2 == 0 and w > 3:
                    c.vline(xx, y0 + 1, y1 - 1, pal["dark"])
        else:
            for yy in range(y0 + 1, y1):
                if (yy - y0) % 2 == 0 and h > 3:
                    for xx in range(x0 + 1, x1):
                        if (xx * 3 + yy) % 7:
                            c.set(xx, yy, pal["dark"])
    if h >= 3 and w >= 3:
        c.hline(x0 + 1, x1 - 1, y0 + 1, pal["light"])
        c.vline(x0 + 1, y0 + 1, y1 - 1, pal["light"])
    c.frame(x0, y0, x1, y1, pal["out"])


def render(layout):
    rnd = random.Random(layout.level_id * 31 + 7)
    pal = layout.palette
    c = Canvas(W, H, (0, 0, 0))
    sky(c, pal, rnd)
    if pal["mood"] != "night":
        ccol = {"day": (250, 252, 255), "dusk": (250, 210, 210), "sunset": (255, 226, 190)}[pal["mood"]]
        cloud(c, 8, 12, 1.0, ccol)
        cloud(c, 70, 26, 0.8, ccol)
        if layout.level_id % 2:
            cloud(c, 104, 8, 0.7, ccol)
    hills(c, pal, rnd)
    ground(c, pal, rnd)

    # Масштаб постройки: вписать в правую часть картинки.
    xs = [i.left for i in layout.items] + [s[1] - s[3] / 2 for s in layout.slabs]
    xe = [i.right for i in layout.items] + [s[1] + s[3] / 2 for s in layout.slabs]
    top = max(i.top for i in layout.items) + 0.6
    span_x = max(xe) - min(xs)
    span_y = top - LL.GROUND_TOP
    scale = min(MAX_SCALE, (STRUCT_RIGHT - STRUCT_LEFT) / span_x, (GROUND_Y - 22) / span_y)
    mid = (max(xe) + min(xs)) / 2
    cx = (STRUCT_LEFT + STRUCT_RIGHT) / 2.0

    def px(wx):
        return cx + (wx - mid) * scale

    def py(wy):
        return GROUND_Y - (wy - LL.GROUND_TOP) * scale

    for name, sx, sy, sw, sh in layout.slabs:
        x0, x1 = int(round(px(sx - sw / 2))), int(round(px(sx + sw / 2))) - 1
        y0, y1 = int(round(py(sy + sh / 2))), GROUND_Y + 1
        block(c, x0, y0, x1, y1, SLAB, False, True)
    for it in layout.blocks:
        # Не тоньше 3 пикселей: контур + хотя бы один пиксель цвета материала.
        bw = max(3, int(round(it.ext_w * scale)))
        bh = max(3, int(round(it.ext_h * scale)))
        x0 = int(round(px(it.x) - bw / 2.0))
        y1 = int(round(py(it.bottom))) - 1
        stone = it.kind == "stone"
        block(c, x0, y1 - bh + 1, x0 + bw - 1, y1, STONE if stone else WOOD,
              it.rot == 90 or it.ext_h > it.ext_w * 1.5, stone)

    mouse_art = MOUSE_BIG if scale >= 4.4 else MOUSE_SMALL
    m = sprite(mouse_art, MOUSE_COLORS)
    for it in layout.mice:
        bx = int(round(px(it.x))) - m.w // 2
        by = int(round(py(it.bottom))) - m.h
        c.blit(m, bx, by)

    start = catapult(c, 20, GROUND_Y)
    target_mouse = max(layout.mice, key=lambda i: i.top)
    end = (px(target_mouse.x) - 6, py(target_mouse.y))
    trajectory(c, start, end, min(start[1], end[1]) - 34)
    return c


def main(argv):
    only = {int(a) for a in argv if a.isdigit()}
    for layout in LL.all_layouts():
        if only and layout.level_id not in only:
            continue
        art = render(layout)
        path = os.path.join(OUT_DIR, "level_%d.png" % layout.level_id)
        art.scaled(UPSCALE).save_png(path, alpha=False)
        print("записан", os.path.relpath(path, ROOT), "%dx%d" % (W * UPSCALE, H * UPSCALE))


if __name__ == "__main__":
    main(sys.argv[1:])

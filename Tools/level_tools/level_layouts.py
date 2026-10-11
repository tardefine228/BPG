# -*- coding: utf-8 -*-
"""Раскладки построек для пяти уровней — общий источник данных для инструментов.

Используется:
  * apply_layouts.py  — записывает постройки в сцены уровней (префабы блоков и мышей);
  * gen_previews.py   — рисует пиксельные превью карточек уровней;
  * check_layouts.py  — (необязательно) проверяет устойчивость построек в Box2D.

Координаты локальные относительно объекта TargetArea (его Y = 0), поэтому Y совпадает
с мировым. Верх земли (FlatGround) находится на GROUND_TOP. Размеры блоков кратны
0.1 — это один пиксель текстуры (Pixels Per Unit = 10), так пиксели блоков совпадают.

Типы элементов:
  wood   — деревянный блок: одно попадание, лёгкий (плотность WOOD_DENSITY);
  stone  — каменный блок: выдерживает несколько ударов, тяжёлый;
  mouse  — мышь-цель (аналог свиньи в Angry Birds), всегда стоит НЕ на земле;
  slab   — неподвижная каменная площадка (статичная, не считается землёй).
"""

GROUND_TOP = -2.61

# Зазоры повторяют «кожу» коллайдеров Unity (Default Contact Offset = 0.01):
# блоки ставятся так, чтобы с первого кадра лежать без толчка.
BOX_GAP = 0.015      # блок на блоке / на земле
MOUSE_GAP = 0.005    # капсула мыши на блоке

WOOD_DENSITY = 0.25  # ящик 2.86 x 2.86 из старых уровней весил 2
STONE_DENSITY = 0.6  # бронированный блок старых уровней весил 5–6

# Размер мыши: спрайт 24x16 пикселей (2.4 x 1.6) и капсула по телу без уха и хвоста.
MOUSE_W = 2.4
MOUSE_H = 1.6
MOUSE_COLLIDER_W = 1.8
MOUSE_COLLIDER_H = 1.1
# Смещение капсулы относительно центра спрайта (тело ниже центра из-за уха).
MOUSE_COLLIDER_OFFSET = (-0.05, -0.25)


class Item:
    __slots__ = ("kind", "x", "y", "w", "h", "rot", "name", "lives")

    def __init__(self, kind, x, y, w, h, rot=0, name="", lives=0):
        self.kind = kind
        self.x = round(x, 4)
        self.y = round(y, 4)
        self.w = round(w, 4)
        self.h = round(h, 4)
        self.rot = rot          # 0 или 90 градусов (90 — доска стоит вертикально)
        self.name = name
        self.lives = lives

    # Габариты с учётом поворота.
    @property
    def ext_w(self):
        return self.h if self.rot == 90 else self.w

    @property
    def ext_h(self):
        return self.w if self.rot == 90 else self.h

    @property
    def top(self):
        if self.kind == "mouse":
            return self.y + MOUSE_COLLIDER_H / 2
        return self.y + self.ext_h / 2

    @property
    def bottom(self):
        if self.kind == "mouse":
            return self.y - MOUSE_COLLIDER_H / 2
        return self.y - self.ext_h / 2

    @property
    def left(self):
        return self.x - self.ext_w / 2

    @property
    def right(self):
        return self.x + self.ext_w / 2


class Layout:
    """Постройка одного уровня."""

    def __init__(self, level_id, scene, target_x, sling_x, impact_size, palette):
        self.level_id = level_id
        self.scene = scene
        self.target_x = target_x      # мировой X объекта TargetArea
        self.sling_x = sling_x        # мировой X рогатки (для превью и проверок)
        self.impact_size = impact_size  # размер камеры «Цель»
        self.palette = palette
        self.items = []
        self.slabs = []               # (имя, центр X, центр Y, ширина, высота) в локальных координатах
        self.counters = {}

    def _name(self, prefix):
        n = self.counters.get(prefix, 0) + 1
        self.counters[prefix] = n
        return "%s_%02d" % (prefix, n)

    # base — Y нижней грани (обычно top предыдущего блока или GROUND_TOP).
    def block(self, kind, x, base, w, h, vertical=False, prefix=None):
        if vertical:
            item = Item(kind, x, base + BOX_GAP + w / 2, w, h, rot=90)
        else:
            item = Item(kind, x, base + BOX_GAP + h / 2, w, h)
        item.name = self._name(prefix or ("Wood" if kind == "wood" else "Stone"))
        self.items.append(item)
        return item

    def wood(self, x, base, w, h, vertical=False):
        return self.block("wood", x, base, w, h, vertical)

    def stone(self, x, base, w, h, vertical=False):
        return self.block("stone", x, base, w, h, vertical)

    # Вертикальная доска: длина length, толщина thick.
    def pillar(self, kind, x, base, length, thick=0.6):
        return self.block(kind, x, base, length, thick, vertical=True)

    def mouse(self, x, base, lives=2):
        item = Item("mouse", x, base + MOUSE_GAP + MOUSE_COLLIDER_H / 2, MOUSE_W, MOUSE_H,
                    name=self._name("Mouse"), lives=lives)
        self.items.append(item)
        return item

    def slab(self, name, cx, cy, w, h):
        self.slabs.append((name, cx, cy, w, h))
        return cy + h / 2

    @property
    def blocks(self):
        return [i for i in self.items if i.kind != "mouse"]

    @property
    def mice(self):
        return [i for i in self.items if i.kind == "mouse"]


# Палитры повторяют цвета фона и земли сцен (BackgroundPlaceholder / FlatGround).
PALETTES = {
    1: dict(sky=(133, 184, 224), ground=(232, 157, 87), mood="day"),
    2: dict(sky=(128, 107, 158), ground=(194, 179, 158), mood="dusk"),
    3: dict(sky=(36, 46, 92), ground=(140, 158, 179), mood="night"),
    4: dict(sky=(204, 115, 89), ground=(143, 99, 79), mood="sunset"),
    5: dict(sky=(135, 186, 219), ground=(92, 133, 87), mood="day"),
}

G = GROUND_TOP
T = 0.6   # толщина досок


def level_1():
    """Уровень 1 — простая «скамейка»: две стойки, доска и мышь сверху."""
    L = Layout(1, "Gameplay", target_x=50.0, sling_x=-64.965, impact_size=10.0, palette=PALETTES[1])
    L.pillar("wood", -1.8, G, 2.4)
    p = L.pillar("wood", 1.8, G, 2.4)
    top = L.wood(0.0, p.top, 4.8, T)
    L.mouse(0.0, top.top)
    return L


def level_2():
    """Уровень 2 — двухэтажная башня, по мыши на каждом этаже, каменный упор слева."""
    L = Layout(2, "Level2", target_x=44.0, sling_x=-64.965, impact_size=11.0, palette=PALETTES[2])
    L.stone(-4.2, G, 1.2, 1.2)
    L.pillar("wood", -2.1, G, 3.0)
    p = L.pillar("wood", 2.1, G, 3.0)
    floor1 = L.wood(0.0, p.top, 5.4, T)
    L.mouse(0.0, floor1.top)
    L.pillar("wood", -2.1, floor1.top, 2.4)
    p2 = L.pillar("wood", 2.1, floor1.top, 2.4)
    floor2 = L.wood(0.0, p2.top, 5.4, T)
    L.mouse(0.0, floor2.top)
    L.stone(4.2, G, 1.2, 1.2)
    return L


def level_3():
    """Уровень 3 — две башни на каменных основаниях, мост и хижина посередине."""
    L = Layout(3, "Level3", target_x=44.0, sling_x=-69.0, impact_size=11.0, palette=PALETTES[3])
    # Левая башня.
    lb = L.stone(-4.2, G, 3.0, 1.2)
    L.pillar("wood", -5.4, lb.top, 3.0)
    lp = L.pillar("wood", -3.0, lb.top, 3.0)
    # Правая башня.
    rb = L.stone(4.2, G, 3.0, 1.2)
    L.pillar("wood", 3.0, rb.top, 3.0)
    L.pillar("wood", 5.4, rb.top, 3.0)
    # Мост через обе башни.
    bridge = L.wood(0.0, lp.top, 12.0, T)
    # Хижина посередине моста.
    L.pillar("wood", -1.5, bridge.top, 2.4)
    hut = L.pillar("wood", 1.5, bridge.top, 2.4)
    L.wood(0.0, hut.top, 3.6, T)
    L.mouse(0.0, bridge.top)
    # Края моста.
    L.mouse(-3.4, bridge.top)
    L.mouse(3.4, bridge.top)
    L.wood(-5.0, bridge.top, 1.2, 1.2)
    L.wood(5.0, bridge.top, 1.2, 1.2)
    return L


def level_4():
    """Уровень 4 — крепость на каменной ступени: блиндаж впереди, две комнаты и каменная крыша."""
    L = Layout(4, "Level4", target_x=40.0, sling_x=-66.0, impact_size=11.0, palette=PALETTES[4])
    step_top = L.slab("StoneStep", 2.0, -1.41, 11.0, 2.4)
    # Блиндаж на земле перед ступенью: каменный куб под деревянным навесом.
    L.pillar("wood", -7.6, G, 2.4)
    L.stone(-6.4, G, 1.2, 1.2)
    b = L.pillar("wood", -5.2, G, 2.4)
    L.wood(-6.4, b.top, 3.6, T)
    # Первый этаж крепости.
    L.pillar("wood", -2.9, step_top, 3.0)
    L.pillar("wood", 2.0, step_top, 3.0)
    p = L.pillar("wood", 6.9, step_top, 3.0)
    floor1 = L.wood(2.0, p.top, 10.6, T)
    # Центральная комната под каменной крышей.
    L.pillar("wood", -1.9, floor1.top, 2.4)
    c = L.pillar("wood", 1.9, floor1.top, 2.4)
    roof = L.stone(0.0, c.top, 4.8, T)
    L.mouse(0.0, floor1.top)
    # Правая комната.
    L.pillar("wood", 3.3, floor1.top, 2.4)
    r = L.pillar("wood", 6.9, floor1.top, 2.4)
    L.wood(5.1, r.top, 4.2, T)
    L.mouse(5.1, floor1.top)
    # Мышь на крыше между деревянными кубами.
    L.wood(-1.75, roof.top, 1.2, 1.2)
    L.mouse(0.0, roof.top)
    L.wood(1.75, roof.top, 1.2, 1.2)
    return L


def level_5():
    """Уровень 5 — замок на каменном цоколе: два этажа, башенка и сторожевая вышка справа."""
    L = Layout(5, "Level5", target_x=42.0, sling_x=-70.0, impact_size=12.0, palette=PALETTES[5])
    base_top = L.slab("TowerBase", -1.0, -1.11, 13.0, 3.0)
    right_top = L.slab("RightPodium", 10.0, -1.11, 6.0, 3.0)
    # Цокольный этаж: каменные стойки.
    L.pillar("stone", -6.6, base_top, 3.0)
    L.pillar("stone", -1.0, base_top, 3.0)
    s = L.pillar("stone", 4.6, base_top, 3.0)
    floor1 = L.wood(-1.0, s.top, 12.4, T)
    # Первый этаж: три пролёта — мышь, каменный куб, мышь.
    L.pillar("wood", -6.6, floor1.top, 2.4)
    L.pillar("wood", -2.8, floor1.top, 2.4)
    L.pillar("wood", 0.8, floor1.top, 2.4)
    q = L.pillar("wood", 4.6, floor1.top, 2.4)
    L.mouse(-4.7, floor1.top)
    L.stone(-1.0, floor1.top, 1.2, 1.2)
    L.mouse(2.7, floor1.top)
    floor2 = L.wood(-1.0, q.top, 12.4, T)
    # Башенка на втором этаже с каменной крышей.
    L.pillar("wood", -2.8, floor2.top, 2.4)
    t = L.pillar("wood", 0.8, floor2.top, 2.4)
    roof = L.stone(-1.0, t.top, 5.4, T)
    L.wood(-1.0, floor2.top, 1.2, 1.2)
    L.wood(-3.05, roof.top, 1.2, 1.2)
    L.mouse(-1.0, roof.top)
    L.wood(1.05, roof.top, 1.2, 1.2)
    # Сторожевая вышка на правом подиуме.
    L.pillar("wood", 8.0, right_top, 3.0)
    v = L.pillar("wood", 12.0, right_top, 3.0)
    deck = L.wood(10.0, v.top, 5.6, T)
    L.wood(7.85, deck.top, 1.2, 1.2)
    L.mouse(10.0, deck.top)
    L.wood(12.15, deck.top, 1.2, 1.2)
    return L


LEVELS = [level_1, level_2, level_3, level_4, level_5]


def all_layouts():
    return [build() for build in LEVELS]


if __name__ == "__main__":
    for lay in all_layouts():
        print("Уровень %d (%s): блоков %d, мышей %d" % (
            lay.level_id, lay.scene, len(lay.blocks), len(lay.mice)))
        for it in lay.items:
            print("   %-10s %-6s x=%7.3f y=%7.3f w=%.2f h=%.2f rot=%d" % (
                it.name, it.kind, it.x, it.y, it.w, it.h, it.rot))

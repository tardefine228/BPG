# -*- coding: utf-8 -*-
"""Необязательная проверка построек в Box2D (тот же физический движок, что у Unity 2D).

    pip install Box2D
    python Tools/level_tools/check_layouts.py            # устойчивость + пристрелка
    python Tools/level_tools/check_layouts.py --quick    # только устойчивость
    python Tools/level_tools/check_layouts.py --ammo     # + серии выстрелов в пределах боезапаса

Повторяются настройки проекта: шаг 0.02 с, итерации 8/3, «кожа» коллайдеров 0.01,
материал блоков (трение 0.4, упругость 0.1), снаряд массой 1 и радиусом 0.5,
гравитация снаряда x2, сопротивление воздуха 0.06, скорость запуска до 62.5.

Проверяется:
  1. постройка стоит 12 секунд без толчков: ни один блок не сдвинулся, мыши живы;
  2. ни одна мышь не стоит на земле;
  3. пристрелка: перебор углов и силы — сколько мышей можно победить одним выстрелом
     (мышь побеждена при касании земли, падении за карту или ударах сильнее порога);
  4. --ammo: «внимательный игрок» стреляет сериями в пределах боезапаса уровня
     (Max Projectiles читается из сцены). Как точки прогноза в игре, он видит траекторию
     до первого столкновения и выбирает ту, что упирается в живую мышь; если таких нет —
     в блок рядом с мышью. Между выстрелами 8 с, кот исчезает через 12 с (Projectile Lifetime).
Правила урона мыши совпадают со скриптом MouseTarget.
"""
import math
import os
import random
import re
import sys

try:
    from Box2D import b2World, b2ContactListener, b2RayCastCallback
except ImportError:  # pragma: no cover
    sys.exit("Нужен пакет Box2D: pip install Box2D")

import level_layouts as LL

DT = 0.02
GRAVITY = -9.81
LAUNCH_LOCAL = (-2.17, 1.56)   # LaunchPoint относительно Slingshot
SLING_Y = -0.43
MAX_SPEED = 2.5 * 25.0
PROJECTILE_RADIUS = 0.5

# Совпадает со значениями по умолчанию в MouseTarget.cs.
MOUSE_LIVES = 2
MOUSE_DAMAGE_SPEED = 4.0
MOUSE_LETHAL_SPEED = 10.0
MOUSE_SETTLE = 1.0
MOUSE_HIT_COOLDOWN = 0.2
MOUSE_MASS = 0.6
FALL_Y = -6.0
SERIES_GAMES = 20


class Listener(b2ContactListener):
    def __init__(self, sim):
        b2ContactListener.__init__(self)
        self.sim = sim

    def BeginContact(self, contact):
        self.sim.on_contact(contact)

    def PreSolve(self, contact, old_manifold):
        pass

    def PostSolve(self, contact, impulse):
        pass

    def EndContact(self, contact):
        pass


class Sim:
    def __init__(self, layout):
        self.layout = layout
        self.world = b2World(gravity=(0, GRAVITY), doSleep=True)
        self.listener = Listener(self)
        self.world.contactListener = self.listener
        self.time = 0.0
        self.mice = {}
        self.events = []
        ox = layout.target_x
        # Земля: огромный статичный прямоугольник, верх на GROUND_TOP.
        self.ground = self.world.CreateStaticBody(position=(5.0, LL.GROUND_TOP - 40.0))
        self.ground.CreatePolygonFixture(box=(105.0, 40.0), friction=0.4, restitution=0.0)
        self.ground.userData = {"kind": "ground"}
        for name, cx, cy, w, h in layout.slabs:
            slab = self.world.CreateStaticBody(position=(ox + cx, cy))
            slab.CreatePolygonFixture(box=(w / 2, h / 2), friction=0.4, restitution=0.0)
            slab.userData = {"kind": "slab", "name": name}
        self.bodies = []
        for it in layout.items:
            angle = math.radians(it.rot)
            body = self.world.CreateDynamicBody(position=(ox + it.x, it.y), angle=angle)
            body.angularDamping = 0.05
            body.bullet = True
            if it.kind == "mouse":
                r = LL.MOUSE_COLLIDER_H / 2
                half = (LL.MOUSE_COLLIDER_W - LL.MOUSE_COLLIDER_H) / 2
                area = LL.MOUSE_COLLIDER_W * LL.MOUSE_COLLIDER_H - (4 - math.pi) * r * r
                dens = MOUSE_MASS / area
                body.CreatePolygonFixture(box=(half, r), density=dens, friction=0.4, restitution=0.0)
                body.CreateCircleFixture(radius=r, pos=(-half, 0), density=dens, friction=0.4, restitution=0.0)
                body.CreateCircleFixture(radius=r, pos=(half, 0), density=dens, friction=0.4, restitution=0.0)
                state = {"kind": "mouse", "name": it.name, "lives": it.lives or MOUSE_LIVES,
                         "dead": False, "why": "", "next_hit": 0.0}
                body.userData = state
                self.mice[it.name] = (body, state)
            else:
                dens = LL.WOOD_DENSITY if it.kind == "wood" else LL.STONE_DENSITY
                body.CreatePolygonFixture(box=(it.w / 2, it.h / 2), density=dens,
                                          friction=0.4, restitution=0.1)
                body.userData = {"kind": it.kind, "name": it.name}
            self.bodies.append((it, body, (body.position.x, body.position.y, body.angle)))

    # Урон мыши по правилам MouseTarget.
    def on_contact(self, contact):
        a = contact.fixtureA.body
        b = contact.fixtureB.body
        for me, other in ((a, b), (b, a)):
            st = me.userData
            if not st or st.get("kind") != "mouse" or st["dead"]:
                continue
            if self.time < MOUSE_SETTLE:
                continue
            ok = other.userData or {}
            if ok.get("kind") == "ground":
                self.kill(st, "земля")
                continue
            wm = contact.worldManifold
            point = wm.points[0] if contact.manifold.pointCount > 0 else me.position
            va = me.GetLinearVelocityFromWorldPoint(point)
            vb = other.GetLinearVelocityFromWorldPoint(point)
            rel = math.hypot(va[0] - vb[0], va[1] - vb[1])
            if rel >= MOUSE_LETHAL_SPEED:
                self.kill(st, "сильный удар %.1f" % rel)
            elif rel >= MOUSE_DAMAGE_SPEED and self.time >= st["next_hit"]:
                st["next_hit"] = self.time + MOUSE_HIT_COOLDOWN
                st["lives"] -= 1
                if st["lives"] <= 0:
                    self.kill(st, "удары")

    def kill(self, st, why):
        if not st["dead"]:
            st["dead"] = True
            st["why"] = why

    def step(self, seconds):
        n = int(round(seconds / DT))
        for _ in range(n):
            self.world.Step(DT, 8, 3)
            self.time += DT
            for name, (body, st) in self.mice.items():
                if st["dead"]:
                    continue
                if body.position.y < FALL_Y:
                    self.kill(st, "упала за карту")
                # Касание земли в покое (OnCollisionStay2D).
                if self.time >= MOUSE_SETTLE:
                    for ce in body.contacts:
                        if ce.contact.touching and (ce.other.userData or {}).get("kind") == "ground":
                            self.kill(st, "земля")
            # Побеждённые мыши исчезают (как в игре).
            for name, (body, st) in self.mice.items():
                if st["dead"] and body.active:
                    body.active = False

    def max_drift(self):
        worst = (0.0, None)
        for it, body, (x0, y0, a0) in self.bodies:
            d = math.hypot(body.position.x - x0, body.position.y - y0)
            d = max(d, abs(body.angle - a0) * max(it.w, it.h) / 2)
            if d > worst[0]:
                worst = (d, it.name)
        return worst

    def launch(self, angle_deg, power):
        sx = self.layout.sling_x + LAUNCH_LOCAL[0]
        sy = SLING_Y + LAUNCH_LOCAL[1]
        body = self.world.CreateDynamicBody(position=(sx, sy))
        body.CreateCircleFixture(radius=PROJECTILE_RADIUS, density=1.0 / (math.pi * 0.25),
                                 friction=0.9, restitution=0.02)
        body.gravityScale = 2.0
        body.linearDamping = 0.06
        body.angularDamping = 0.05
        body.bullet = True
        body.userData = {"kind": "projectile"}
        a = math.radians(angle_deg)
        speed = MAX_SPEED * power
        body.linearVelocity = (math.cos(a) * speed, math.sin(a) * speed)
        return body


def overlaps(layout):
    problems = []
    items = layout.items
    for i in range(len(items)):
        a = items[i]
        for j in range(i + 1, len(items)):
            b = items[j]
            aw = LL.MOUSE_COLLIDER_W if a.kind == "mouse" else a.ext_w
            bw = LL.MOUSE_COLLIDER_W if b.kind == "mouse" else b.ext_w
            ah = LL.MOUSE_COLLIDER_H if a.kind == "mouse" else a.ext_h
            bh = LL.MOUSE_COLLIDER_H if b.kind == "mouse" else b.ext_h
            dx = abs(a.x - b.x) - (aw + bw) / 2
            dy = abs(a.y - b.y) - (ah + bh) / 2
            if dx < -0.001 and dy < -0.001:
                problems.append("пересекаются %s и %s" % (a.name, b.name))
    # Каждый элемент должен на что-то опираться; мышь — только на блок постройки.
    for it in items:
        w = LL.MOUSE_COLLIDER_W if it.kind == "mouse" else it.ext_w
        support = []
        if abs(it.bottom - LL.GROUND_TOP) < 0.05:
            support.append("земля")
        for name, cx, cy, sw, sh in layout.slabs:
            if abs(it.bottom - (cy + sh / 2)) < 0.05 and abs(it.x - cx) < (w + sw) / 2:
                support.append("площадка " + name)
        for other in items:
            if other is it or other.kind == "mouse":
                continue
            if abs(it.bottom - other.top) < 0.05 and abs(it.x - other.x) < (w + other.ext_w) / 2 - 0.05:
                support.append(other.name)
        if not support:
            problems.append("%s висит в воздухе" % it.name)
        if it.kind == "mouse" and any(s == "земля" or s.startswith("площадка") for s in support):
            problems.append("%s стоит на земле/площадке" % it.name)
    return problems


def stability(layout, seconds=12.0):
    sim = Sim(layout)
    sim.step(seconds)
    drift, who = sim.max_drift()
    dead = [(n, st["why"]) for n, (b, st) in sim.mice.items() if st["dead"]]
    return drift, who, dead


def sweep(layout, angles, powers, seconds=9.0):
    results = []
    for ang in angles:
        for pw in powers:
            sim = Sim(layout)
            sim.step(MOUSE_SETTLE + 0.2)
            sim.launch(ang, pw)
            sim.step(seconds)
            dead = sum(1 for _, (b, st) in sim.mice.items() if st["dead"])
            results.append((ang, pw, dead))
    return results


# ------------------------------------------------------------ серии выстрелов (--ammo)
SCENES = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Assets", "Scenes")
SHOT_INTERVAL = 8.0
PROJECTILE_LIFETIME = 12.0
AIM_ANGLES = [a * 1.25 for a in range(-8, 49)]
AIM_POWERS = [0.55 + 0.025 * i for i in range(19)]


def scene_ammo(layout):
    """Боезапас уровня: LevelSession → Max Projectiles в сцене."""
    with open(os.path.join(SCENES, layout.scene + ".unity"), encoding="utf-8") as f:
        m = re.search(r"^  maxProjectiles: (\d+)", f.read(), re.M)
    return int(m.group(1)) if m else 5


class _FirstHit(b2RayCastCallback):
    def __init__(self):
        b2RayCastCallback.__init__(self)
        self.body = None
        self.fraction = 2.0

    def ReportFixture(self, fixture, point, normal, fraction):
        if (fixture.body.userData or {}).get("kind") == "projectile" or not fixture.body.active:
            return -1.0
        if fraction < self.fraction:
            self.body, self.fraction = fixture.body, fraction
        return fraction


def first_hit(sim, angle_deg, power):
    """Первое тело на траектории — то, на чём обрываются точки прогноза в игре."""
    x = sim.layout.sling_x + LAUNCH_LOCAL[0]
    y = SLING_Y + LAUNCH_LOCAL[1]
    a = math.radians(angle_deg)
    vx, vy = math.cos(a) * MAX_SPEED * power, math.sin(a) * MAX_SPEED * power
    damp = 1.0 / (1.0 + DT * 0.06)
    for _ in range(600):
        vy += GRAVITY * 2.0 * DT
        vx, vy = vx * damp, vy * damp
        nx, ny = x + vx * DT, y + vy * DT
        d = math.hypot(nx - x, ny - y) or 1e-9
        px, py = -(ny - y) / d * 0.45, (nx - x) / d * 0.45
        hit, best = None, 2.0
        for ox, oy in ((0.0, 0.0), (px, py), (-px, -py)):
            cb = _FirstHit()
            sim.world.RayCast(cb, (x + ox, y + oy), (nx + ox, ny + oy))
            if cb.body is not None and cb.fraction < best:
                hit, best = cb.body, cb.fraction
        if hit is not None:
            return hit
        x, y = nx, ny
        if y < FALL_Y or x > sim.layout.target_x + 40:
            return None
    return None


def careful_shot(sim, rng):
    live = [b for b, st in sim.mice.values() if not st["dead"]]
    direct, near, other = [], [], []
    for ang in AIM_ANGLES:
        for pw in AIM_POWERS:
            hit = first_hit(sim, ang, pw)
            kind = (hit.userData or {}).get("kind") if hit is not None else None
            if kind == "mouse" and hit in live:
                direct.append((ang, pw))
            elif kind in ("wood", "stone"):
                close = any(abs(hit.position.x - m.position.x) < 2.5 for m in live)
                (near if close else other).append((ang, pw))
    return rng.choice(direct or near or other or [(20.0, 0.8)])


def play_series(layout, ammo, rng):
    """Номер выстрела, после которого побеждены все мыши, или None."""
    sim = Sim(layout)
    sim.step(MOUSE_SETTLE + 0.2)
    flying = []
    for shot in range(1, ammo + 1):
        flying.append((sim.time, sim.launch(*careful_shot(sim, rng))))
        for _ in range(int(round(SHOT_INTERVAL / DT))):
            sim.step(DT)
            for i, (t0, body) in enumerate(flying):
                if body is not None and sim.time - t0 >= PROJECTILE_LIFETIME:
                    sim.world.DestroyBody(body)
                    flying[i] = (t0, None)
        if all(st["dead"] for _, st in sim.mice.values()):
            return shot
    return None


def main():
    quick = "--quick" in sys.argv
    only = [int(a) for a in sys.argv[1:] if a.isdigit()]
    ok = True
    for lay in LL.all_layouts():
        if only and lay.level_id not in only:
            continue
        print("=== Уровень %d (%s): блоков %d, мышей %d" % (
            lay.level_id, lay.scene, len(lay.blocks), len(lay.mice)))
        probs = overlaps(lay)
        for p in probs:
            print("   ОШИБКА:", p)
        ok &= not probs
        drift, who, dead = stability(lay)
        status = "OK" if drift < 0.05 and not dead else "НЕУСТОЙЧИВО"
        ok &= status == "OK"
        print("   покой 12 с: максимальный сдвиг %.4f (%s), погибло мышей %d -> %s" % (
            drift, who, len(dead), status))
        for n, why in dead:
            print("      %s: %s" % (n, why))
        if quick:
            continue
        angles = [a * 2.5 for a in range(-4, 25)]
        powers = [0.55 + 0.05 * i for i in range(10)]
        res = sweep(lay, angles, powers)
        total = len(lay.mice)
        best = max(r[2] for r in res)
        hits = sum(1 for r in res if r[2] > 0)
        full = sum(1 for r in res if r[2] == total)
        print("   пристрелка: выстрелов %d, задевают мышей %d, лучший выстрел: %d из %d мышей, "
              "все мыши одним выстрелом: %d вариантов" % (len(res), hits, best, total, full))
        if "--ammo" in sys.argv:
            ammo = scene_ammo(lay)
            rng = random.Random(lay.level_id)
            games = [play_series(lay, ammo, rng) for _ in range(SERIES_GAMES)]
            wins = [g for g in games if g is not None]
            print("   серии (котов %d): внимательный игрок побеждает в %d из %d попыток; "
                  "выстрелов до победы: %s" % (ammo, len(wins), len(games),
                                               ", ".join("%d — %d" % (k, wins.count(k)) for k in range(1, ammo + 1))))
    print("ИТОГ:", "OK" if ok else "ЕСТЬ ПРОБЛЕМЫ")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())

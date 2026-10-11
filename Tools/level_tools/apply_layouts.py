# -*- coding: utf-8 -*-
"""Записывает постройки из level_layouts.py в сцены уровней (нужен только Python 3).

    python Tools/level_tools/apply_layouts.py            # все уровни
    python Tools/level_tools/apply_layouts.py 3          # только уровень 3
    python Tools/level_tools/apply_layouts.py --check    # только проверить сцены

ВНИМАНИЕ: всё содержимое объекта TargetArea в сцене заменяется постройкой из раскладки.
Если постройку уже правили вручную в Unity, сначала перенесите правки в level_layouts.py.

Что делает скрипт:
  * удаляет старые блоки внутри TargetArea и ставит экземпляры префабов
    Assets/Prefabs/Blocks/WoodBlock|StoneBlock.prefab и Assets/Prefabs/Mouse.prefab;
  * ставит TargetArea в центр постройки и подбирает размер камеры «Цель» (impactSize);
  * переносит каменные площадки (StoneStep, TowerBase, RightPodium) и рисует их
    каменной текстурой, лишние площадки удаляет;
  * помечает землю (FlatGround) и площадки тегом Ground;
  * проверяет целостность YAML: ссылки, связи Transform и список SceneRoots.
UI, камера, звук и остальные объекты сцены не трогаются.
"""
import io
import math
import os
import re
import sys

import level_layouts as LL

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
SCENES = os.path.join(ROOT, "Assets", "Scenes")

DOC_RE = re.compile(r"^--- !u!(\d+) &(-?\d+)( stripped)?\n", re.M)
GO, TR, RECT, SR, BOX, MB, PREFAB = 1, 4, 224, 212, 61, 114, 1001
TRANSFORMS = (TR, RECT)
ROOTS_ID = 9223372036854775807
ASPECT = 16.0 / 9.0
SLAB_TINT = "{r: 0.78, g: 0.78, b: 0.84, a: 1}"
SLAB_NAMES = ("StonePodium", "StoneStep", "TowerBase", "RightPodium")


class Doc:
    __slots__ = ("cls", "id", "stripped", "body")

    def __init__(self, cls, fid, stripped, body):
        self.cls = cls
        self.id = fid
        self.stripped = stripped
        self.body = body

    def text(self):
        return "--- !u!%d &%d%s\n%s" % (self.cls, self.id, " stripped" if self.stripped else "", self.body)


def parse(text):
    matches = list(DOC_RE.finditer(text))
    header = text[:matches[0].start()]
    docs = []
    for i, m in enumerate(matches):
        end = matches[i + 1].start() if i + 1 < len(matches) else len(text)
        docs.append(Doc(int(m.group(1)), int(m.group(2)), bool(m.group(3)), text[m.end():end]))
    return header, docs


def field(body, name):
    m = re.search(r"^\s*%s: (.*)$" % re.escape(name), body, re.M)
    return m.group(1).strip() if m else None


def ref(value):
    m = re.search(r"fileID: (-?\d+)", value or "")
    return int(m.group(1)) if m else 0


def children(body):
    if "m_Children:" not in body:
        return []
    seg = body.split("m_Children:")[1].split("m_Father")[0]
    return [int(x) for x in re.findall(r"- \{fileID: (-?\d+)\}", seg)]


def components(body):
    return [int(x) for x in re.findall(r"component: \{fileID: (-?\d+)\}", body)]


def fmt(v):
    v = round(float(v), 6)
    if v == int(v):
        return str(int(v))
    return format(v, ".7g")


# ------------------------------------------------------------------ префабы
class PrefabInfo:
    def __init__(self, rel):
        path = os.path.join(ROOT, rel)
        meta = io.open(path + ".meta", encoding="utf-8").read()
        self.guid = re.search(r"^guid: ([0-9a-f]{32})", meta, re.M).group(1)
        _, docs = parse(io.open(path, encoding="utf-8").read())
        self.rel = rel
        self.go = self.tr = self.sr = self.box = self.mouse = None
        self.sr_size = self.box_size = None
        self.auto_tiling = False
        for d in docs:
            if d.cls == TR and ref(field(d.body, "m_Father")) == 0:
                self.tr = d.id
                self.go = ref(field(d.body, "m_GameObject"))
            elif d.cls == SR:
                self.sr = d.id
                self.sr_size = vec2(field(d.body, "m_Size"))
            elif d.cls == BOX:
                self.box = d.id
                self.box_size = vec2(field(d.body, "m_Size"))
                self.auto_tiling = field(d.body, "m_AutoTiling") == "1"
            elif d.cls == MB and "MouseTarget" in d.body:
                self.mouse = d.id
        assert self.go and self.tr and self.sr, rel


def vec2(value):
    m = re.match(r"\{x: ([-\d.e]+), y: ([-\d.e]+)\}", value or "")
    return (float(m.group(1)), float(m.group(2))) if m else None


def sprite_guid(rel):
    meta = io.open(os.path.join(ROOT, rel + ".meta"), encoding="utf-8").read()
    return re.search(r"^guid: ([0-9a-f]{32})", meta, re.M).group(1)


WOOD = PrefabInfo("Assets/Prefabs/Blocks/WoodBlock.prefab")
STONE = PrefabInfo("Assets/Prefabs/Blocks/StoneBlock.prefab")
MOUSE = PrefabInfo("Assets/Prefabs/Mouse.prefab")
STONE_SPRITE = sprite_guid("Assets/Sprites/Blocks/Stone.png")


def modification(target, guid, path, value):
    return ("    - target: {fileID: %d, guid: %s, type: 3}\n"
            "      propertyPath: %s\n"
            "      value: %s\n"
            "      objectReference: {fileID: 0}\n") % (target, guid, path, value)


def instance_docs(item, prefab, parent_tr, instance_id):
    g = prefab.guid
    mods = []
    if item.kind == "mouse":
        ox, oy = LL.MOUSE_COLLIDER_OFFSET
        x, y = item.x - ox, item.y - oy
    else:
        x, y = item.x, item.y
    angle = math.radians(item.rot)
    qz, qw = math.sin(angle / 2), math.cos(angle / 2)
    for axis, val in (("x", x), ("y", y), ("z", 0)):
        mods.append(modification(prefab.tr, g, "m_LocalPosition." + axis, fmt(val)))
    for axis, val in (("w", qw), ("x", 0), ("y", 0), ("z", qz)):
        mods.append(modification(prefab.tr, g, "m_LocalRotation." + axis, fmt(val)))
    for axis, val in (("x", 0), ("y", 0), ("z", item.rot)):
        mods.append(modification(prefab.tr, g, "m_LocalEulerAnglesHint." + axis, fmt(val)))
    mods.append(modification(prefab.go, g, "m_Name", item.name))
    if item.kind != "mouse":
        # Размер блока задаётся явно и одинаково у спрайта и коллайдера.
        # Auto Tiling у коллайдеров блоков выключен: Unity не пересчитывает m_Size сама.
        for target in (prefab.sr, prefab.box):
            mods.append(modification(target, g, "m_Size.x", fmt(item.w)))
            mods.append(modification(target, g, "m_Size.y", fmt(item.h)))
    elif item.lives and item.lives != 2:
        mods.append(modification(prefab.mouse, g, "lives", str(item.lives)))
    body = ("PrefabInstance:\n"
            "  m_ObjectHideFlags: 0\n"
            "  serializedVersion: 2\n"
            "  m_Modification:\n"
            "    serializedVersion: 3\n"
            "    m_TransformParent: {fileID: %d}\n"
            "    m_Modifications:\n%s"
            "    m_RemovedComponents: []\n"
            "    m_RemovedGameObjects: []\n"
            "    m_AddedGameObjects: []\n"
            "    m_AddedComponents: []\n"
            "  m_SourcePrefab: {fileID: 100100000, guid: %s, type: 3}\n") % (parent_tr, "".join(mods), g)
    # Так же, как Unity: id объекта экземпляра = id в префабе XOR id экземпляра.
    stripped_id = (prefab.tr ^ instance_id) & 0x7FFFFFFFFFFFFFFF
    stripped = ("Transform:\n"
                "  m_CorrespondingSourceObject: {fileID: %d, guid: %s, type: 3}\n"
                "  m_PrefabInstance: {fileID: %d}\n"
                "  m_PrefabAsset: {fileID: 0}\n") % (prefab.tr, g, instance_id)
    return Doc(PREFAB, instance_id, False, body), Doc(TR, stripped_id, True, stripped)


# ------------------------------------------------------------------ сцена
class Scene:
    def __init__(self, path):
        self.path = path
        self.header, self.docs = parse(io.open(path, encoding="utf-8").read())

    @property
    def byid(self):
        return {d.id: d for d in self.docs}

    def go_named(self, name):
        found = [d for d in self.docs if d.cls == GO and field(d.body, "m_Name") == name]
        return found[0] if found else None

    def comp(self, go, cls):
        byid = self.byid
        for c in components(go.body):
            if c in byid and byid[c].cls == cls:
                return byid[c]
        return None

    def remove_ids(self, ids):
        self.docs = [d for d in self.docs if d.id not in ids]

    def collect_object(self, tr_id, out):
        """id всех документов объекта (вместе с детьми) для удаления."""
        byid = self.byid
        tr = byid[tr_id]
        if tr.stripped:
            inst = ref(field(tr.body, "m_PrefabInstance"))
            out.add(inst)
            for d in self.docs:
                if d.stripped and ref(field(d.body, "m_PrefabInstance")) == inst:
                    out.add(d.id)
            return
        for child in children(tr.body):
            self.collect_object(child, out)
        go = ref(field(tr.body, "m_GameObject"))
        out.add(go)
        out.update(components(byid[go].body))

    def save(self):
        self.docs.sort(key=lambda d: (d.id == ROOTS_ID, d.id))
        text = self.header + "".join(d.text() for d in self.docs)
        io.open(self.path, "w", encoding="utf-8", newline="\n").write(text)


def set_line(body, name, value):
    new, n = re.subn(r"^(\s*%s: ).*$" % re.escape(name), lambda m: m.group(1) + value, body, count=1, flags=re.M)
    assert n == 1, name
    return new


def set_children(body, ids):
    head, rest = body.split("  m_Children:", 1)
    tail = rest[rest.index("  m_Father:"):]
    kids = "  m_Children:" + (" []\n" if not ids else "\n" + "".join("  - {fileID: %d}\n" % i for i in ids))
    return head + kids + tail


def apply(layout):
    path = os.path.join(SCENES, layout.scene + ".unity")
    scene = Scene(path)

    # Центр постройки по горизонтали становится позицией TargetArea.
    xs = [i.left for i in layout.items] + [s[1] - s[3] / 2 for s in layout.slabs]
    xe = [i.right for i in layout.items] + [s[1] + s[3] / 2 for s in layout.slabs]
    shift = round((min(xs) + max(xe)) / 2, 2)
    target_x = layout.target_x + shift

    area = scene.go_named("TargetArea")
    area_tr = scene.comp(area, TR)
    old = set()
    for child in children(area_tr.body):
        scene.collect_object(child, old)
    scene.remove_ids(old)

    used = {d.id for d in scene.docs}
    new_docs, kids = [], []
    base = 4000000000 + layout.level_id * 1000000
    for index, item in enumerate(layout.blocks + layout.mice):
        prefab = MOUSE if item.kind == "mouse" else WOOD if item.kind == "wood" else STONE
        moved = LL.Item(item.kind, item.x - shift, item.y, item.w, item.h, item.rot, item.name, item.lives)
        inst_id = base + index * 2
        inst, stripped = instance_docs(moved, prefab, area_tr.id, inst_id)
        assert inst.id not in used and stripped.id not in used
        used.update((inst.id, stripped.id))
        new_docs += [inst, stripped]
        kids.append(stripped.id)
    scene.docs += new_docs
    area_tr.body = set_children(area_tr.body, kids)
    area_tr.body = set_line(area_tr.body, "m_LocalPosition", "{x: %s, y: 0, z: 0}" % fmt(target_x))

    # Каменные площадки: позиция из раскладки, каменная текстура, тег Ground; лишние удаляются.
    wanted = {s[0]: s for s in layout.slabs}
    for name in SLAB_NAMES:
        go = scene.go_named(name)
        if go is None:
            continue
        tr = scene.comp(go, TR)
        if name not in wanted:
            gone = set()
            scene.collect_object(tr.id, gone)
            scene.remove_ids(gone)
            continue
        _, cx, cy, w, h = wanted[name]
        go.body = set_line(go.body, "m_TagString", "Ground")
        tr.body = set_line(tr.body, "m_LocalPosition", "{x: %s, y: %s, z: 0}" % (fmt(layout.target_x + cx), fmt(cy)))
        tr.body = set_line(tr.body, "m_LocalScale", "{x: 1, y: 1, z: 1}")
        sr = scene.comp(go, SR)
        sr.body = set_line(sr.body, "m_Sprite", "{fileID: 21300000, guid: %s, type: 3}" % STONE_SPRITE)
        sr.body = set_line(sr.body, "m_Color", SLAB_TINT)
        sr.body = set_line(sr.body, "m_DrawMode", "2")
        sr.body = set_line(sr.body, "m_Size", "{x: %s, y: %s}" % (fmt(w), fmt(h)))
        box = scene.comp(go, BOX)
        box.body = set_line(box.body, "m_Size", "{x: %s, y: %s}" % (fmt(w), fmt(h)))
    missing = set(wanted) - {n for n in SLAB_NAMES if scene.go_named(n)}
    assert not missing, "в сцене нет площадок %s" % missing

    ground = scene.go_named("FlatGround")
    ground.body = set_line(ground.body, "m_TagString", "Ground")

    # Камера «Цель»: вся постройка с запасом (центр вида на 3 единицы выше TargetArea).
    top = max(i.top for i in layout.items)
    half_w = max(max(xe) - shift, shift - min(xs)) + 2.5
    size = max(9.0, top + 2.0 - 3.0, 3.0 - (LL.GROUND_TOP - 1.5), half_w / ASPECT)
    size = math.ceil(size)
    for d in scene.docs:
        if d.cls == MB and "mapMin:" in d.body and "impactSize:" in d.body:
            d.body = set_line(d.body, "impactSize", fmt(size))

    # SceneRoots: убрать удалённые корневые объекты.
    roots = scene.byid[ROOTS_ID]
    alive = {d.id for d in scene.docs}
    lines = [ln for ln in roots.body.splitlines(True)
             if not re.match(r"  - \{fileID: (-?\d+)\}", ln) or int(re.match(r"  - \{fileID: (-?\d+)\}", ln).group(1)) in alive]
    roots.body = "".join(lines)

    errors = validate(scene) + validate_blocks(scene)
    if errors:
        raise SystemExit("%s: ошибки сцены:\n  %s" % (layout.scene, "\n  ".join(errors[:20])))
    scene.save()
    print("%-9s TargetArea x=%-6s блоков %2d, мышей %d, impactSize %d" % (
        layout.scene, fmt(target_x), len(layout.blocks), len(layout.mice), size))


MOD = re.compile(r"- target: \{fileID: (-?\d+), guid: ([0-9a-f]{32}), type: 3\}\n"
                 r"\s+propertyPath: (\S+)\n\s+value: (.*)\n")


def validate_blocks(scene):
    """Коллайдер каждого блока совпадает с его спрайтом, Auto Tiling не используется.

    С включённым Auto Tiling Unity сама пересчитывает размер BoxCollider2D по рамке
    спрайта; на этом блоки проваливались сквозь землю. Поэтому размер задаётся явно.
    """
    errors = []
    blocks = {WOOD.guid: WOOD, STONE.guid: STONE}
    for info in blocks.values():
        if info.box is None or info.auto_tiling:
            errors.append("%s: у BoxCollider2D должен быть выключен Auto Tiling" % info.rel)
        if info.sr_size != info.box_size:
            errors.append("%s: размер BoxCollider2D не совпадает с размером спрайта" % info.rel)
    for d in scene.docs:
        if d.cls != PREFAB:
            continue
        m = re.search(r"m_SourcePrefab: \{fileID: \d+, guid: ([0-9a-f]{32})", d.body)
        info = blocks.get(m.group(1)) if m else None
        if info is None:
            continue
        mods = {(int(t), path): value.strip() for t, g, path, value in MOD.findall(d.body) if g == info.guid}
        name = mods.get((info.go, "m_Name"), str(d.id))
        if any(t == info.box and (path.startswith("m_SpriteTilingProperty") or path == "m_AutoTiling")
               for t, path in mods):
            errors.append("%s: лишние переопределения Auto Tiling у коллайдера" % name)
        for axis, default_sr, default_box in zip("xy", info.sr_size, info.box_size):
            sr = float(mods.get((info.sr, "m_Size." + axis), default_sr))
            box = float(mods.get((info.box, "m_Size." + axis), default_box))
            if abs(sr - box) > 1e-4:
                errors.append("%s: размер спрайта %s по %s не равен коллайдеру %s" % (name, fmt(sr), axis, fmt(box)))
    return errors


def validate(scene):
    errors = []
    byid = {}
    for d in scene.docs:
        if d.id in byid:
            errors.append("повтор id %d" % d.id)
        byid[d.id] = d
    for d in scene.docs:
        for m in re.finditer(r"\{fileID: (-?\d+)([^}]*)\}", d.body):
            r = int(m.group(1))
            if "guid:" in m.group(2) or r == 0:
                continue
            if r not in byid:
                errors.append("документ %d ссылается на отсутствующий %d" % (d.id, r))
        if d.cls == GO:
            for c in components(d.body):
                if c not in byid:
                    errors.append("у объекта %d нет компонента %d" % (d.id, c))
                elif "m_GameObject: {fileID: %d}" % d.id not in byid[c].body:
                    errors.append("компонент %d не указывает на объект %d" % (c, d.id))
        if d.cls in TRANSFORMS and not d.stripped:
            for k in children(d.body):
                kid = byid.get(k)
                if kid is None:
                    errors.append("у %d нет ребёнка %d" % (d.id, k))
                elif kid.stripped:
                    inst = byid.get(ref(field(kid.body, "m_PrefabInstance")))
                    if inst is None or ref(field(inst.body, "m_TransformParent")) != d.id:
                        errors.append("экземпляр префаба %d не привязан к %d" % (k, d.id))
                elif ref(field(kid.body, "m_Father")) != d.id:
                    errors.append("ребёнок %d не указывает на родителя %d" % (k, d.id))
            father = ref(field(d.body, "m_Father"))
            if father and (father not in byid or d.id not in children(byid[father].body)):
                errors.append("родитель %d не содержит %d" % (father, d.id))
        if d.cls == PREFAB:
            parent = ref(field(d.body, "m_TransformParent"))
            if parent:
                strip = [s.id for s in scene.docs if s.stripped and s.cls == TR
                         and ref(field(s.body, "m_PrefabInstance")) == d.id]
                if not any(s in children(byid[parent].body) for s in strip):
                    errors.append("экземпляр %d отсутствует в m_Children родителя" % d.id)
    roots = byid.get(ROOTS_ID)
    if roots is None:
        errors.append("нет SceneRoots")
    else:
        listed = sorted(int(x) for x in re.findall(r"- \{fileID: (-?\d+)\}", roots.body))
        expected = sorted([d.id for d in scene.docs if d.cls in TRANSFORMS and not d.stripped
                           and ref(field(d.body, "m_Father")) == 0]
                          + [d.id for d in scene.docs if d.cls == PREFAB
                             and ref(field(d.body, "m_TransformParent")) == 0])
        if listed != expected:
            errors.append("SceneRoots не совпадает с корневыми объектами")
    return errors


def main(argv):
    check_only = "--check" in argv
    only = {int(a) for a in argv if a.isdigit()}
    failed = False
    for layout in LL.all_layouts():
        if only and layout.level_id not in only:
            continue
        if check_only:
            scene = Scene(os.path.join(SCENES, layout.scene + ".unity"))
            errors = validate(scene) + validate_blocks(scene)
            print(layout.scene, "OK" if not errors else "ОШИБКИ: " + "; ".join(errors[:5]))
            failed |= bool(errors)
        else:
            apply(layout)
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

#!/usr/bin/env python3
"""
Builds the extra gameplay scenes (Level2 ... Level5) for the BPG Unity project.

The scenes are assembled from objects of the working level 1 scene
(Gameplay.unity), so every component block is exactly the one Unity already
wrote for this project. Only the level defining values differ: positions,
colors, map bounds, targets and static platforms.
"""
import os
import re

# The repository root, computed from this file (Tools/level_tools/<script>).
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC = os.path.join(ROOT, 'Assets/Scenes/Gameplay.unity')
BASE_ID = 3000000000

GO, TR, MONO, SPRITE_RENDERER = 1, 4, 114, 212
SCENE_ROOTS = 1660057539
SETTINGS_CLASSES = (29, 104, 157, 196)

OBJS = {
    'GameManager': (854157702, [854157703, 854157704, 1200000004, 1200000005]),
    'MainCamera': (519420028, [519420032, 519420031, 519420029, 519420030, 519420033]),
    'GlobalLight': (619394800, [619394802, 619394801]),
    'FlatGround': (513407833, [513407835, 513407834, 513407836, 513407837]),
    'Slingshot': (272414282, [272414283]),
    'RotatingPivot': (933486503, [933486504]),
    'LaunchPoint': (837157689, [837157690]),
    'Metatel': (1454540094, [1454540096, 1454540095]),
    'CatapultPart': (1428137472, [1428137474, 1428137473]),
    'AimTrajectory': (277280249, [277280251, 277280250]),
    'Background': (1800000000, [1800000001, 1800000002]),
    'TargetArea': (1800000010, [1800000011]),
    'Box': (1451135823, [1451135827, 1451135826, 1451135825, 1451135824, 1451135828]),
    'EventSystem': (1200000000, [1200000001, 1200000003, 1200000002]),
    'AudioManager': (268435900, [268435901, 268435902]),
}
TRANSFORM, SPRITE, BODY, COLLIDER, SCRIPT = 0, 1, 2, 3, 4
CAMERA_CONTROLLER, LIGHT_COMPONENT = 4, 1


def parse(path):
    text = open(path, encoding='utf-8').read()
    parts = re.split(r'^--- !u!(\d+) &(\d+)\n', text, flags=re.M)
    docs = {}
    for i in range(1, len(parts), 3):
        docs[int(parts[i + 1])] = (int(parts[i]), parts[i + 2])
    return parts[0], docs


HEADER, TMPL = parse(SRC)


def vec(v):
    return '{x: %s, y: %s, z: %s}' % (v[0], v[1], v[2])


def col(c):
    return '{r: %s, g: %s, b: %s, a: %s}' % (c[0], c[1], c[2], c[3])


def clear_children(body):
    pattern = re.compile(r'^  m_Children:(?: \[\])?\n(?:  - \{fileID: \d+\}\n)*', re.M)
    if not pattern.search(body):
        raise KeyError('m_Children not found')
    return pattern.sub('  m_Children: []\n', body, count=1)


def set_field(body, field, value):
    pattern = re.compile(r'^  ' + re.escape(field) + r':[^\n]*$', re.M)
    if not pattern.search(body):
        raise KeyError('field not found: ' + field)
    return pattern.sub(lambda _: '  %s: %s' % (field, value), body, count=1)


class Builder:
    def __init__(self):
        self.docs = []            # (class, id, body)
        self.idmap = {}
        self.parent = {}          # new transform id -> new parent transform id
        self.children = {}        # new transform id -> [new child transform ids]
        self.order = []           # transform ids in creation order
        self.next_id = BASE_ID

    def alloc(self):
        self.next_id += 1
        return self.next_id

    def emit(self, cls, fid, body):
        if not body.endswith('\n'):
            body += '\n'
        self.docs.append((cls, fid, body))

    def clone(self, template, name=None, position=None, scale=None, parent=None,
              fields=None, raw=None):
        """fields: {slot: [(field, value)]}, raw: {slot: [(old, new)]}"""
        old_go, old_comps = OBJS[template]
        go_id = self.alloc()
        comp_ids = [self.alloc() for _ in old_comps]
        # Cross-object references point to the first canonical clone. In
        # particular, extra podiums must not replace the camera's FlatGround.
        self.idmap.setdefault(old_go, go_id)
        for old, new in zip(old_comps, comp_ids):
            self.idmap.setdefault(old, new)

        fields = fields or {}
        raw = raw or {}

        self_ids = {old_go: go_id}
        self_ids.update({old: new for old, new in zip(old_comps, comp_ids)})

        def self_remap(text):
            return re.sub(r'\{fileID: (\d+)\}', lambda m: '{fileID: %d}' %
                          self_ids.get(int(m.group(1)), int(m.group(1))), text)

        cls_go, body_go = TMPL[old_go]
        body_go = self_remap(body_go)
        if name is not None:
            body_go = set_field(body_go, 'm_Name', name)
        self.emit(cls_go, go_id, body_go)

        for slot, old_comp in enumerate(old_comps):
            cls, body = TMPL[old_comp]
            body = self_remap(body)
            body = set_field(body, 'm_GameObject', '{fileID: %d}' % go_id)
            if slot == TRANSFORM:
                if position is not None:
                    body = set_field(body, 'm_LocalPosition', vec(position))
                if scale is not None:
                    body = set_field(body, 'm_LocalScale', vec(scale))
                body = clear_children(body)
                self.parent[comp_ids[TRANSFORM]] = parent or 0
                self.children.setdefault(comp_ids[TRANSFORM], [])
                self.order.append(comp_ids[TRANSFORM])
                if parent:
                    self.children.setdefault(parent, []).append(comp_ids[TRANSFORM])
            for field, value in fields.get(slot, []):
                body = set_field(body, field, value)
            for old, new in raw.get(slot, []):
                if old not in body:
                    raise KeyError('raw patch not found: ' + old)
                body = body.replace(old, new, 1)
            self.emit(cls, comp_ids[slot], body)

        self.go = go_id
        self.tr = comp_ids[TRANSFORM]
        self.comps = comp_ids
        return go_id, comp_ids[TRANSFORM], comp_ids

    def add_block(self, name, local_pos, parent, scale=0.6391, mass=2,
                  color=(1, 1, 1, 1), hits=None):
        go_id, tr_id, comps = self.clone(
            'Box', name=name, position=(local_pos[0], local_pos[1], 0),
            scale=(scale, scale, 1), parent=parent,
            fields={SPRITE: [('m_Color', col(color))],
                    BODY: [('m_Mass', str(mass))]})
        self.docs = [d for d in self.docs if d[1] != comps[SCRIPT]]
        reinforced = hits is not None and hits > 1
        self.emit(MONO, comps[SCRIPT], level_target_body(
            go_id, hits,
            ARMOR_PUSH_RESISTANCE if reinforced else 0,
            ARMOR_SLIDING_FRICTION if reinforced else 1))
        return tr_id

    def add_static_slab(self, name, center, size, color, sorting_order=0):
        return self.clone(
            'FlatGround', name=name, position=(center[0], center[1], 0),
            scale=(size[0], size[1], 1),
            fields={SPRITE: [('m_Color', col(color)),
                             ('m_SortingOrder', str(sorting_order))]})[1]

    def finalize(self):
        """Rewrite every transform link and remap all fileID references."""
        fixed = []
        for cls, fid, body in self.docs:
            if cls == TR:
                body = clear_children(body)
                kids = self.children.get(fid, [])
                if kids:
                    body = body.replace(
                        '  m_Children: []\n',
                        '  m_Children:\n' + ''.join(
                            '  - {fileID: %d}\n' % k for k in kids), 1)
                body = set_field(body, 'm_Father',
                                 '{fileID: %d}' % self.parent.get(fid, 0))
            fixed.append((cls, fid, body))

        out = []
        for cls, fid, body in fixed:
            body = re.sub(r'\{fileID: (\d+)\}',
                          lambda m: '{fileID: %d}' % self.idmap.get(
                              int(m.group(1)), int(m.group(1))), body)
            out.append('--- !u!%d &%d\n%s' % (cls, fid, body))

        roots = [t for t in self.order if self.parent.get(t, 0) == 0]
        out.append('--- !u!%d &9223372036854775807\nSceneRoots:\n'
                   '  m_ObjectHideFlags: 0\n  m_Roots:\n' % SCENE_ROOTS)
        out += ['  - {fileID: %d}\n' % t for t in roots]
        return HEADER + ''.join(out)


def level_target_body(go_id, hits, push_resistance, sliding_friction):
    """Serialised LevelTarget. Ordinary blocks keep hits = 1 and no resistance."""
    lines = [
        'MonoBehaviour:',
        '  m_ObjectHideFlags: 0',
        '  m_CorrespondingSourceObject: {fileID: 0}',
        '  m_PrefabInstance: {fileID: 0}',
        '  m_PrefabAsset: {fileID: 0}',
        '  m_GameObject: {fileID: %d}' % go_id,
        '  m_Enabled: 1',
        '  m_EditorHideFlags: 0',
        '  m_Script: {fileID: 11500000, guid: d1a5f4e3b7c95062e8f0a2b4c3d6e579, type: 3}',
        '  m_Name:',
        '  m_EditorClassIdentifier: Assembly-CSharp::LevelTarget',
        '  fallY: -6',
        '  impactSpeed: 2',
        '  hitsToClear: %d' % (1 if hits is None else hits),
        '  hitCooldown: 0.35',
        '  showHitFeedback: 1',
        '  damagedTint: {r: 0.32, g: 0.31, b: 0.36, a: 1}',
        '  hitFlashColor: {r: 1, g: 0.85, b: 0.4, a: 1}',
        '  hitFlashDuration: 0.12',
        '  breakOnCleared: 1',
        '  breakImpulse: 6',
        '  breakTorque: 30',
        '  pushResistance: %s' % push_resistance,
        '  slidingFriction: %s' % sliding_friction,
        '  onHit:',
        '    m_PersistentCalls:',
        '      m_Calls: []',
        '  onCleared:',
        '    m_PersistentCalls:',
        '      m_Calls: []',
    ]
    return '\n'.join(lines) + '\n'


STEEL = (0.62, 0.66, 0.74, 1)

# Victory jingles referenced by the LevelSession component of every scene. The
# GUIDs are read from the .meta files, so re-importing the wavs keeps working.
VICTORY_CLIP_PATHS = {
    3: 'Assets/Resources/Audio/Victory/Victory3Stars.wav',
    2: 'Assets/Resources/Audio/Victory/Victory2Stars.wav',
    1: 'Assets/Resources/Audio/Victory/Victory1Stars.wav',
}


def audio_clip_ref(clip_path):
    meta_path = os.path.join(ROOT, clip_path + '.meta')
    if not os.path.exists(meta_path):
        raise FileNotFoundError('missing audio meta: ' + clip_path)
    with open(meta_path, encoding='utf-8') as handle:
        guid = re.search(r'^guid: (\w+)$', handle.read(), re.M).group(1)
    return '{fileID: 8300000, guid: %s, type: 3}' % guid


def apply_victory_sounds(scene_text):
    """
    Keeps exactly one victory-sound block in the scene: the Gameplay template
    already carries the fields, so they are refreshed (clip GUIDs) instead of
    being appended a second time.
    """
    existing = re.compile(r'^  (?:victory3StarsSound|victory2StarsSound|'
                          r'victory1StarsSound|victorySoundDelay|victoryVolume):.*\n',
                          re.M)
    scene_text = existing.sub('', scene_text)

    lines = scene_text.splitlines(keepends=True)
    output = []
    for line in lines:
        output.append(line)
        if line.strip() == 'completeDelay: %s' % 1.1:
            output.append(victory_sound_fields())
    return ''.join(output)


def victory_sound_fields():
    return ('  victory3StarsSound: %s\n'
            '  victory2StarsSound: %s\n'
            '  victory1StarsSound: %s\n'
            '  victorySoundDelay: 0.15\n'
            '  victoryVolume: 0.9\n' % (
                audio_clip_ref(VICTORY_CLIP_PATHS[3]),
                audio_clip_ref(VICTORY_CLIP_PATHS[2]),
                audio_clip_ref(VICTORY_CLIP_PATHS[1])))

LEVEL2 = {
    'file': 'Assets/Scenes/Level2.unity',
    'level_id': 2,
    'slingshot_x': -64.965,
    'target_area': (45.0, 0.0),
    'ground_color': (0.76, 0.70, 0.62, 1),
    'background_color': (0.50, 0.42, 0.62, 1),
    'light_color': (1.0, 0.94, 0.86, 1),
    'map_min': (-85, -7.5),
    'map_max': (95, 62.5),
    'impact_size': 18,
    'blocks': [
        {'name': 'Box_A1', 'pos': (36.57, -1.20)},
        {'name': 'Box_A2', 'pos': (39.43, -1.20)},
        {'name': 'DurableBlock_A', 'pos': (38.00, 1.905), 'scale': 0.75,
         'mass': 5, 'color': STEEL, 'hits': 3},
        {'name': 'Box_B1', 'pos': (52.00, -1.20)},
        {'name': 'Box_B2', 'pos': (52.00, 1.657)},
    ],
    'slabs': [],
}

LEVEL3 = {
    'file': 'Assets/Scenes/Level3.unity',
    'level_id': 3,
    'slingshot_x': -69.0,
    'target_area': (42.0, 0.0),
    'ground_color': (0.55, 0.62, 0.70, 1),
    'background_color': (0.14, 0.18, 0.36, 1),
    'light_color': (0.80, 0.87, 1.0, 1),
    'map_min': (-95, -7.5),
    'map_max': (105, 62.5),
    'impact_size': 30,
    'blocks': [
        {'name': 'Box_A1', 'pos': (30.57, -1.20)},
        {'name': 'Box_A2', 'pos': (33.43, -1.20)},
        {'name': 'DurableBlock_A', 'pos': (32.00, 1.905), 'scale': 0.75,
         'mass': 5, 'color': STEEL, 'hits': 4},
        {'name': 'Box_B1', 'pos': (46.57, 2.03)},
        {'name': 'Box_B2', 'pos': (49.43, 2.03)},
        {'name': 'DurableBlock_B', 'pos': (48.00, 5.02), 'scale': 0.70,
         'mass': 5, 'color': STEEL, 'hits': 5},
    ],
    'slabs': [
        {'name': 'StonePodium', 'center': (46.0, -1.15), 'size': (16.0, 3.5),
         'color': (0.42, 0.43, 0.47, 1), 'order': -1},
    ],
}


# ---------------------------------------------------------------- geometry ----
GROUND_TOP = -2.61          # world y of the walkable ground surface
BLOCK_FULL_SIZE = 4.47      # collider size of a cloned battle box, in its units


def half_h(scale=0.6391):
    """Half of the world height of a battle box clone."""
    return BLOCK_FULL_SIZE * scale * 0.5


def on_ground(scale=0.6391):
    """Center y of a box that lies on the ground (with a small sink)."""
    return round(GROUND_TOP + half_h(scale) - 0.048, 3)


def on_top(top, scale=0.6391):
    """Center y of a box that rests on a surface whose top is at `top`."""
    return round(top + half_h(scale) - 0.02, 3)


# Reinforced blocks swallow most of the hit and brake hard while sliding, so a
# shot only nudges them where an ordinary crate would be thrown aside.
ARMOR_PUSH_RESISTANCE = 0.6
ARMOR_SLIDING_FRICTION = 1.8

STAND = 0.6391              # ordinary crate scale
ARMOR = 0.75                # reinforced block scale (large)
ARMOR_SMALL = 0.70          # reinforced block scale (compact)
SANDSTONE = (0.45, 0.41, 0.38, 1)

# Two steel pillars holding a crate: the gate of the fortress.
GATE_TOP = on_ground(ARMOR) + half_h(ARMOR)
STEP_TOP = -1.41 + 1.2
STEP_BOX_TOP = on_top(STEP_TOP, STAND) + half_h(STAND)

LEVEL4 = {
    'file': 'Assets/Scenes/Level4.unity',
    'level_id': 4,
    'slingshot_x': -66.0,
    'target_area': (36.0, 0.0),
    'ground_color': (0.56, 0.39, 0.31, 1),
    'background_color': (0.80, 0.45, 0.35, 1),
    'light_color': (1.0, 0.84, 0.68, 1),
    'map_min': (-88, -7.5),
    'map_max': (98, 62.5),
    'impact_size': 16,
    'preview_top': 9.0,
    'blocks': [
        {'name': 'DurableBlock_A', 'pos': (30.00, on_ground(ARMOR)), 'scale': ARMOR,
         'mass': 5, 'color': STEEL, 'hits': 3},
        {'name': 'DurableBlock_B', 'pos': (33.80, on_ground(ARMOR)), 'scale': ARMOR,
         'mass': 5, 'color': STEEL, 'hits': 3},
        {'name': 'Box_A1', 'pos': (31.90, on_top(GATE_TOP, STAND))},
        {'name': 'Box_B1', 'pos': (43.00, on_top(STEP_TOP, STAND))},
        {'name': 'DurableBlock_C', 'pos': (43.00, on_top(STEP_BOX_TOP, ARMOR_SMALL)),
         'scale': ARMOR_SMALL, 'mass': 5, 'color': STEEL, 'hits': 4},
    ],
    'slabs': [
        {'name': 'StoneStep', 'center': (43.0, -1.41), 'size': (11.0, 2.4),
         'color': SANDSTONE, 'order': -1},
    ],
}

# The tall tower: a stone base, two crates, the toughest block in the game and
# a crate on top, plus a reinforced block on its own podium behind the tower.
TOWER_BASE_TOP = -1.11 + 1.5
TOWER_BOX_TOP = on_top(TOWER_BASE_TOP, STAND) + half_h(STAND)
TOWER_ARMOR_TOP = on_top(TOWER_BOX_TOP, ARMOR) + half_h(ARMOR)
PODIUM_TOP = -1.11 + 1.5

LEVEL5 = {
    'file': 'Assets/Scenes/Level5.unity',
    'level_id': 5,
    'slingshot_x': -70.0,
    'target_area': (40.0, 0.0),
    'ground_color': (0.36, 0.52, 0.34, 1),
    'background_color': (0.53, 0.73, 0.86, 1),
    'light_color': (1.0, 1.0, 0.95, 1),
    'map_min': (-96, -7.5),
    'map_max': (106, 62.5),
    'impact_size': 20,
    'preview_top': 12.0,
    'blocks': [
        {'name': 'Box_A1', 'pos': (36.70, on_top(TOWER_BASE_TOP, STAND))},
        {'name': 'Box_A2', 'pos': (41.30, on_top(TOWER_BASE_TOP, STAND))},
        {'name': 'DurableBlock_A', 'pos': (39.00, on_top(TOWER_BOX_TOP, ARMOR)),
         'scale': ARMOR, 'mass': 6, 'color': STEEL, 'hits': 6},
        {'name': 'Box_B1', 'pos': (39.00, on_top(TOWER_ARMOR_TOP, STAND))},
        {'name': 'DurableBlock_B', 'pos': (52.00, on_top(PODIUM_TOP, ARMOR_SMALL)),
         'scale': ARMOR_SMALL, 'mass': 6, 'color': STEEL, 'hits': 5},
    ],
    'slabs': [
        {'name': 'TowerBase', 'center': (39.0, -1.11), 'size': (13.0, 3.0),
         'color': (0.48, 0.47, 0.45, 1), 'order': -1},
        {'name': 'RightPodium', 'center': (52.0, -1.11), 'size': (6.0, 3.0),
         'color': (0.48, 0.47, 0.45, 1), 'order': -1},
    ],
}


def build(cfg):
    b = Builder()

    for fid, (cls, body) in TMPL.items():
        if cls in SETTINGS_CLASSES:
            b.emit(cls, fid, body)

    # ground and sky
    ground = b.clone('FlatGround', name='FlatGround', position=(5, -42.61, 0),
                     scale=(210.22223, 80, 1),
                     fields={SPRITE: [('m_Color', col(cfg['ground_color']))]})
    background = b.clone('Background', name='BackgroundPlaceholder',
                         position=(5, 27.5, 10),
                         fields={SPRITE: [('m_Color', col(cfg['background_color']))]})
    light = b.clone('GlobalLight', name='Global Light 2D',
                    fields={LIGHT_COMPONENT: [
                        ('m_Color', col(cfg['light_color']))]})

    # slingshot
    sling = b.clone('Slingshot', name='Slingshot',
                    position=(cfg['slingshot_x'], -0.43, 0))
    pivot = b.clone('RotatingPivot', name='RotatingPivot', position=(-2.13, 0.82, 0),
                    parent=sling[1])
    launch = b.clone('LaunchPoint', name='LaunchPoint', position=(-2.17, 1.5600002, 0),
                     parent=pivot[1])
    b.clone('Metatel', name='Metatel', position=(-1.2336597, 0.8951699, 0),
            parent=pivot[1])
    b.clone('CatapultPart', name='KusokKatapulti2_0',
            position=(-2.5331597, -0.49013, 0), parent=sling[1])
    b.clone('AimTrajectory', name='AimTrajectory', position=(-4.8742, 0.99307, 0))

    # targets
    area = b.clone('TargetArea', name='TargetArea',
                   position=(cfg['target_area'][0], cfg['target_area'][1], 0))
    for block in cfg['blocks']:
        b.add_block(block['name'],
                    (block['pos'][0] - cfg['target_area'][0],
                     block['pos'][1] - cfg['target_area'][1]),
                    area[1], scale=block.get('scale', 0.6391),
                    mass=block.get('mass', 2), color=block.get('color', (1, 1, 1, 1)),
                    hits=block.get('hits'))

    for slab in cfg['slabs']:
        b.add_static_slab(slab['name'], slab['center'], slab['size'],
                          slab['color'], slab.get('order', 0))

    # camera (references ground, background, pivot and the slingshot controller)
    cam_x = (cfg['map_min'][0] + cfg['map_max'][0]) * 0.5
    cam_y = (cfg['map_min'][1] + cfg['map_max'][1]) * 0.5
    b.clone('MainCamera', name='Main Camera', position=(cam_x, cam_y, -10),
            fields={CAMERA_CONTROLLER: [
                ('mapMin', vec((cfg['map_min'][0], cfg['map_min'][1], 0))),
                ('mapMax', vec((cfg['map_max'][0], cfg['map_max'][1], 0))),
                ('impactSize', str(cfg['impact_size'])),
            ]})

    # manager with the level id baked into the session component
    b.clone('GameManager', name='GameManager', position=(0, 0, 0),
            fields={3: [('forcedLevelId', str(cfg['level_id']))]})
    b.clone('AudioManager', name='AudioManager')
    b.clone('EventSystem', name='EventSystem')

    return apply_victory_sounds(b.finalize())


# ------------------------------------------------------------------ validation ----
def validate(text):
    errors = []
    parts = re.split(r'^--- !u!(\d+) &(\d+)\n', text, flags=re.M)
    docs = {}
    for i in range(1, len(parts), 3):
        fid = int(parts[i + 1])
        if fid in docs:
            errors.append('duplicate id %d' % fid)
        docs[fid] = (int(parts[i]), parts[i + 2])

    for fid, (cls, body) in docs.items():
        for m in re.finditer(r'\{fileID: (\d+)([^}]*)\}', body):
            ref = int(m.group(1))
            if 'guid:' in m.group(2):
                continue
            if ref != 0 and ref not in docs:
                errors.append('doc %d (%d) references missing %d' % (fid, cls, ref))
        if cls == GO:
            comps = [int(x) for x in re.findall(r'- component: \{fileID: (\d+)\}', body)]
            for c in comps:
                if c not in docs:
                    errors.append('gameobject %d has missing component %d' % (fid, c))
                elif 'm_GameObject: {fileID: %d}' % fid not in docs[c][1]:
                    errors.append('component %d does not point back to %d' % (c, fid))
        if cls == TR:
            father = int(re.search(r'm_Father: \{fileID: (\d+)\}', body).group(1))
            kids = [int(x) for x in re.findall(
                r'- \{fileID: (\d+)\}',
                body.split('m_Children:')[1].split('m_Father')[0])]
            for k in kids:
                if k not in docs or docs[k][0] != TR:
                    errors.append('transform %d child %d missing' % (fid, k))
                elif 'm_Father: {fileID: %d}' % fid not in docs[k][1]:
                    errors.append('child %d does not point to father %d' % (k, fid))
            if father != 0:
                if father not in docs:
                    errors.append('transform %d father %d missing' % (fid, father))
                else:
                    fkids = [int(x) for x in re.findall(
                        r'- \{fileID: (\d+)\}',
                        docs[father][1].split('m_Children:')[1].split('m_Father')[0])]
                    if fid not in fkids:
                        errors.append('father %d does not list child %d' % (father, fid))

    roots_doc = docs.get(9223372036854775807)
    if roots_doc is None:
        errors.append('missing SceneRoots')
    else:
        roots = [int(x) for x in re.findall(r'- \{fileID: (\d+)\}', roots_doc[1])]
        expected = [fid for fid, (cls, body) in docs.items()
                    if cls == TR and 'm_Father: {fileID: 0}' in body]
        if sorted(roots) != sorted(expected):
            errors.append('SceneRoots mismatch')
    return errors


def main():
    for cfg in (LEVEL2, LEVEL3, LEVEL4, LEVEL5):
        text = build(cfg)
        problems = validate(text)
        if problems:
            print('FAILED', cfg['file'])
            for p in problems[:20]:
                print('  ', p)
            raise SystemExit(1)
        path = os.path.join(ROOT, cfg['file'])
        with open(path, 'w', encoding='utf-8', newline='\n') as handle:
            handle.write(text)
        print('wrote', cfg['file'], len(text), 'bytes')


if __name__ == '__main__':
    main()

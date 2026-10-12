"""Transfer only UI appearance and idle-animation components onto incoming team scenes.

Used for scene merge resolution without opening Unity or running any checks/gameplay.
All navigation, events, layout, physics, level objects and gameplay settings are retained.
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
IMAGE = 'fe87c0e1cc204ed48ad3b37840f39efc'
BUTTON = '4e29b1a8efbd4b44bb3f3716e73f07ff'
SLIDER = '67db9e8f0e2ae9c40bc1e2b64352a6b4'
TEXT = 'f4688fdb7df04437aeb418b961361dc5'
FEEDBACK = '90524ff660ca43be81f3fab87e1e92ef'
IDLE = 'f25049c3b2ca343408198ade5a447d40'
FONT = 'f22f3daa37ece474b8824c3ce1c73932'
MATERIAL_ID = '-8719701163922677924'
SPRITES = ['9df1ff043d315264497d600cae0da5d6', '34cf7d371ffffa24383a3cb4026381dd',
           'cc2da07aef5d3b74984717a8fe234f0b', '0641dede7ceccb8478162d8b28ee8ea1']
PANEL = '0b97c8091329b6a48be2f7a85cf33d3c'
WHITE = (244, 235, 221, 255)
DARK = (34, 27, 49, 255)

def color(values):
    return '{' + ', '.join(k + ': ' + format(v / 255, '.9g') for k, v in zip('rgba', values)) + '}'

def author(path):
    data = path.read_text(encoding='utf-8-sig')
    prefix = data.split('--- !u!', 1)[0]
    docs, kinds, order = {}, {}, []
    for match in re.finditer(r'(?m)^--- !u!(\d+) &(-?\d+)[^\n]*\n[\s\S]*?(?=^--- !u!|\Z)', data):
        docs[match[2]] = match[0]
        kinds[match[2]] = int(match[1])
        order.append(match[2])
    gos, go_of, script_of, rect_go, parent = {}, {}, {}, {}, {}
    for key, doc in docs.items():
        go = re.search(r'  m_GameObject: {fileID: (-?\d+)', doc)
        if go: go_of[key] = go[1]
        script = re.search(r'  m_Script:.*guid: (\w+)', doc)
        if script: script_of[key] = script[1]
        name = re.search(r'  m_Name: (.*)', doc)
        if kinds[key] == 1 and name: gos[key] = name[1]
        if kinds[key] in (4, 224) and go:
            rect_go[key] = go[1]
    for key, doc in docs.items():
        if key in rect_go:
            father = re.search(r'  m_Father: {fileID: (-?\d+)', doc)
            parent[rect_go[key]] = rect_go.get(father[1]) if father else None

    def set_field(key, name, value, indent=2):
        line = ' ' * indent + name + ': ' + value
        pattern = r'(?m)^' + ' ' * indent + re.escape(name) + r':[^\n]*$'
        if re.search(pattern, docs[key]): docs[key] = re.sub(pattern, lambda m: line, docs[key])
        else: docs[key] = docs[key].rstrip() + '\n' + line + '\n'

    def set_plate(key, guid, multiplier=1):
        set_field(key, 'm_Sprite', '{fileID: 21300000, guid: ' + guid + ', type: 3}')
        set_field(key, 'm_Type', '1')
        set_field(key, 'm_Color', '{r: 1, g: 1, b: 1, a: 1}')
        set_field(key, 'm_PixelsPerUnitMultiplier', str(multiplier))

    next_id = 8505000000
    def add_component(go, guid, class_name, fields):
        nonlocal next_id
        if go not in docs or '  m_Component:\n' not in docs[go]: return
        next_id += 1
        while str(next_id) in docs: next_id += 1
        key = str(next_id)
        docs[go] = re.sub(r'(?m)^  m_Layer:', '  - component: {fileID: ' + key + '}\n  m_Layer:', docs[go], count=1)
        docs[key] = f'''--- !u!114 &{key}
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {go}}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {guid}, type: 3}}
  m_Name:
  m_EditorClassIdentifier: Assembly-CSharp::{class_name}
{fields}'''
        go_of[key], script_of[key] = go, guid
        kinds[key] = 114
        order.append(key)
        return key

    roles = {}
    for key, guid in list(script_of.items()):
        if guid != BUTTON: continue
        go = go_of[key]
        name = gos.get(go, '')
        if name.startswith('Dot'): continue
        text_children = []
        for text_id, text_guid in script_of.items():
            if text_guid != TEXT: continue
            ancestor = go_of.get(text_id)
            while ancestor and ancestor != go: ancestor = parent.get(ancestor)
            if ancestor == go: text_children.append(text_id)
        primary = name in ('PlayButton', 'ResumeButton', 'ContinueButton', 'StartButton', 'NextLevelButton')
        role = 3 if not text_children else 0 if primary else 1
        roles[go] = role
        target = re.search(r'  m_TargetGraphic: {fileID: (-?\d+)', docs[key])
        if target and target[1] in docs: set_plate(target[1], SPRITES[role])
        set_field(key, 'm_Transition', '1')
        for field in ('m_NormalColor', 'm_HighlightedColor', 'm_PressedColor', 'm_SelectedColor'):
            set_field(key, field, '{r: 1, g: 1, b: 1, a: 1}', 4)
        set_field(key, 'm_DisabledColor', '{r: 0.6117647, g: 0.5921569, b: 0.654902, a: 0.627451}', 4)
        feedback = next((i for i, g in script_of.items() if g == FEEDBACK and go_of.get(i) == go), None)
        if feedback is None:
            feedback = add_component(go, FEEDBACK, 'MenuControlFeedback',
                '  pressCompression: 1\n  pressAmount: 0.02\n  pressReturnDuration: 0.1\n  sharedFocusColor: 1\n  idleSway: 0\n  swayAngle: 0.45\n  swayDistance: 0.7\n  swayFrequency: 0.18\n')
        if feedback:
            set_field(feedback, 'plate', str(role))
            set_field(feedback, 'sharedFocusColor', '1')

    for key, guid in list(script_of.items()):
        go = go_of.get(key)
        name = gos.get(go, '')
        if guid == TEXT:
            ancestor, role = go, None
            while ancestor:
                if ancestor in roles: role = roles[ancestor]; break
                ancestor = parent.get(ancestor)
            text_color = DARK if role in (0, 2, 3) else WHITE
            set_field(key, 'm_fontAsset', '{fileID: 11400000, guid: ' + FONT + ', type: 2}')
            set_field(key, 'm_sharedMaterial', '{fileID: ' + MATERIAL_ID + ', guid: ' + FONT + ', type: 2}')
            set_field(key, 'm_fontStyle', '0')
            set_field(key, 'm_fontColor', color(text_color))
            rgba = int.from_bytes(bytes(text_color), 'little')
            docs[key] = re.sub(r'(  m_fontColor32:\n    serializedVersion: \d+\n    rgba:) \d+', lambda m: m[1] + ' ' + str(rgba), docs[key])
        elif guid == IMAGE:
            if name in ('MainButtonsPanel', 'Content', 'DefeatPanel', 'ScorePanel', 'PreviewFrame', 'GuidePanel'):
                set_plate(key, PANEL)
            if name == 'Handle': set_plate(key, SPRITES[3], 2)
            if name == 'Fill': set_field(key, 'm_Color', color((166, 140, 207, 255)))
        elif guid == SLIDER:
            for field in ('m_HighlightedColor', 'm_PressedColor', 'm_SelectedColor'):
                set_field(key, field, '{r: 1, g: 1, b: 1, a: 1}', 4)
            for image_id, image_guid in script_of.items():
                image_go = go_of.get(image_id)
                if image_guid == IMAGE and gos.get(image_go) == 'Background' and parent.get(image_go) == go:
                    set_plate(image_id, SPRITES[1])
        elif guid in ('eabcce4b483e4332af8256b50980f867', 'ba615b026b31d654a9eabdf7fcd72e3c'):
            if not any(g == IDLE and go_of.get(i) == go for i, g in script_of.items()):
                add_component(go, IDLE, 'IdleSpriteAnimation',
                    '  frames: []\n  framesPerSecond: 6\n  pingPong: 0\n  variedStart: 1\n  onlyWhenStill: 1\n  stillSpeed: 0.2\n  stillAngularSpeed: 8\n')
    path.write_text(prefix + ''.join(docs[key] for key in order), encoding='utf-8', newline='\n')
    print('Applied UI appearance to incoming scene: ' + path.name)

if __name__ == '__main__':
    for name in sys.argv[1:]: author(ROOT / name)

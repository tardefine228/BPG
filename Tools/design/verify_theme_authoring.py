"""Compare the scene/prefab authoring result with the source before copying it back."""
import re
import sys
import difflib
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
GAME_SCRIPTS = [
    'Gun/SlingshotController', 'LevelScripts/LevelSession', 'Enemies/MouseEnemy',
    'LevelScripts/LevelTarget', 'Cameras/ForestAtmosphere', 'Cameras/GameplayCameraController',
    'ProjectileS/CatProjectile', 'ProjectileS/CatFlightEffects', 'ProjectileS/ProjectileComicEffects',
    'Abilities/DashCatAbility', 'Abilities/MultiplyCatAbility', 'Abilities/BlastCatAbility',
    'Abilities/HeavyCatAbility', 'Abilities/DiveCatAbility',
]

def documents(path, from_head=False):
    result = {}
    if from_head:
        git_result = subprocess.run(['git', 'show', 'HEAD:' + path.relative_to(ROOT).as_posix()], cwd=ROOT, capture_output=True)
        # The user's debug scene is local/ignored; preserve its current physical state.
        content = git_result.stdout.decode('utf-8-sig') if git_result.returncode == 0 else path.read_text(encoding='utf-8-sig')
    else:
        content = path.read_text(encoding='utf-8-sig')
    for match in re.finditer(r'(?m)^--- !u!(\d+) &(-?\d+)[^\n]*\n([\s\S]*?)(?=^--- !u!|\Z)', content):
        result[match[2]] = (int(match[1]), match[3].strip())
    return result

def main():
    preview = Path(sys.argv[1])
    restore = '--restore-protected' in sys.argv
    from_head = '--from-head' in sys.argv
    guids = set()
    for script in GAME_SCRIPTS:
        meta = ROOT / ('Assets/Scripts/' + script + '.cs.meta')
        if meta.exists():
            guids.add(re.search(r'guid: (\w+)', meta.read_text())[1])
    paths = list((ROOT / 'Assets/Scenes').glob('*.unity'))
    paths += list((ROOT / 'Assets/Prefabs/Cats').glob('Cat?.prefab'))
    paths += list((ROOT / 'Assets/Prefabs/Mouses').glob('*.prefab'))
    checked = 0
    for source in paths:
        target = preview / source.relative_to(ROOT)
        old, new = documents(source, from_head), documents(target)
        assert len(new) > 0
        if restore:
            content = target.read_text(encoding='utf-8-sig')
            for doc_id, (kind, body) in old.items():
                script = re.search(r'm_Script:.*guid: (\w+)', body)
                protected = kind in (4, 50, 58, 60, 61, 212) or (script and script[1] in guids)
                if protected and doc_id in new:
                    # Unity may round camera/physics floats and materialize implicit defaults.
                    # Reuse the original gameplay block exactly; all authoring here concerns UI.
                    pattern = r'(?m)(^--- !u!\d+ &' + doc_id + r'[^\n]*\n)[\s\S]*?(?=^--- !u!|\Z)'
                    content = re.sub(pattern, lambda m: m[1] + body + '\n', content)
            target.write_text(content, encoding='utf-8', newline='\n')
            new = documents(target)
        for doc_id, (kind, body) in old.items():
            script = re.search(r'm_Script:.*guid: (\w+)', body)
            # Physical transforms/renderers/colliders and gameplay settings must be byte-equivalent.
            protected = kind in (4, 50, 58, 60, 61, 212) or (script and script[1] in guids)
            if not protected:
                continue
            assert doc_id in new, (str(source), 'removed gameplay object', doc_id)
            candidate = new[doc_id][1]
            if (new[doc_id][0], candidate) != (kind, body):
                delta = '\n'.join(difflib.unified_diff(body.splitlines(), candidate.splitlines(), n=2))
                raise AssertionError(str(source) + ', object ' + doc_id + '\n' + delta)
            checked += 1
    print(f'PASS: {checked} physical/gameplay documents unchanged across {len(paths)} scenes and prefabs.')

if __name__ == '__main__':
    main()

"""Static scene reference and score HUD checks; no Unity installation required.

Run from the repository root:
    python3 -m unittest discover -s Tools/validation -p 'test_*.py' -v

These checks validate saved objects and references, not runtime rendering.
"""

import re
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
DOCUMENT_HEADER = re.compile(r"^--- !u!(\d+) &(-?\d+)(?: stripped)?\n", re.MULTILINE)


def read_documents(path):
    text = path.read_text(encoding="utf-8")
    headers = list(DOCUMENT_HEADER.finditer(text))
    documents = {}
    for index, header in enumerate(headers):
        file_id = int(header[2])
        if file_id in documents:
            raise AssertionError(f"{path.name}: duplicate fileID {file_id}")
        end = headers[index + 1].start() if index + 1 < len(headers) else len(text)
        documents[file_id] = (int(header[1]), text[header.end():end])
    return documents


def field(body, name):
    match = re.search(r"^  " + re.escape(name) + r":([^\n]*)$", body, re.MULTILINE)
    if match is None:
        raise AssertionError(f"Missing serialized field: {name}")
    return match[1].strip()


def reference(body, name):
    match = re.fullmatch(r"\{fileID: (-?\d+)\}", field(body, name))
    if match is None:
        raise AssertionError(f"{name}: expected a local scene reference")
    return int(match[1])


class ScoreHudTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        settings = (ROOT / "ProjectSettings/EditorBuildSettings.asset").read_text(encoding="utf-8")
        cls.scenes = {}
        for enabled, path in re.findall(r"  - enabled: ([01])\n    path: ([^\n]+)", settings):
            if enabled != "1":
                continue
            documents = read_documents(ROOT / path)
            sessions = [body for kind, body in documents.values()
                        if kind == 114 and "  m_EditorClassIdentifier: Assembly-CSharp::LevelSession\n" in body]
            if sessions:
                cls.scenes[Path(path).stem] = (documents, sessions)

    def game_object(self, documents, name):
        matches = [file_id for file_id, (kind, body) in documents.items()
                   if kind == 1 and re.search(r"^  m_Name: " + re.escape(name) + r"$", body, re.MULTILINE)]
        self.assertEqual(len(matches), 1, f"Expected one {name} GameObject")
        return matches[0]

    def component(self, documents, game_object, kind):
        ids = [int(value) for value in re.findall(
            r"- component: \{fileID: (-?\d+)\}", documents[game_object][1])]
        matches = [file_id for file_id in ids
                   if file_id in documents and documents[file_id][0] == kind]
        self.assertEqual(len(matches), 1, f"GameObject {game_object}: expected one component of class {kind}")
        self.assertEqual(reference(documents[matches[0]][1], "m_GameObject"), game_object)
        return matches[0]

    def active_ancestors(self, documents, game_object):
        ancestors = []
        transform = self.component(documents, game_object, 224)
        while transform:
            self.assertNotIn(transform, ancestors, "Cycle in HUD hierarchy")
            self.assertIn(transform, documents, "Missing parent transform")
            ancestors.append(transform)
            body = documents[transform][1]
            owner = reference(body, "m_GameObject")
            self.assertIn(owner, documents, "Missing parent GameObject")
            self.assertEqual(field(documents[owner][1], "m_IsActive"), "1", "Inactive HUD ancestor")
            parent = reference(body, "m_Father")
            if parent:
                self.assertIn(parent, documents, "Missing parent transform")
                children = documents[parent][1].split("  m_Children:", 1)[1].split("  m_Father:", 1)[0]
                self.assertIn(f"- {{fileID: {transform}}}", children, "Parent does not list its HUD child")
            transform = parent
        return ancestors

    def test_score_canvas_is_present_and_enabled_in_all_levels(self):
        self.assertTrue({"Gameplay", "Level2", "Level3", "Level4", "Level5"}.issubset(self.scenes),
                        "Missing gameplay scene in build settings")
        for name, (documents, _) in self.scenes.items():
            with self.subTest(scene=name):
                owner = self.game_object(documents, "LevelScoreCanvas")
                canvas = documents[self.component(documents, owner, 223)][1]
                self.assertEqual(field(canvas, "m_Enabled"), "1")
                self.assertEqual(field(canvas, "m_RenderMode"), "0", "Score HUD must use Screen Space Overlay")
                self.active_ancestors(documents, owner)

    def test_score_text_bindings_point_to_active_hud_text(self):
        for name, (documents, sessions) in self.scenes.items():
            with self.subTest(scene=name):
                self.assertEqual(len(sessions), 1)
                owner = self.game_object(documents, "LevelScoreCanvas")
                canvas_transform = self.component(documents, owner, 224)
                for binding, object_name in (("scoreValueText", "ScoreValue"), ("scoreHintText", "ScoreHint")):
                    with self.subTest(binding=binding):
                        text_id = reference(sessions[0], binding)
                        self.assertNotEqual(text_id, 0, f"{binding} is unassigned")
                        self.assertIn(text_id, documents, f"{binding} references a missing component")
                        kind, text = documents[text_id]
                        self.assertEqual(kind, 114)
                        self.assertEqual(field(text, "m_EditorClassIdentifier"),
                                         "Unity.TextMeshPro::TMPro.TextMeshProUGUI")
                        self.assertEqual(field(text, "m_Enabled"), "1")
                        text_owner = self.game_object(documents, object_name)
                        self.assertEqual(reference(text, "m_GameObject"), text_owner)
                        self.assertIn(f"- component: {{fileID: {text_id}}}", documents[text_owner][1])
                        self.assertIn(canvas_transform, self.active_ancestors(documents, text_owner))
                title = self.game_object(documents, "ScoreTitle")
                self.assertIn(canvas_transform, self.active_ancestors(documents, title))


class SceneReferenceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        settings = (ROOT / "ProjectSettings/EditorBuildSettings.asset").read_text(encoding="utf-8")
        cls.scenes = {Path(path).stem: read_documents(ROOT / path)
                      for enabled, path in re.findall(r"  - enabled: ([01])\n    path: ([^\n]+)", settings)
                      if enabled == "1"}
        cls.atmosphere_assets = {}
        for name, path, file_id, asset_type in (
            ("m_Script", "Assets/Scripts/Cameras/ForestAtmosphere.cs", 11500000, 3),
            ("atmosphereMaterial", "Assets/Materials/ForestAtmosphere.mat", 2100000, 2),
        ):
            meta = (ROOT / (path + ".meta")).read_text(encoding="utf-8")
            guid = re.search(r"^guid: (\w+)$", meta, re.MULTILINE)[1]
            cls.atmosphere_assets[name] = f"{{fileID: {file_id}, guid: {guid}, type: {asset_type}}}"

    def test_all_local_references_resolve_in_enabled_scenes(self):
        self.assertTrue(self.scenes, "No enabled scenes to validate")
        for name, documents in self.scenes.items():
            with self.subTest(scene=name):
                for owner, (_, body) in documents.items():
                    for match in re.finditer(r"\{fileID: (-?\d+)([^}]*)\}", body):
                        target = int(match[1])
                        if target == 0 or "guid:" in match[2]:
                            continue  # Null or external asset reference, not a local scene object.
                        self.assertTrue(target in documents,
                                        f"{name}: document {owner} references missing local fileID {target}")

    def test_gameplay_atmosphere_is_attached_to_main_camera(self):
        for name, documents in self.scenes.items():
            if not any(kind == 114 and "  m_EditorClassIdentifier: Assembly-CSharp::LevelSession\n" in body
                       for kind, body in documents.values()):
                continue
            with self.subTest(scene=name):
                cameras = [file_id for file_id, (kind, body) in documents.items()
                           if kind == 1 and re.search(r"^  m_Name: Main Camera$", body, re.MULTILINE)]
                self.assertEqual(len(cameras), 1, "Expected one Main Camera GameObject")
                atmospheres = [(file_id, body) for file_id, (kind, body) in documents.items()
                               if kind == 114 and "  m_EditorClassIdentifier: Assembly-CSharp::ForestAtmosphere\n" in body]
                self.assertEqual(len(atmospheres), 1, "Expected one ForestAtmosphere component")
                component_id, atmosphere = atmospheres[0]
                self.assertEqual(reference(atmosphere, "m_GameObject"), cameras[0])
                self.assertIn(f"- component: {{fileID: {component_id}}}", documents[cameras[0]][1],
                              "Main Camera must list its ForestAtmosphere component")
                for asset, expected_reference in self.atmosphere_assets.items():
                    self.assertEqual(field(atmosphere, asset), expected_reference,
                                     f"{name}: incorrect atmosphere asset reference {asset}")


if __name__ == "__main__":
    unittest.main()

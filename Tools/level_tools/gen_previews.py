#!/usr/bin/env python3
"""
Renders the small schematic preview pictures shown on the level cards of the
lobby (Assets/Resources/LevelPreviews/level_N.png).

The layout data is imported from gen_levels.py, so the pictures always match
the generated scenes. Written with a tiny built-in PNG encoder: no image
library is required.
"""
import struct
import sys
import zlib
import os

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

# The repository root, computed from this file (Tools/level_tools/<script>).
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from gen_levels import GROUND_TOP, half_h, LEVEL2, LEVEL3, LEVEL4, LEVEL5  # noqa: E402

WIDTH, HEIGHT = 640, 360
PROJECTILE_COLOR = (1.0, 0.14, 0.10, 1)
SLING_COLOR = (0.30, 0.20, 0.15, 1)


def to255(color, factor=1.0):
    return tuple(max(0, min(255, int(round(channel * factor * 255)))) for channel in color[:3])


def mix(color, other, amount):
    return tuple(color[i] + (other[i] - color[i]) * amount for i in range(4))


def write_png(path, rows):
    raw = b''.join(b'\x00' + bytes(row) for row in rows)

    def chunk(tag, data):
        return (struct.pack('>I', len(data)) + tag + data
                + struct.pack('>I', zlib.crc32(tag + data) & 0xffffffff))

    header = b'\x89PNG\r\n\x1a\n'
    ihdr = struct.pack('>IIBBBBB', WIDTH, HEIGHT, 8, 2, 0, 0, 0)
    with open(path, 'wb') as handle:
        handle.write(header)
        handle.write(chunk(b'IHDR', ihdr))
        handle.write(chunk(b'IDAT', zlib.compress(raw, 9)))
        handle.write(chunk(b'IEND', b''))


class Canvas:
    """Maps world coordinates to pixels with one uniform scale on both axes."""

    def __init__(self, x0, y0, scale):
        self.x0, self.y0, self.scale = x0, y0, scale
        self.pixels = [[(0, 0, 0)] * WIDTH for _ in range(HEIGHT)]

    @property
    def y1(self):
        return self.y0 + HEIGHT / self.scale

    @property
    def x1(self):
        return self.x0 + WIDTH / self.scale

    def px(self, world_x):
        return (world_x - self.x0) * self.scale

    def py(self, world_y):
        return (self.y1 - world_y) * self.scale

    def units_per_pixel(self):
        return 1.0 / self.scale

    def fill(self, x_a, x_b, y_a, y_b, color):
        """Fill the world-space rectangle [x_a, x_b] x [y_a, y_b]."""
        left, right = sorted((self.px(x_a), self.px(x_b)))
        top, bottom = sorted((self.py(y_a), self.py(y_b)))
        for y in range(max(0, int(top)), min(HEIGHT - 1, int(bottom)) + 1):
            row = self.pixels[y]
            for x in range(max(0, int(left)), min(WIDTH - 1, int(right)) + 1):
                row[x] = color

    def rows(self):
        flattened = []
        for row in self.pixels:
            for pixel in row:
                flattened.extend(pixel)
        return [flattened[y * WIDTH * 3:(y + 1) * WIDTH * 3] for y in range(HEIGHT)]


def content_box(cfg):
    """World-space box that contains every structure of the level."""
    left = min(block['pos'][0] - half_h(block.get('scale', 0.6391)) for block in cfg['blocks'])
    right = max(block['pos'][0] + half_h(block.get('scale', 0.6391)) for block in cfg['blocks'])
    top = max(block['pos'][1] + half_h(block.get('scale', 0.6391)) for block in cfg['blocks'])
    for slab in cfg['slabs']:
        cx, cy = slab['center']
        sx, sy = slab['size']
        left = min(left, cx - sx / 2)
        right = max(right, cx + sx / 2)
        top = max(top, cy + sy / 2)
    return left, right, top


def render(cfg, path):
    sky = cfg['background_color']
    ground = cfg['ground_color']

    left, right, top = content_box(cfg)
    margin = 3.2
    scale = WIDTH / max(1.0, (right - left) + margin * 2.0)
    frame_height = HEIGHT / scale
    above_ground = min(frame_height - 1.2,
                       max(top - GROUND_TOP + margin * 0.6, frame_height * 0.34))
    y1 = GROUND_TOP + above_ground
    y0 = y1 - frame_height
    canvas = Canvas(left - margin, y0, scale)

    # Sky with a soft vertical gradient.
    horizon = canvas.py(GROUND_TOP)
    for y in range(HEIGHT):
        amount = 0.0 if y <= horizon else (y - horizon) / max(1.0, HEIGHT - horizon)
        color = to255(sky, 1.0 - 0.20 * amount)
        for x in range(WIDTH):
            canvas.pixels[y][x] = color

    # Ground with a darker top edge.
    canvas.fill(canvas.x0, canvas.x1, canvas.y0, GROUND_TOP, to255(ground))
    canvas.fill(canvas.x0, canvas.x1, GROUND_TOP - 0.10, GROUND_TOP,
                to255(mix(ground, (0, 0, 0, 1), 0.35)))

    # Static slabs.
    for slab in cfg['slabs']:
        cx, cy = slab['center']
        sx, sy = slab['size']
        canvas.fill(cx - sx / 2, cx + sx / 2, cy - sy / 2, cy + sy / 2, to255(slab['color']))
        canvas.fill(cx - sx / 2, cx + sx / 2, cy + sy / 2 - 0.15, cy + sy / 2,
                    to255(mix(slab['color'], (1, 1, 1, 1), 0.28)))

    # Battle boxes.
    for block in cfg['blocks']:
        cx, cy = block['pos']
        half = half_h(block.get('scale', 0.6391))
        color = block.get('color', (1, 1, 1, 1))
        canvas.fill(cx - half, cx + half, cy - half, cy + half, to255(color))
        border = 0.07
        canvas.fill(cx - half, cx + half, cy - half, cy - half + border,
                    to255(mix(color, (0, 0, 0, 1), 0.45)))
        canvas.fill(cx - half, cx + half, cy + half - border, cy + half,
                    to255(mix(color, (1, 1, 1, 1), 0.35)))
        canvas.fill(cx - half, cx - half + border, cy - half, cy + half,
                    to255(mix(color, (0, 0, 0, 1), 0.35)))
        canvas.fill(cx + half - border, cx + half, cy - half, cy + half,
                    to255(mix(color, (0, 0, 0, 1), 0.35)))
        if block.get('hits'):
            # Reinforced block: inner plate plus a "durability" marker.
            inset = half * 0.52
            canvas.fill(cx - inset, cx + inset, cy - inset, cy + inset,
                        to255(mix(color, (1, 1, 1, 1), 0.22)))
            dot = 0.17
            canvas.fill(cx - dot, cx + dot, cy - dot, cy + dot, to255((0.90, 0.61, 0.32, 1)))

    # Dashed "shot comes from here" hint at the left edge of the frame.
    first_target_x = min(block['pos'][0] for block in cfg['blocks'])
    hint_y = GROUND_TOP + 2.4
    step = (first_target_x - (canvas.x0 + 1.0)) / 7.0
    for i in range(6):
        dash_x = canvas.x0 + 1.0 + step * i
        canvas.fill(dash_x, dash_x + step * 0.55, hint_y, hint_y + 0.55,
                    to255(PROJECTILE_COLOR))

    # Slingshot: base, post and the shell on top (only when it is in frame).
    sx = cfg['slingshot_x']
    canvas.fill(sx - 0.55, sx + 0.55, GROUND_TOP, GROUND_TOP + 3.1, to255(SLING_COLOR))
    canvas.fill(sx - 1.1, sx + 1.1, GROUND_TOP, GROUND_TOP + 0.5, to255(SLING_COLOR))
    canvas.fill(sx + 0.1, sx + 2.4, GROUND_TOP + 2.9, GROUND_TOP + 3.4, to255(SLING_COLOR))
    shell = 0.75
    canvas.fill(sx + 2.0 - shell, sx + 2.0 + shell, GROUND_TOP + 3.0 - shell,
                GROUND_TOP + 3.0 + shell, to255(PROJECTILE_COLOR))

    write_png(path, canvas.rows())
    return os.path.getsize(path)


def main():
    base = os.path.join(ROOT, 'Assets/Resources/LevelPreviews')
    for cfg in (LEVEL4, LEVEL5):
        level_id = cfg['level_id']
        path = os.path.join(base, 'level_%d.png' % level_id)
        size = render(cfg, path)
        print('wrote', path, size, 'bytes')


if __name__ == '__main__':
    main()

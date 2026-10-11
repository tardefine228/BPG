# -*- coding: utf-8 -*-
"""Мини-библиотека пиксель-арта без внешних зависимостей (только Python 3).

Холст RGBA, примитивы рисования, масштабирование «ближайший сосед» и запись PNG.
Используется генераторами спрайтов (gen_sprites.py) и превью уровней (gen_previews.py).
"""
import struct
import zlib

TRANSPARENT = (0, 0, 0, 0)


def rgba(c, a=255):
    return (c[0], c[1], c[2], c[3] if len(c) > 3 else a)


def clamp8(v):
    return 0 if v < 0 else 255 if v > 255 else int(round(v))


def shade(c, f):
    """Осветление (f > 1) или затемнение (f < 1) цвета."""
    return (clamp8(c[0] * f), clamp8(c[1] * f), clamp8(c[2] * f)) + tuple(c[3:4])


def mix(a, b, t):
    return tuple(clamp8(a[i] + (b[i] - a[i]) * t) for i in range(3)) + tuple(a[3:4])


class Canvas:
    def __init__(self, width, height, fill=TRANSPARENT):
        self.w = width
        self.h = height
        f = rgba(fill)
        self.px = [f] * (width * height)

    # Начало координат — левый верхний угол, как в PNG.
    def set(self, x, y, c):
        if 0 <= x < self.w and 0 <= y < self.h:
            c = rgba(c)
            if c[3] == 255:
                self.px[y * self.w + x] = c
            elif c[3] > 0:
                base = self.px[y * self.w + x]
                t = c[3] / 255.0
                out_a = c[3] + base[3] * (1 - t)
                if out_a <= 0:
                    return
                mixed = tuple(clamp8((c[i] * c[3] + base[i] * base[3] * (1 - t)) / out_a) for i in range(3))
                self.px[y * self.w + x] = mixed + (clamp8(out_a),)

    def get(self, x, y):
        if 0 <= x < self.w and 0 <= y < self.h:
            return self.px[y * self.w + x]
        return TRANSPARENT

    def rect(self, x0, y0, x1, y1, c):
        """Закрашенный прямоугольник, включая обе границы."""
        if x0 > x1:
            x0, x1 = x1, x0
        if y0 > y1:
            y0, y1 = y1, y0
        for y in range(max(0, y0), min(self.h - 1, y1) + 1):
            for x in range(max(0, x0), min(self.w - 1, x1) + 1):
                self.set(x, y, c)

    def frame(self, x0, y0, x1, y1, c):
        self.hline(x0, x1, y0, c)
        self.hline(x0, x1, y1, c)
        self.vline(x0, y0, y1, c)
        self.vline(x1, y0, y1, c)

    def hline(self, x0, x1, y, c):
        if x0 > x1:
            x0, x1 = x1, x0
        for x in range(x0, x1 + 1):
            self.set(x, y, c)

    def vline(self, x, y0, y1, c):
        if y0 > y1:
            y0, y1 = y1, y0
        for y in range(y0, y1 + 1):
            self.set(x, y, c)

    def line(self, x0, y0, x1, y1, c):
        dx = abs(x1 - x0)
        dy = -abs(y1 - y0)
        sx = 1 if x0 < x1 else -1
        sy = 1 if y0 < y1 else -1
        err = dx + dy
        while True:
            self.set(x0, y0, c)
            if x0 == x1 and y0 == y1:
                break
            e2 = 2 * err
            if e2 >= dy:
                err += dy
                x0 += sx
            if e2 <= dx:
                err += dx
                y0 += sy

    def ellipse(self, cx, cy, rx, ry, c):
        """Закрашенный эллипс (центр и радиусы могут быть дробными)."""
        for y in range(int(cy - ry) - 1, int(cy + ry) + 2):
            for x in range(int(cx - rx) - 1, int(cx + rx) + 2):
                dx = (x + 0.5 - cx) / max(rx, 0.01)
                dy = (y + 0.5 - cy) / max(ry, 0.01)
                if dx * dx + dy * dy <= 1.0:
                    self.set(x, y, c)

    def blit(self, src, x0, y0, flip=False):
        for y in range(src.h):
            for x in range(src.w):
                c = src.px[y * src.w + (src.w - 1 - x if flip else x)]
                if c[3]:
                    self.set(x0 + x, y0 + y, c)

    def outline(self, c, src_alpha_min=1):
        """Обводит непрозрачные пиксели контуром в 1 пиксель (по 4 соседям)."""
        out = [p for p in self.px]
        for y in range(self.h):
            for x in range(self.w):
                if self.px[y * self.w + x][3] >= src_alpha_min:
                    continue
                for nx, ny in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)):
                    if 0 <= nx < self.w and 0 <= ny < self.h and self.px[ny * self.w + nx][3] >= src_alpha_min:
                        out[y * self.w + x] = rgba(c)
                        break
        self.px = out

    def scaled(self, n):
        """Увеличение «ближайший сосед» в n раз."""
        out = Canvas(self.w * n, self.h * n)
        px = []
        for y in range(self.h):
            row = []
            for x in range(self.w):
                row.extend([self.px[y * self.w + x]] * n)
            for _ in range(n):
                px.extend(row)
        out.px = px
        return out

    def flipped_v(self):
        out = Canvas(self.w, self.h)
        for y in range(self.h):
            out.px[y * self.w:(y + 1) * self.w] = self.px[(self.h - 1 - y) * self.w:(self.h - y) * self.w]
        return out

    def crop_bounds(self):
        """Границы непрозрачной области: (x0, y0, x1, y1) включительно."""
        xs = [i % self.w for i, p in enumerate(self.px) if p[3]]
        ys = [i // self.w for i, p in enumerate(self.px) if p[3]]
        if not xs:
            return None
        return min(xs), min(ys), max(xs), max(ys)

    def save_png(self, path, alpha=True):
        write_png(path, self.w, self.h, self.px, alpha)


def write_png(path, width, height, pixels, alpha=True):
    channels = 4 if alpha else 3
    raw = bytearray()
    for y in range(height):
        raw.append(0)
        row = pixels[y * width:(y + 1) * width]
        for p in row:
            raw.extend(p[:channels])

    def chunk(tag, data):
        body = tag + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)

    color_type = 6 if alpha else 2
    png = b"\x89PNG\r\n\x1a\n"
    png += chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, color_type, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(bytes(raw), 9))
    png += chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(png)


def read_png(path):
    """Читает 8-битный PNG RGB/RGBA без чересстрочности (для проверок)."""
    with open(path, "rb") as f:
        data = f.read()
    assert data[:8] == b"\x89PNG\r\n\x1a\n", path
    pos = 8
    idat = b""
    width = height = color_type = None
    while pos < len(data):
        length = struct.unpack(">I", data[pos:pos + 4])[0]
        tag = data[pos + 4:pos + 8]
        body = data[pos + 8:pos + 8 + length]
        if tag == b"IHDR":
            width, height, depth, color_type = struct.unpack(">IIBB", body[:10])
            assert depth == 8
        elif tag == b"IDAT":
            idat += body
        pos += 12 + length
    raw = zlib.decompress(idat)
    ch = 4 if color_type == 6 else 3
    stride = width * ch
    rows = []
    prev = bytearray(stride)
    p = 0
    for _ in range(height):
        ft = raw[p]
        line = bytearray(raw[p + 1:p + 1 + stride])
        p += 1 + stride
        for i in range(stride):
            a = line[i - ch] if i >= ch else 0
            b = prev[i]
            c = prev[i - ch] if i >= ch else 0
            if ft == 1:
                line[i] = (line[i] + a) & 255
            elif ft == 2:
                line[i] = (line[i] + b) & 255
            elif ft == 3:
                line[i] = (line[i] + ((a + b) >> 1)) & 255
            elif ft == 4:
                pa, pb, pc = abs(b - c), abs(a - c), abs(a + b - 2 * c)
                pr = a if pa <= pb and pa <= pc else b if pb <= pc else c
                line[i] = (line[i] + pr) & 255
        rows.append(line)
        prev = line
    canvas = Canvas(width, height)
    px = []
    for line in rows:
        for x in range(width):
            q = line[x * ch:(x + 1) * ch]
            px.append((q[0], q[1], q[2], q[3] if ch == 4 else 255))
    canvas.px = px
    return canvas

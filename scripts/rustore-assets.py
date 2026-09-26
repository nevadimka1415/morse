#!/usr/bin/env python3
"""Иконка и скриншоты для карточки RuStore (docs/RUSTORE.md).

Запуск: python3 scripts/rustore-assets.py <папка снимков дымового теста> <папка для результата>

- icon-512.png — иконка 512×512 из тех же слоёв, что иконка телефона (Resources/AppIcon/appicon.svg — фон,
  appiconfg.svg — знак). Лаунчер Android показывает центральные 72 из 108 dp адаптивной иконки, поэтому берётся
  та же центральная часть — на витрине иконка выглядит так же, как на телефоне.
- 1-training.png … 6-settings.png — снимки эмулятора (1080×2400, русский проход android-smoke) без строки состояния
  и кнопок Android (правила RuStore), на холсте 9:16 с тем же градиентом, что у иконки.
Нужен Pillow.
"""
import re
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parent.parent
ICON_DIR = ROOT / "src/MorseTrainer.Mobile/Resources/AppIcon"
# Снимки для карточки в порядке показа (имена файлов android-smoke)
SHOTS = [
    ("android-ru-training.png", "1-training.png"),
    ("android-ru-course-book-koch.png", "2-course-book.png"),
    ("android-ru-learning.png", "3-learning.png"),
    ("android-ru-quiz-answered.png", "4-quiz.png"),
    ("android-ru-keyer.png", "5-keyer.png"),
    ("android-ru-settings.png", "6-settings.png"),
]
SHOT_SIZE = (1080, 2400)          # эмулятор pixel_6
STATUS_BAR, NAVIGATION_BAR = 128, 2337   # системные полосы pixel_6: строки, где меняется цвет
CANVAS = (1080, 1920)             # 9:16


def hex_rgb(value):
    return tuple(int(value[i:i + 2], 16) for i in (1, 3, 5))


def gradient_stops():
    svg = (ICON_DIR / "appicon.svg").read_text(encoding="utf-8")
    stops = [(float(offset), hex_rgb(color)) for offset, color in
             re.findall(r'<stop offset="([\d.]+)" stop-color="(#[0-9A-Fa-f]{6})"', svg)]
    if len(stops) < 2:
        raise SystemExit("appicon.svg: не найден градиент фона")
    return stops


def color_at(stops, t):
    t = min(max(t, 0.0), 1.0)
    for (t0, c0), (t1, c1) in zip(stops, stops[1:]):
        if t <= t1:
            f = 0 if t1 == t0 else (t - t0) / (t1 - t0)
            return tuple(round(a + (b - a) * f) for a, b in zip(c0, c1))
    return stops[-1][1]


def diagonal(size, stops, origin=0.0, span=None):
    """Градиент по диагонали (как linearGradient 0,0 → 1,1): t растёт с x + y."""
    width, height = size
    span = span or (width + height)
    row = [color_at(stops, (origin + i) / span) for i in range(width + height)]
    image = Image.new("RGB", size)
    pixels = image.load()
    for y in range(height):
        for x in range(width):
            pixels[x, y] = row[x + y]
    return image


def icon(stops, path, size=512, samples=4):
    svg = (ICON_DIR / "appiconfg.svg").read_text(encoding="utf-8")
    visible = 512 * 72 / 108                  # видимая часть адаптивной иконки в единицах SVG
    offset = (512 - visible) / 2
    scale = size * samples / visible
    big = size * samples
    # Фон: градиент всего квадрата 512, видна его центральная часть
    image = diagonal((big, big), stops, origin=2 * offset * scale, span=1024 * scale)
    draw = ImageDraw.Draw(image)

    def at(value):
        return (float(value) - offset) * scale

    for cx, cy, r, fill in re.findall(r'<circle cx="([\d.]+)" cy="([\d.]+)" r="([\d.]+)" fill="(#[0-9A-Fa-f]{6})"', svg):
        draw.ellipse([at(float(cx) - float(r)), at(float(cy) - float(r)), at(float(cx) + float(r)), at(float(cy) + float(r))],
                     fill=hex_rgb(fill))
    for x, y, w, h, rx, fill in re.findall(
            r'<rect x="([\d.]+)" y="([\d.]+)" width="([\d.]+)" height="([\d.]+)" rx="([\d.]+)" fill="(#[0-9A-Fa-f]{6})"', svg):
        draw.rounded_rectangle([at(x), at(y), at(float(x) + float(w)), at(float(y) + float(h))], radius=float(rx) * scale,
                               fill=hex_rgb(fill))
    image.resize((size, size), Image.LANCZOS).save(path, optimize=True)


def framed(shot_path, background, path):
    shot = Image.open(shot_path).convert("RGB")
    if shot.size != SHOT_SIZE:
        raise SystemExit(f"{shot_path.name}: {shot.size}, ожидается {SHOT_SIZE} (эмулятор pixel_6)")
    shot = shot.crop((0, STATUS_BAR, SHOT_SIZE[0], NAVIGATION_BAR))
    margin = 70
    height = CANVAS[1] - 2 * margin
    width = round(shot.width * height / shot.height)
    shot = shot.resize((width, height), Image.LANCZOS)
    canvas = background.copy()
    x0, y0 = (CANVAS[0] - width) // 2, margin
    shadow = Image.new("L", CANVAS, 0)
    ImageDraw.Draw(shadow).rounded_rectangle([x0, y0 + 12, x0 + width, y0 + height + 12], radius=44, fill=110)
    canvas.paste((0, 40, 40), (0, 0), shadow.filter(ImageFilter.GaussianBlur(24)))
    mask = Image.new("L", (width, height), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, width - 1, height - 1], radius=40, fill=255)
    canvas.paste(shot, (x0, y0), mask)
    canvas.save(path, optimize=True)


def main():
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)
    shots, out = Path(sys.argv[1]), Path(sys.argv[2])
    out.mkdir(parents=True, exist_ok=True)
    stops = gradient_stops()
    icon(stops, out / "icon-512.png")
    print(out / "icon-512.png")
    background = diagonal(CANVAS, stops)
    for source, target in SHOTS:
        framed(shots / source, background, out / target)
        print(out / target)


if __name__ == "__main__":
    main()

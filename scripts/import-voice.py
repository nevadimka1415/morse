#!/usr/bin/env python3
"""Встраивает записанные пользователем напевы как стандартный голос приложения.

Вход: папка с файлами code_XXXX.m4a/.mp3/.wav/.ogg/.opus (из «Свой голос…» → «Поделиться записями» на телефоне).
Для каждого файла:
  1. по огибающей громкости (окна 20 мс) находится сам напев: порог = max(шум + 12 дБ, пик − 35 дБ), участки голоса
     с паузами до 250 мс сливаются, короткие отдельные звуки по краям (щелчок кнопки, вдох) отбрасываются;
  2. вырезается напев с запасом 60 мс до и 120 мс после, края плавно затухают (без щелчков), гул ниже 80 Гц срезается;
  3. громкость выравнивается в два прохода loudnorm (линейно, без сжатия) до −20 LUFS, как у встроенного голоса.
Выход: src/MorseTrainer/Assets/Voice/code_XXXX.wav (Windows, PCM 16 бит 44,1 кГц моно) и
       src/MorseTrainer.Mobile/Resources/Raw/voice/code_XXXX.m4a (телефон, AAC 128 кбит/с 44,1 кГц моно).
Коды без записи остаются прежними. Запуск: scripts/import-voice.py <папка> [--dry-run]
"""
import argparse
import json
import math
import re
import statistics
import struct
import subprocess
import sys
from pathlib import Path

RATE = 16000
WINDOW = 0.02
MERGE_GAP = 0.25
MIN_RUN = 0.08
LEAD = 0.06
TAIL = 0.12
TARGET_LUFS = -20

ROOT = Path(__file__).resolve().parent.parent
WAV_DIR = ROOT / "src/MorseTrainer/Assets/Voice"
M4A_DIR = ROOT / "src/MorseTrainer.Mobile/Resources/Raw/voice"
SYMBOLS = {"01": "А", "1000": "Б", "011": "В", "110": "Г", "100": "Д", "0": "Е", "0001": "Ж", "1100": "З", "00": "И",
           "0111": "Й", "101": "К", "0100": "Л", "11": "М", "10": "Н", "111": "О", "0110": "П", "010": "Р", "000": "С",
           "1": "Т", "001": "У", "0010": "Ф", "0000": "Х", "1010": "Ц", "1110": "Ч", "1111": "Ш", "1101": "Щ",
           "11011": "Ъ", "1011": "Ы", "1001": "Ь", "00100": "Э", "0011": "Ю", "0101": "Я", "01111": "1", "00111": "2",
           "00011": "3", "00001": "4", "00000": "5", "10000": "6", "11000": "7", "11100": "8", "11110": "9", "11111": "0"}


def envelope(path):
    raw = subprocess.run(["ffmpeg", "-v", "error", "-i", str(path), "-ac", "1", "-ar", str(RATE), "-f", "s16le", "-"],
                         capture_output=True, check=True).stdout
    samples = struct.unpack(f"<{len(raw) // 2}h", raw)
    size = int(RATE * WINDOW)
    levels = []
    for start in range(0, len(samples) - size + 1, size):
        chunk = samples[start:start + size]
        rms = math.sqrt(sum(value * value for value in chunk) / size)
        levels.append(20 * math.log10(max(rms, 1) / 32768))
    return levels, len(samples) / RATE


def speech_span(levels, duration):
    """Начало и конец напева в секундах или None, если голоса не найдено."""
    peak = max(levels)
    floor = statistics.quantiles(levels, n=10)[0]
    threshold = max(floor + 12, peak - 35)
    runs = []
    for index, level in enumerate(levels):
        if level <= threshold:
            continue
        if runs and index - runs[-1][1] <= MERGE_GAP / WINDOW:
            runs[-1][1] = index + 1
        else:
            runs.append([index, index + 1])
    # Короткие одиночные звуки по краям (щелчок, вдох) — не напев
    while len(runs) > 1 and (runs[0][1] - runs[0][0]) * WINDOW < MIN_RUN:
        runs.pop(0)
    while len(runs) > 1 and (runs[-1][1] - runs[-1][0]) * WINDOW < MIN_RUN:
        runs.pop()
    if not runs:
        return None
    start = max(0.0, runs[0][0] * WINDOW - LEAD)
    end = min(duration, runs[-1][1] * WINDOW + TAIL)
    return start, end


def trim_filter(start, end):
    return (f"atrim=start={start:.3f}:end={end:.3f},asetpts=PTS-STARTPTS,afade=t=in:d=0.015,"
            f"areverse,afade=t=in:d=0.03,areverse,highpass=f=80")


def loudness_pass(source, base_filter):
    """Первый проход loudnorm: измерения для линейного второго прохода."""
    output = subprocess.run(["ffmpeg", "-hide_banner", "-i", str(source), "-af",
                             f"{base_filter},loudnorm=I={TARGET_LUFS}:TP=-1.5:LRA=7:print_format=json", "-f", "null", "-"],
                            capture_output=True, text=True, check=True).stderr
    return json.loads(output[output.rindex("{"):output.rindex("}") + 1])


def import_file(path, code, dry_run):
    levels, duration = envelope(path)
    span = speech_span(levels, duration)
    if span is None:
        return f"пропуск: {path.name} — голоса не найдено"
    start, end = span
    if dry_run:
        return f"будет встроен: {path.name:18} {SYMBOLS.get(code, '?'):2} запись {duration:5.2f} с → напев {start:4.2f}–{end:4.2f} с"
    base = trim_filter(start, end)
    measured = loudness_pass(path, base)
    loudnorm = (f"loudnorm=I={TARGET_LUFS}:TP=-1.5:LRA=7:measured_I={measured['input_i']}:measured_TP={measured['input_tp']}:"
                f"measured_LRA={measured['input_lra']}:measured_thresh={measured['input_thresh']}:"
                f"offset={measured['target_offset']}:linear=true")
    wav = WAV_DIR / f"code_{code}.wav"
    m4a = M4A_DIR / f"code_{code}.m4a"
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", str(path), "-af", f"{base},{loudnorm}", "-ac", "1", "-ar", "44100",
                    "-c:a", "pcm_s16le", str(wav)], check=True)
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", str(wav), "-ac", "1", "-ar", "44100", "-c:a", "aac", "-b:a", "128k",
                    "-movflags", "+faststart", str(m4a)], check=True)
    final = float(subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "default=nw=1:nk=1", str(wav)],
                                 capture_output=True, text=True, check=True).stdout)
    return (f"встроен: {path.name:18} {SYMBOLS.get(code, '?'):2} запись {duration:5.2f} с → напев {final:4.2f} с "
            f"(было {start:4.2f}–{end:4.2f} с)")


def main():
    parser = argparse.ArgumentParser(description="Встроить записи напевов code_XXXX как голос приложения")
    parser.add_argument("folder", type=Path)
    parser.add_argument("--dry-run", action="store_true", help="только показать, что будет встроено")
    args = parser.parse_args()
    done = set()
    for path in sorted(args.folder.iterdir()):
        match = re.fullmatch(r"code_([01]{1,7})\.(m4a|mp3|wav|ogg|opus)", path.name, re.IGNORECASE)
        if not match:
            continue
        code = match.group(1)
        if code in done:
            print(f"пропуск: {path.name} — второй файл для code_{code}")
            continue
        if not (WAV_DIR / f"code_{code}.wav").exists():
            print(f"пропуск: {path.name} — такого кода нет во встроенном голосе")
            continue
        done.add(code)
        print(import_file(path, code, args.dry_run))
    total = sorted(file.stem[5:] for file in WAV_DIR.glob("code_*.wav"))
    missing = [f"{SYMBOLS.get(code, '?')} (code_{code})" for code in total if code not in done]
    print(f"Встроено записей: {len(done)} из {len(total)}.")
    if missing:
        print("Без новой записи (остался прежний голос): " + ", ".join(missing))
    return 0


if __name__ == "__main__":
    sys.exit(main())

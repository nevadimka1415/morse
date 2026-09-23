#!/usr/bin/env bash
# Встраивает записанные пользователем напевы как стандартный голос приложения.
# Вход: папка с файлами code_XXXX.m4a/.mp3/.wav/.ogg/.opus (из «Свой голос…» → «Поделиться записями» на телефоне).
# Для каждого файла: обрезка тишины в начале и конце, срез гула ниже 80 Гц, выравнивание громкости, затем
#   src/MorseTrainer/Assets/Voice/code_XXXX.wav              — Windows (PCM 16 бит, 44,1 кГц, моно)
#   src/MorseTrainer.Mobile/Resources/Raw/voice/code_XXXX.m4a — телефон (AAC 128 кбит/с, 44,1 кГц, моно)
# Коды, для которых записи нет, остаются прежними. Запуск: scripts/import-voice.sh <папка> [--dry-run]
set -euo pipefail

source_dir="${1:?Укажите папку с записями code_XXXX}"
dry_run="${2:-}"
root="$(cd "$(dirname "$0")/.." && pwd)"
wav_dir="$root/src/MorseTrainer/Assets/Voice"
m4a_dir="$root/src/MorseTrainer.Mobile/Resources/Raw/voice"
# Фильтр: тишина с начала, разворот, тишина с конца, разворот обратно, гул, громкость (-18 LUFS, пики до -1,5 дБ)
filter="silenceremove=start_periods=1:start_threshold=-45dB:start_silence=0.05,areverse,silenceremove=start_periods=1:start_threshold=-45dB:start_silence=0.1,areverse,highpass=f=80,loudnorm=I=-18:TP=-1.5:LRA=7"

declare -A done_codes=()
imported=0
while IFS= read -r -d '' file; do
  name="$(basename "$file")"
  lower="${name,,}"
  code="${lower%.*}"
  if [[ ! "$code" =~ ^code_[01]{1,7}$ ]]; then
    echo "пропуск: $name (имя не code_XXXX)"
    continue
  fi
  if [[ -n "${done_codes[$code]:-}" ]]; then
    echo "пропуск: $name (второй файл для $code)"
    continue
  fi
  if [[ ! -f "$wav_dir/$code.wav" ]]; then
    echo "пропуск: $name (такого кода нет во встроенном голосе)"
    continue
  fi
  done_codes[$code]=1
  if [[ "$dry_run" == "--dry-run" ]]; then
    echo "будет встроен: $name → $code"
    continue
  fi
  ffmpeg -v error -y -i "$file" -af "$filter" -ac 1 -ar 44100 -c:a pcm_s16le "$wav_dir/$code.wav"
  ffmpeg -v error -y -i "$wav_dir/$code.wav" -ac 1 -ar 44100 -c:a aac -b:a 128k -movflags +faststart "$m4a_dir/$code.m4a"
  duration="$(ffprobe -v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 "$wav_dir/$code.wav")"
  printf 'встроен: %-22s → %s (%.2f с)\n' "$name" "$code" "$duration"
  imported=$((imported + 1))
done < <(find "$source_dir" -maxdepth 1 -type f \( -iname 'code_*.m4a' -o -iname 'code_*.mp3' -o -iname 'code_*.wav' -o -iname 'code_*.ogg' -o -iname 'code_*.opus' \) -print0 | sort -z)

missing=()
for wav in "$wav_dir"/code_*.wav; do
  code="$(basename "$wav" .wav)"
  [[ -z "${done_codes[$code]:-}" ]] && missing+=("$code")
done
echo "Встроено записей: $imported из $(ls "$wav_dir"/code_*.wav | wc -l)."
if (( ${#missing[@]} > 0 )); then
  echo "Без новой записи (остался прежний голос): ${missing[*]}"
fi

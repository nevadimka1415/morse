#!/usr/bin/env python3
"""Проверка, что у собранного приложения есть своя иконка, а не стандартный значок системы.

Запуск: python3 scripts/check-app-icon.py <файл .apk или .ipa> [ещё файлы…]

APK: в двоичном AndroidManifest.xml у <application> должны быть android:icon и android:roundIcon (ссылки на ресурсы),
а в пакете — сами картинки res/mipmap-*. Без android:icon лаунчер показывает стандартный значок Android, хотя картинки
в пакете лежат, — за это RuStore отклонил 2.9.1: «иконка на витрине не совпадает с иконкой установленного приложения».
IPA: в Info.plist должны быть сведения об иконке (CFBundleIcons — их пишет сборка, когда в исходном Info.plist указан
XSAppIconAssets), а в пакете — Assets.car.
Нужен только Python 3, без пакетов.
"""
import plistlib
import re
import struct
import sys
import zipfile

# Номера атрибутов Android (android.R.attr): имя в пуле строк может быть вырезано, номер остаётся
ATTRIBUTES = {0x01010001: "label", 0x01010002: "icon", 0x0101052C: "roundIcon"}
STRING_POOL, RESOURCE_MAP, START_TAG = 0x0001, 0x0180, 0x0102
TYPE_REFERENCE, TYPE_STRING = 0x01, 0x03


def manifest_tags(data):
    """Теги двоичного AndroidManifest.xml: список (имя тега, {атрибут: (тип значения, значение)})."""
    strings, resource_ids, tags = [], [], []
    position = 8
    while position < len(data):
        chunk, header_size, size = struct.unpack_from("<HHI", data, position)
        if size < 8:
            raise ValueError("повреждённый AndroidManifest.xml")
        if chunk == STRING_POOL:
            count, _, flags, strings_start, _ = struct.unpack_from("<IIIII", data, position + 8)
            utf8 = bool(flags & 0x100)
            base = position + strings_start
            for offset in struct.unpack_from(f"<{count}I", data, position + header_size):
                at = base + offset
                if utf8:
                    at += 2 if data[at] & 0x80 else 1          # длина в символах
                    length = data[at]
                    at += 1
                    if length & 0x80:
                        length = ((length & 0x7F) << 8) | data[at]
                        at += 1
                    strings.append(data[at:at + length].decode("utf-8", "replace"))
                else:
                    length = struct.unpack_from("<H", data, at)[0]
                    at += 2
                    if length & 0x8000:
                        length = ((length & 0x7FFF) << 16) | struct.unpack_from("<H", data, at)[0]
                        at += 2
                    strings.append(data[at:at + 2 * length].decode("utf-16le", "replace"))
        elif chunk == RESOURCE_MAP:
            resource_ids = list(struct.unpack_from(f"<{(size - header_size) // 4}I", data, position + header_size))
        elif chunk == START_TAG:
            _, name, start, attribute_size, count = struct.unpack_from("<iiHHH", data, position + 16)
            attributes = {}
            for index in range(count):
                at = position + 16 + start + index * attribute_size
                _, attribute, _, _, _, value_type, value = struct.unpack_from("<iiiHBBI", data, at)
                resource = resource_ids[attribute] if attribute < len(resource_ids) else 0
                attributes[ATTRIBUTES.get(resource) or strings[attribute]] = (value_type, value)
            tags.append((strings[name], attributes))
        position += size
    return tags


def check_apk(archive):
    problems = []
    names = archive.namelist()
    tags = manifest_tags(archive.read("AndroidManifest.xml"))
    application = next((attributes for name, attributes in tags if name == "application"), None)
    if application is None:
        return ["в манифесте нет <application>"], ""
    for attribute in ("icon", "roundIcon"):
        value_type, value = application.get(attribute, (None, 0))
        if value_type != TYPE_REFERENCE or not value:
            problems.append(f"у <application> нет android:{attribute} — лаунчер покажет стандартный значок Android")
    pictures = [name for name in names if re.match(r"res/mipmap[^/]*/", name)]
    if not pictures:
        problems.append("в пакете нет картинок иконки (res/mipmap-*)")
    adaptive = [name for name in pictures if name.endswith(".xml")]
    icon = application.get("icon", (None, 0))[1]
    return problems, f"android:icon=@0x{icon:08x}, картинок иконки: {len(pictures)}, из них адаптивных: {len(adaptive)}"


def check_ipa(archive):
    problems = []
    names = archive.namelist()
    info = next((name for name in names if re.fullmatch(r"Payload/[^/]+\.app/Info\.plist", name)), None)
    if info is None:
        return ["в пакете нет Payload/*.app/Info.plist"], ""
    plist = plistlib.loads(archive.read(info))
    keys = sorted(key for key in plist if key.startswith("CFBundleIcon"))
    if not keys:
        problems.append("в Info.plist нет CFBundleIcons — у приложения на iPhone не будет иконки (XSAppIconAssets?)")
    bundle = info.rsplit("/", 1)[0]
    if f"{bundle}/Assets.car" not in names:
        problems.append("в пакете нет Assets.car с иконкой")
    pictures = [name for name in names if re.fullmatch(re.escape(bundle) + r"/AppIcon[^/]*\.png", name)]
    return problems, f"{', '.join(keys)}; Assets.car; отдельных картинок AppIcon: {len(pictures)}"


def main():
    # На раннере Windows вывод идёт в cp1252 — русский текст без этого роняет print
    for stream in (sys.stdout, sys.stderr):
        stream.reconfigure(encoding="utf-8", errors="replace")
    if len(sys.argv) < 2:
        raise SystemExit(__doc__)
    failed = False
    for path in sys.argv[1:]:
        try:
            with zipfile.ZipFile(path) as archive:
                problems, detail = check_ipa(archive) if path.lower().endswith(".ipa") else check_apk(archive)
        except (OSError, KeyError, ValueError, struct.error, IndexError, zipfile.BadZipFile) as error:
            problems, detail = [f"не удалось прочитать: {error}"], ""
        if problems:
            failed = True
            print(f"[FAIL] {path}: " + "; ".join(problems))
        else:
            print(f"[ok] {path}: {detail}")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())

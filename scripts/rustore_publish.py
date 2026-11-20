#!/usr/bin/env python3
"""Отправка новой версии Morse Trainer в RuStore через API RuStore.

Ключ создаётся в RuStore Консоли: «Разработчик» → «API RuStore» → «Создать ключ».
Скрипт берёт его из переменных окружения:
  RUSTORE_KEY_ID       — ID ключа из таблицы ключей;
  RUSTORE_PRIVATE_KEY  — приватный ключ (Base64, как его показывает консоль).

  python scripts/rustore_publish.py proverka
      вход по ключу и пять последних версий со статусами (только чтение);
  python scripts/rustore_publish.py vylozhit <файл.apk> <версия>
      черновик версии с «Что нового» из CHANGELOG.md → загрузка APK → отправка на модерацию.
      Публикация — сразу после одобрения модератором (INSTANTLY).

Нужен пакет cryptography (подпись запроса на вход — SHA512withRSA).
"""
import base64
import datetime as dt
import json
import os
import pathlib
import re
import ssl
import sys
import urllib.error
import urllib.request
import uuid

from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import padding

ХОСТ = "https://public-api-m.rustore.ru"
ПАКЕТ = "ru.morsetrainer.app"
КОРЕНЬ = pathlib.Path(__file__).resolve().parent.parent
# API RuStore подписан сертификатом «Russian Trusted Root CA» (Минцифры), которого нет в обычных хранилищах
# (раннеры GitHub, Linux, Windows) — без него соединение рвётся на проверке сертификата. Корень и промежуточный
# сертификат лежат рядом и добавляются к системным только для этих запросов.
КОНТЕКСТ = ssl.create_default_context()
КОНТЕКСТ.load_verify_locations(cafile=str(pathlib.Path(__file__).with_name("russian-trusted-ca.pem")))


def запрос(метод, путь, токен=None, тело=None, файл=None):
    """Один запрос к API. Возвращает разобранный JSON; при code != OK — исключение с текстом ошибки."""
    заголовки = {"Accept": "application/json"}
    данные = None
    if токен:
        заголовки["Public-Token"] = токен
    if файл is not None:
        # multipart/form-data с одним полем file
        граница = uuid.uuid4().hex
        имя = pathlib.Path(файл).name
        данные = (
            f"--{граница}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"{имя}\"\r\n"
            "Content-Type: application/vnd.android.package-archive\r\n\r\n"
        ).encode() + pathlib.Path(файл).read_bytes() + f"\r\n--{граница}--\r\n".encode()
        заголовки["Content-Type"] = f"multipart/form-data; boundary={граница}"
    elif тело is not None:
        данные = json.dumps(тело, ensure_ascii=False).encode("utf-8")
        заголовки["Content-Type"] = "application/json; charset=utf-8"
    r = urllib.request.Request(ХОСТ + путь, data=данные, method=метод, headers=заголовки)
    try:
        with urllib.request.urlopen(r, timeout=600, context=КОНТЕКСТ) as ответ:
            j = json.load(ответ)
    except urllib.error.HTTPError as e:
        текст = e.read().decode("utf-8", "replace")
        raise SystemExit(f"RuStore {метод} {путь}: HTTP {e.code}: {текст[:500]}")
    if j.get("code") != "OK":
        raise SystemExit(f"RuStore {метод} {путь}: {j.get('code')}: {j.get('message')}")
    return j


def токен():
    """Вход по ключу: подпись строки keyId + timestamp алгоритмом SHA512withRSA, ответ — JWE на 900 секунд."""
    ид = os.environ.get("RUSTORE_KEY_ID", "").strip()
    ключ = re.sub(r"\s+", "", os.environ.get("RUSTORE_PRIVATE_KEY", ""))
    if not ид or not ключ:
        raise SystemExit("нет RUSTORE_KEY_ID или RUSTORE_PRIVATE_KEY")
    закрытый = serialization.load_der_private_key(base64.b64decode(ключ), password=None)
    время = dt.datetime.now(dt.timezone.utc).isoformat(timespec="milliseconds")
    подпись = закрытый.sign((ид + время).encode("utf-8"), padding.PKCS1v15(), hashes.SHA512())
    j = запрос("POST", "/public/auth", тело={"keyId": ид, "timestamp": время,
                                            "signature": base64.b64encode(подпись).decode()})
    return j["body"]["jwe"]


def версии(т, сколько=5, ид=None):
    путь = f"/public/v1/application/{ПАКЕТ}/version?" + (f"ids={ид}" if ид else f"page=0&size={сколько}")
    return запрос("GET", путь, т)["body"]["content"]


def что_нового(версия):
    """Раздел «## <версия>» из CHANGELOG.md простым текстом (до 5000 знаков — предел RuStore)."""
    текст = (КОРЕНЬ / "CHANGELOG.md").read_text(encoding="utf-8")
    m = re.search(rf"^## {re.escape(версия)}\s*$(.*?)(?=^## |\Z)", текст, re.M | re.S)
    if not m:
        raise SystemExit(f"в CHANGELOG.md нет раздела «## {версия}»")
    строки = [re.sub(r"^(\s*)- ", r"\1• ", с.rstrip()) for с in m.group(1).strip().splitlines()]
    итог = re.sub(r"[*_`]", "", "\n".join(строки))
    return итог if len(итог) <= 5000 else итог[:4990].rsplit("\n", 1)[0] + "\n…"


def показать(список):
    for в in список:
        print(f"  {в.get('versionName')} (код {в.get('versionCode')}) — {в.get('versionStatus')}, id {в.get('versionId')}")


def main():
    if len(sys.argv) >= 2 and sys.argv[1] == "proverka":
        т = токен()
        print("RuStore: вход по ключу — ОК; последние версии:")
        показать(версии(т))
        return
    if len(sys.argv) == 4 and sys.argv[1] == "vylozhit":
        apk, версия = sys.argv[2], sys.argv[3].lstrip("v")
        новое = что_нового(версия)
        т = токен()
        j = запрос("POST", f"/public/v1/application/{ПАКЕТ}/version", т,
                   тело={"whatsNew": новое, "publishType": "INSTANTLY"})
        # номер черновика в примерах документации лежит то в body, то в body.versionId
        тело = j.get("body")
        ид = тело.get("versionId") if isinstance(тело, dict) else тело
        print(f"RuStore: черновик {ид} создан (прежний черновик, если был, RuStore удаляет сам)")
        запрос("POST", f"/public/v1/application/{ПАКЕТ}/version/{ид}/apk?isMainApk=true", т, файл=apk)
        print(f"RuStore: APK {pathlib.Path(apk).name} загружен")
        запрос("POST", f"/public/v1/application/{ПАКЕТ}/version/{ид}/commit?priorityUpdate=0", т)
        print(f"RuStore: версия {версия} отправлена на модерацию; выйдет сразу после одобрения")
        показать(версии(т, ид=ид))
        return
    print(__doc__)
    sys.exit(2)


if __name__ == "__main__":
    main()

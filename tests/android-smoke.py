#!/usr/bin/env python3
"""Дымовой тест APK на Android-эмуляторе (job android-smoke в .github/workflows/mobile.yml).

Ставит APK, запускает главную активность, ждёт первое задание на вкладке «Тренировка»,
нажимает «Слушать»/«Стоп», раскрывает и сворачивает «Параметры», проходит по пяти вкладкам нижней панели,
открывает книжку курса («Начать курс»): свайп по тексту листает её в обе стороны, системная «Назад» закрывает без
старта курса; затем листает кнопками до «Начать шаг 1» и получает задание шага 1, а через «⋯» → «Выбрать шаг…»
делает текущим шаг 3; в «Настройках» нажимает значок вверху (пасхалка)
(на «На слух» — ответ и автопереход к следующему символу, затем раунд с выключенным автопереходом) и на каждом шаге
снимает скриншот, проверяет, что процесс жив и в logcat нет падений приложения. Затем пересоздаёт
активность и повторяет проход на русском (язык приложения ru-RU) — скриншоты android-ru-* идут в README:
там же внизу «Настроек» должна быть «Проверить обновления», а сброс курса «⋯» → «Сбросить курс» → «Начать курс» → «Пропустить»
снова даёт шаг 1 (книжка по-русски — скриншоты для карточки RuStore).
Если передан APK для RuStore, он ставится поверх: в его «Настройках» кнопки «Проверить обновления» быть не должно.

Запуск: python3 tests/android-smoke.py <apk или папка с apk> <папка для скриншотов и логов> [<APK для RuStore или папка>]
"""
import os
import re
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
from pathlib import Path

PACKAGE = "ru.morsetrainer.app"
# Вкладки в порядке AppShell: имя файла, русское и английское название (язык эмулятора — системный)
TABS = [
    ("training", "Тренировка", "Training"),
    ("learning", "Обучение", "Learning"),
    ("quiz", "На слух", "By ear"),
    ("keyer", "Передача", "Sending"),
    ("settings", "Настройки", "Settings"),
]
READY_PREFIXES = ("Готово ·", "Done ·")
PLAY_TEXTS = ("▶ Слушать", "▶ Play")
STOP_TEXTS = ("■ Стоп", "■ Stop")
QUIZ_NEW_TEXTS = ("▶ Новый символ", "▶ New symbol")
QUIZ_QUESTION_TEXTS = ("Какой символ прозвучал?", "Which symbol was that?")
QUIZ_ANSWERED_PREFIXES = ("Верно:", "Правильно:", "Correct:")
PARAMS_TEXTS = ("Параметры ▾", "Параметры ▴", "Parameters ▾", "Parameters ▴")
PARAMS_OPEN_TEXTS = ("Изменить в настройках", "Change in settings")
START_COURSE_TEXTS = ("Начать курс", "Start the course")
BOOK_TITLE_TEXTS = ("Как устроен курс", "How the course works")
KOCH_TITLE_TEXTS = ("Метод Коха", "Koch method")
UPDATES_TEXTS = ("Проверить обновления", "Check for updates")
RUSTORE_NOTE_PREFIXES = ("Обновления приходят через RuStore", "Updates come through RuStore")
LOGO_TEXTS = ("Morse Trainer",)   # описание значка для TalkBack (content-desc)
NEXT_STEP_TEXTS = ("Следующий шаг", "Next step")
COURSE_MENU_TEXTS = ("Меню курса", "Course menu")
CHOOSE_STEP_TEXTS = ("Выбрать шаг…", "Choose step…")
RESET_COURSE_TEXTS = ("Сбросить курс", "Reset the course")
STEP3_TITLE_PREFIXES = ("Курс «С нуля до 60 зн/мин» · шаг 3 из", "Course “From zero to 60 cpm” · step 3 of")
rustore_apk_arg = sys.argv[3] if len(sys.argv) > 3 else None

out_dir = Path(sys.argv[2] if len(sys.argv) > 2 else "android-smoke")
results = []
last_dump = ""


def adb(*args, timeout=120, check=True, binary=False):
    completed = subprocess.run(["adb", *args], capture_output=True, timeout=timeout)
    if check and completed.returncode != 0:
        raise RuntimeError(f"adb {' '.join(args)} -> {completed.returncode}\n"
                           f"{completed.stdout.decode(errors='replace')}{completed.stderr.decode(errors='replace')}")
    return completed.stdout if binary else completed.stdout.decode("utf-8", errors="replace")


def find_apk(path):
    path = Path(path)
    if path.is_file():
        return path
    apks = sorted(path.rglob("*.apk"))
    signed = [apk for apk in apks if apk.name.endswith("-Signed.apk")]
    if not apks:
        raise RuntimeError(f"APK не найден в {path}")
    return (signed or apks)[0]


def dump_ui():
    """Дерево элементов экрана без системных окон «не отвечает» (их закрываем кнопкой Wait)."""
    for _ in range(4):
        root, parents = raw_dump()
        texts = " ".join(node.get("text", "") for node in root.iter("node"))
        waits = nodes_with_text(root, ("Wait",))
        if "isn't responding" in texts and waits and "Morse" not in texts:
            print("Закрываю системное окно «не отвечает» (Wait): " + texts[:120])
            tap(waits[0])
            time.sleep(3)
            continue
        return root, parents
    return root, parents


def raw_dump():
    """Дерево элементов экрана через uiautomator; повторяет, пока экран не успокоится."""
    last_error = ""
    for _ in range(6):
        output = adb("shell", "uiautomator", "dump", "/sdcard/ui.xml", check=False, timeout=60)
        if "dumped to" in output:
            global last_dump
            xml = adb("exec-out", "cat", "/sdcard/ui.xml")
            last_dump = xml
            root = ET.fromstring(xml)
            parents = {child: parent for parent in root.iter() for child in parent}
            return root, parents
        last_error = output.strip()
        time.sleep(1)
    raise RuntimeError(f"uiautomator dump не удался: {last_error}")


def bounds(node):
    x1, y1, x2, y2 = map(int, re.findall(r"-?\d+", node.get("bounds", "[0,0][0,0]")))
    return x1, y1, x2, y2


def normalize(value):
    """Только буквы и цифры без регистра: значки ▶/■, пробелы и заглавные буквы кнопок не мешают поиску."""
    return "".join(symbol for symbol in value.casefold() if symbol.isalnum())


def nodes_with_text(root, texts, prefix=False):
    targets = [normalize(text) for text in texts]
    found = []
    for node in root.iter("node"):
        for value in (node.get("text", ""), node.get("content-desc", "")):
            value = normalize(value)
            if value and any(value.startswith(t) if prefix else value == t for t in targets):
                found.append(node)
                break
    return found


def tap(node):
    x1, y1, x2, y2 = bounds(node)
    adb("shell", "input", "tap", str((x1 + x2) // 2), str((y1 + y2) // 2))


def wait_for(texts, timeout, prefix=False):
    deadline = time.time() + timeout
    while time.time() < deadline:
        ensure_alive()
        root, _ = dump_ui()
        found = nodes_with_text(root, texts, prefix)
        if found:
            return found
        time.sleep(2)
    raise RuntimeError(f"За {timeout} с на экране не появилось: {', '.join(texts)}")


def crash_lines():
    """Строки о падении именно нашего процесса: Java/.NET-исключение, нативный сигнал, ANR."""
    log = adb("logcat", "-d", "-b", "main", "-b", "system", "-b", "crash", timeout=60)
    lines = log.splitlines()
    bad = []
    for index, line in enumerate(lines):
        if f"Process: {PACKAGE}" in line or f">>> {PACKAGE} <<<" in line or f"ANR in {PACKAGE}" in line:
            bad.extend(lines[max(0, index - 2):index + 25])
        elif "UNHANDLED EXCEPTION" in line and ("mono" in line.lower() or "droid" in line.lower()):
            bad.extend(lines[index:index + 25])
    return bad


def ensure_alive():
    pid = adb("shell", "pidof", PACKAGE, check=False).strip()
    if not pid:
        raise RuntimeError("Процесс приложения не запущен (упал или закрылся)")
    bad = crash_lines()
    if bad:
        raise RuntimeError("В logcat падение приложения:\n" + "\n".join(bad[:60]))
    return pid


def screenshot(name):
    data = adb("exec-out", "screencap", "-p", binary=True, timeout=60)
    path = out_dir / f"android-{name}.png"
    path.write_bytes(data)
    print(f"Скриншот {path} ({len(data)} байт)")
    if len(data) < 5000:
        raise RuntimeError(f"Скриншот {name} подозрительно маленький: {len(data)} байт")


def tab_node(root, parents, titles):
    """Кнопка вкладки — самый нижний элемент с таким названием (над ней может быть заголовок страницы)."""
    candidates = nodes_with_text(root, titles)
    if not candidates:
        return None
    return max(candidates, key=lambda node: bounds(node)[1])


def is_selected(node, parents):
    current = node
    for _ in range(4):
        if current is None:
            return False
        if current.get("selected") == "true":
            return True
        current = parents.get(current)
    return False


def step(name, action):
    started = time.time()
    try:
        detail = action() or ""
        results.append((name, "ok", f"{time.time() - started:.1f} с", detail))
        print(f"[ok] {name} {detail}")
    except Exception as error:
        results.append((name, "FAIL", f"{time.time() - started:.1f} с", str(error).splitlines()[0]))
        print(f"[FAIL] {name}: {error}")
        # Последний дамп экрана и видимые тексты — чтобы понять, что было на экране
        if last_dump:
            (out_dir / "ui-failed.xml").write_text(last_dump, encoding="utf-8")
            try:
                texts = [node.get("text") or node.get("content-desc") for node in ET.fromstring(last_dump).iter("node")]
                print("Тексты на экране: " + " | ".join(text for text in texts if text))
            except ET.ParseError:
                pass
        raise


def launch():
    component = adb("shell", "cmd", "package", "resolve-activity", "--brief",
                    "-a", "android.intent.action.MAIN", "-c", "android.intent.category.LAUNCHER", PACKAGE).strip().splitlines()[-1]
    if "/" not in component:
        raise RuntimeError(f"Не найдена главная активность: {component}")
    output = adb("shell", "am", "start", "-W", "-n", component, timeout=180)
    print(output)
    if "Status: ok" not in output:
        raise RuntimeError("am start не подтвердил запуск")
    total = re.search(r"TotalTime: (\d+)", output)
    return f"{component}, запуск {total.group(1) if total else '?'} мс"


def training_ready():
    found = wait_for(READY_PREFIXES, 120, prefix=True)
    screenshot("training")
    return found[0].get("text", "")


def close_alert():
    """Закрывает окно сообщения приложения (DisplayAlert) и возвращает его текст, если оно было."""
    root, _ = dump_ui()
    button = [node for node in root.iter("node") if node.get("resource-id", "").endswith(":id/button1")]
    if not button:
        return ""
    text = " / ".join(node.get("text", "") for node in root.iter("node")
                      if node.get("resource-id", "").endswith(("alertTitle", ":id/message")) and node.get("text"))
    screenshot("alert")
    tap(button[0])
    time.sleep(1)
    return text


def play_and_stop():
    root, _ = dump_ui()
    play = nodes_with_text(root, PLAY_TEXTS)
    if not play:
        raise RuntimeError("Нет кнопки «Слушать»")
    tap(play[0])
    time.sleep(3)
    ensure_alive()
    alert = close_alert()
    if alert:
        # Ошибка звука на эмуляторе без аудиоустройства — не падение; падение ловит ensure_alive
        return f"сообщение вместо звука: {alert}"
    root, _ = dump_ui()
    stop = nodes_with_text(root, STOP_TEXTS)
    if stop:
        tap(stop[0])
    time.sleep(1)
    ensure_alive()
    return "звук запущен и остановлен"


def open_tab(key, titles):
    def action():
        # Нажатие может съесть системное окно «не отвечает» — пробуем до трёх раз, пока вкладка не станет выбранной
        for attempt in range(1, 4):
            root, parents = dump_ui()
            node = tab_node(root, parents, titles)
            if node is None:
                raise RuntimeError(f"Не найдена вкладка {titles}")
            tap(node)
            time.sleep(3)
            ensure_alive()
            root, parents = dump_ui()
            node = tab_node(root, parents, titles)
            if node is not None and is_selected(node, parents):
                screenshot(key)
                return "вкладка выбрана" + (f" с попытки {attempt}" if attempt > 1 else "")
        screenshot(key)
        raise RuntimeError(f"Вкладка {titles[0]} не выбралась за три нажатия")
    return action


def quiz_answers():
    root, _ = dump_ui()
    return [node for node in root.iter("node") if node.get("clickable") == "true" and len(node.get("text", "")) == 1]


def quiz_round(prefix=""):
    """«На слух»: «Новый символ» → звучит сигнал → четыре варианта → ответ; следующий символ звучит сам (автопереход)."""
    def action():
        root, _ = dump_ui()
        new = nodes_with_text(root, QUIZ_NEW_TEXTS)
        if not new:
            raise RuntimeError("Нет кнопки «Новый символ»")
        tap(new[0])
        time.sleep(3)
        ensure_alive()
        alert = close_alert()
        wait_for(QUIZ_QUESTION_TEXTS, 30)
        answers = quiz_answers()
        if len(answers) != 4:
            raise RuntimeError(f"Ожидалось 4 варианта ответа, на экране {len(answers)}")
        before = [node.get("text") for node in answers]
        screenshot(prefix + "quiz-question")
        tap(answers[0])
        # Ответ виден 1–2 с, потом звучит следующий символ: снимок сразу, без дампа экрана
        time.sleep(0.4)
        screenshot(prefix + "quiz-answered")
        deadline = time.time() + 30
        while time.time() < deadline:
            ensure_alive()
            close_alert()
            root, _ = dump_ui()
            after = [node.get("text") for node in quiz_answers()]
            if nodes_with_text(root, QUIZ_QUESTION_TEXTS) and len(after) == 4 and after != before:
                return f"ответ {before[0]}; следующий вопрос сам: {' '.join(before)} → {' '.join(after)}" + (f"; звук: {alert}" if alert else "")
            time.sleep(1)
        raise RuntimeError(f"После ответа следующий символ не прозвучал сам (варианты {' '.join(before)})")
    return action


def quiz_manual_round():
    """Переключатель автоперехода: выключен — после ответа вопрос остаётся на экране с верным символом; потом включается обратно."""
    def toggle():
        for _ in range(3):
            root, _ = dump_ui()
            switches = [node for node in root.iter("node") if node.get("checkable") == "true"]
            if switches:
                tap(switches[-1])
                time.sleep(1)
                return switches[-1].get("checked")
            # Переключатель ниже края экрана — прокрутка вверх
            adb("shell", "input", "swipe", "540", "1600", "540", "700", "300")
            time.sleep(1)
        raise RuntimeError("Не найден переключатель «Следующий символ — сразу после ответа»")

    def action():
        was = toggle()
        if was != "true":
            raise RuntimeError(f"Автопереход по умолчанию должен быть включён, а переключатель: checked={was}")
        adb("shell", "input", "swipe", "540", "700", "540", "1600", "300")
        time.sleep(1)
        root, _ = dump_ui()
        new = nodes_with_text(root, QUIZ_NEW_TEXTS)
        if not new:
            raise RuntimeError("Нет кнопки «Новый символ»")
        tap(new[0])
        time.sleep(3)
        close_alert()
        wait_for(QUIZ_QUESTION_TEXTS, 30)
        answers = quiz_answers()
        if len(answers) != 4:
            raise RuntimeError(f"Ожидалось 4 варианта ответа, на экране {len(answers)}")
        tap(answers[0])
        found = wait_for(QUIZ_ANSWERED_PREFIXES, 15, prefix=True)
        # Без автоперехода ответ остаётся на экране
        time.sleep(4)
        root, _ = dump_ui()
        if not nodes_with_text(root, QUIZ_ANSWERED_PREFIXES, prefix=True):
            raise RuntimeError("Автопереход выключен, но ответ сменился следующим вопросом")
        toggle()
        return found[0].get("text", "") + "; ответ остался на экране, автопереход снова включён"
    return action


def training_params():
    """«Тренировка»: параметры свёрнуты, «Параметры ▾» раскрывает сводку, повторное нажатие сворачивает."""
    root, _ = dump_ui()
    if nodes_with_text(root, PARAMS_OPEN_TEXTS):
        raise RuntimeError("Параметры должны быть свёрнуты по умолчанию")
    toggle = nodes_with_text(root, PARAMS_TEXTS)
    if not toggle:
        raise RuntimeError("Нет кнопки «Параметры ▾»")
    tap(toggle[0])
    found = wait_for(PARAMS_OPEN_TEXTS, 10)
    screenshot("training-params")
    root, _ = dump_ui()
    tap(nodes_with_text(root, PARAMS_TEXTS)[0])
    time.sleep(2)
    root, _ = dump_ui()
    if nodes_with_text(root, PARAMS_OPEN_TEXTS):
        raise RuntimeError("Параметры не свернулись")
    return "раскрыты и свёрнуты"


def course_book():
    """«Обучение» → «Начать курс»: раскрывается книжка, листается до конца, «Начать шаг 1» открывает задание курса."""
    root, _ = dump_ui()
    start = nodes_with_text(root, ("Начать курс", "Start the course"))
    if not start:
        raise RuntimeError("Нет кнопки «Начать курс»")
    tap(start[0])
    wait_for(("Как устроен курс", "How the course works"), 20)
    time.sleep(2.5)   # обложка раскрывается ~1,2 с
    screenshot("course-book")
    for page in range(2, 6):
        root, _ = dump_ui()
        nxt = nodes_with_text(root, ("Далее ›", "Next ›"))
        if not nxt:
            raise RuntimeError(f"Нет кнопки «Далее» перед страницей {page}")
        tap(nxt[0])
        time.sleep(1)
    found = wait_for(("Начать шаг 1", "Start step 1"), 10)
    screenshot("course-book-last")
    tap(found[0])
    wait_for(READY_PREFIXES, 60, prefix=True)
    ensure_alive()
    screenshot("course-step-1")
    return "книжка пролистана до конца, шаг 1 курса создан"


def screen_size():
    match = re.search(r"(\d+)x(\d+)", adb("shell", "wm", "size"))
    return (int(match.group(1)), int(match.group(2))) if match else (1080, 2400)


def swipe(x1, y1, x2, y2, duration=300):
    adb("shell", "input", "swipe", str(x1), str(y1), str(x2), str(y2), str(duration))


def scroll_to_bottom(times=6):
    """Листает страницу вниз у самого левого края — мимо ползунков, чтобы случайно не сдвинуть настройки."""
    _, height = screen_size()
    for _ in range(times):
        swipe(20, height * 3 // 4, 20, height // 4, 250)
        time.sleep(0.7)


def course_book_swipe_and_back():
    """Книжка: свайп по тексту страницы листает в обе стороны, системная «Назад» закрывает её без старта курса."""
    root, _ = dump_ui()
    start = nodes_with_text(root, START_COURSE_TEXTS)
    if not start:
        raise RuntimeError("Нет кнопки «Начать курс»")
    tap(start[0])
    title = wait_for(BOOK_TITLE_TEXTS, 20)
    time.sleep(2.5)   # обложка раскрывается ~1,2 с
    # По самому тексту (первый абзац под заголовком), а не по полям страницы
    width, _ = screen_size()
    y = bounds(title[0])[3] + 250
    swipe(width * 3 // 4, y, width // 5, y)
    wait_for(KOCH_TITLE_TEXTS, 10)
    screenshot("course-book-swiped")
    swipe(width // 5, y, width * 3 // 4, y)
    wait_for(BOOK_TITLE_TEXTS, 10)
    adb("shell", "input", "keyevent", "4")   # системная «Назад»
    time.sleep(2)
    ensure_alive()
    root, _ = dump_ui()
    if nodes_with_text(root, BOOK_TITLE_TEXTS):
        raise RuntimeError("Системная «Назад» не закрыла книжку")
    if not nodes_with_text(root, START_COURSE_TEXTS):
        raise RuntimeError("После «Назад» курс начался, а не должен был")
    return "свайп по тексту листает туда и обратно, «Назад» закрыла книжку без старта курса"


def logo_easter_egg():
    """«Настройки»: значок «· — —» вверху — нажатие играет пасхалку; приложение не падает."""
    found = wait_for(LOGO_TEXTS, 10)
    x1, y1, x2, y2 = bounds(found[0])
    if y1 > 900:
        raise RuntimeError(f"Значок не вверху страницы: {bounds(found[0])}")
    tap(found[0])
    time.sleep(2)
    ensure_alive()
    screenshot("settings-logo")
    return f"значок вверху ({x1},{y1}), нажатие — процесс жив"


def course_menu_button(root):
    """«⋯» курса: по описанию для TalkBack, а если его нет — кнопка в том же ряду правее «Следующий шаг»."""
    found = nodes_with_text(root, COURSE_MENU_TEXTS)
    if found:
        return found[0]
    following = nodes_with_text(root, NEXT_STEP_TEXTS)
    if not following:
        return None
    _, top, right, bottom = bounds(following[0])
    middle = (top + bottom) // 2
    row = [node for node in root.iter("node") if node.get("class", "").endswith("Button")
           and bounds(node)[0] >= right - 5 and bounds(node)[1] < middle < bounds(node)[3]]
    return row[0] if row else None


def choose_course_step_3():
    """«⋯» → «Выбрать шаг…» → «3. …»: шаг 3 становится текущим (видно в заголовке курса), задание не создаётся."""
    root, _ = dump_ui()
    menu = course_menu_button(root)
    if menu is None:
        raise RuntimeError("Нет кнопки «⋯» курса")
    tap(menu)
    tap(wait_for(CHOOSE_STEP_TEXTS, 10)[0])
    item = wait_for(("3.",), 10, prefix=True)
    screenshot("course-choose-step")
    tap(item[0])
    found = wait_for(STEP3_TITLE_PREFIXES, 10, prefix=True)
    ensure_alive()
    screenshot("learning-step-3")
    return found[0].get("text", "")


def updates_button_visible():
    """Обычная сборка: внизу «Настроек» есть «Проверить обновления» — контроль для проверки сборки RuStore."""
    scroll_to_bottom()
    wait_for(UPDATES_TEXTS, 10)
    screenshot("ru-settings-about")
    return "кнопка на месте"


def reset_course_and_skip_book():
    """«⋯» → «Сбросить курс», «Начать курс» снова открывает книжку (по-русски), «Пропустить» сразу даёт шаг 1."""
    root, _ = dump_ui()
    menu = course_menu_button(root)
    if menu is None:
        raise RuntimeError("Нет кнопки «⋯» курса — курс не начат?")
    tap(menu)
    # Сброс — пунктом меню «⋯», одним касанием его не нажать
    tap(wait_for(("Сбросить курс",), 10)[0])
    start = wait_for(("Начать курс",), 15)
    tap(start[0])
    wait_for(("Как устроен курс",), 20)
    time.sleep(2.5)   # обложка раскрывается ~1,2 с
    screenshot("ru-course-book")
    root, _ = dump_ui()
    following = nodes_with_text(root, ("Далее ›",))
    if not following:
        raise RuntimeError("Нет кнопки «Далее»")
    tap(following[0])
    wait_for(("Метод Коха",), 10)
    time.sleep(1)
    screenshot("ru-course-book-koch")
    root, _ = dump_ui()
    skip = nodes_with_text(root, ("Пропустить",))
    if not skip:
        raise RuntimeError("Нет кнопки «Пропустить»")
    tap(skip[0])
    wait_for(READY_PREFIXES[:1], 60, prefix=True)
    ensure_alive()
    return "курс сброшен, книжка по-русски, «Пропустить» дал задание"


def course_on_step_one():
    """После «Пропустить» курс действительно на шаге 1: это видно в заголовке курса на «Обучении»."""
    open_tab("ru-learning-step-1", ("Обучение",))()
    found = wait_for(("Курс «С нуля до 60 зн/мин» · шаг 1 из",), 10, prefix=True)
    return found[0].get("text", "")


def install_rustore():
    apk = find_apk(rustore_apk_arg)
    adb("install", "-r", "-g", str(apk), timeout=300)
    return f"{apk.name}, {apk.stat().st_size // 1024} КБ"


def launch_rustore():
    detail = launch()
    wait_for(READY_PREFIXES, 120, prefix=True)
    return detail


def rustore_settings():
    """Сборка для RuStore: обновления ставит магазин — в «Настройках» нет своей проверки через GitHub."""
    open_tab("rustore-settings-top", ("Настройки", "Settings"))()
    scroll_to_bottom()
    wait_for(RUSTORE_NOTE_PREFIXES, 10, prefix=True)
    root, _ = dump_ui()
    if nodes_with_text(root, UPDATES_TEXTS):
        raise RuntimeError("В сборке для RuStore осталась кнопка «Проверить обновления»")
    screenshot("rustore-settings")
    return "кнопки «Проверить обновления» нет, вместо неё — «Обновления приходят через RuStore»"


def activity_creations():
    log = adb("logcat", "-d", "-b", "events", check=False, timeout=60)
    return sum(1 for line in log.splitlines() if "wm_on_create_called" in line and "MainActivity" in line)


def recreate_activity():
    """Смена размера шрифта пересоздаёт активность (как смена шрифта или языка у пользователя): MAUI создаёт новое окно."""
    before = activity_creations()
    adb("shell", "settings", "put", "system", "font_scale", "1.15")
    time.sleep(6)
    ensure_alive()
    after = activity_creations()
    if after <= before:
        return "шрифт изменён, но активность не пересоздалась"
    return "активность пересоздана"


def switch_to_russian():
    """Язык только для приложения (cmd locale, Android 13+) и перезапуск: скриншоты ru-* — русский интерфейс для README."""
    adb("shell", "cmd", "locale", "set-app-locales", PACKAGE, "--locales", "ru-RU")
    time.sleep(3)
    adb("shell", "am", "force-stop", PACKAGE)
    time.sleep(2)
    launch()
    found = wait_for(READY_PREFIXES[:1], 120, prefix=True)
    screenshot("ru-training")
    return found[0].get("text", "")


def write_summary():
    lines = ["### Дымовой тест Android (эмулятор)", "", "| Шаг | Результат | Время | Подробности |", "|---|---|---|---|"]
    for name, status, duration, detail in results:
        lines.append(f"| {name} | {status} | {duration} | {detail.replace('|', '/')} |")
    text = "\n".join(lines) + "\n"
    (out_dir / "summary.md").write_text(text, encoding="utf-8")
    summary_file = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary_file:
        with open(summary_file, "a", encoding="utf-8") as handle:
            handle.write(text)


def main():
    out_dir.mkdir(parents=True, exist_ok=True)
    apk = find_apk(sys.argv[1] if len(sys.argv) > 1 else ".")
    exit_code = 0
    try:
        adb("wait-for-device", timeout=300)
        # Сразу после загрузки лаунчер эмулятора нередко «не отвечает» — даём системе успокоиться и прячем
        # системные окна ошибок: они перекрывают экран, а падения приложения тест ловит по logcat
        time.sleep(20)
        adb("shell", "settings", "put", "global", "hide_error_dialogs", "1", check=False)
        step("Установка APK", lambda: (adb("install", "-r", "-g", str(apk), timeout=300), f"{apk.name}, {apk.stat().st_size // 1024} КБ")[1])
        adb("logcat", "-b", "all", "-c", check=False)
        step("Запуск MainActivity", launch)
        step("Первое задание на «Тренировке»", training_ready)
        step("Слушать и Стоп", play_and_stop)
        step("Параметры: раскрыть и свернуть", training_params)
        # По всем вкладкам и обратно на «Тренировку» (второй скриншот — под своим именем)
        for key, russian, english in TABS[1:] + [("training-return",) + TABS[0][1:]]:
            step(f"Вкладка «{russian}»", open_tab(key, (russian, english)))
            if key == "quiz":
                step("На слух: ответ и следующий символ сам", quiz_round())
                step("На слух: без автоперехода", quiz_manual_round())
            if key == "settings":
                step("Настройки: значок вверху — пасхалка", logo_easter_egg)
        step("Вкладка «Обучение» для курса", open_tab("learning-course", ("Обучение", "Learning")))
        step("Книжка: свайп по тексту и «Назад»", course_book_swipe_and_back)
        step("Книжка курса и шаг 1", course_book)
        step("Вкладка «Обучение»: выбор шага", open_tab("learning-choose", ("Обучение", "Learning")))
        step("Курс: «⋯» → «Выбрать шаг…» → шаг 3", choose_course_step_3)
        # Пересоздание активности: страницы старого окна не должны ронять приложение при переключении вкладок
        step("Пересоздание активности (шрифт 1.15)", recreate_activity)
        for key, russian, english in [("learning-after-recreate",) + TABS[1][1:], ("training-after-recreate",) + TABS[0][1:]]:
            step(f"После пересоздания: «{russian}»", open_tab(key, (russian, english)))
        adb("shell", "settings", "put", "system", "font_scale", "1.0", check=False)
        # Второй проход на русском (эмулятор en-US): ищутся только русские названия вкладок
        step("Русский язык приложения", switch_to_russian)
        for key, russian, _ in TABS[1:]:
            step(f"На русском: «{russian}»", open_tab("ru-" + key, (russian,)))
            if key == "quiz":
                step("На русском: раунд «На слух»", quiz_round("ru-"))
        step("На русском: «Проверить обновления» в настройках", updates_button_visible)
        step("На русском: «Обучение» для курса", open_tab("ru-learning-course", ("Обучение",)))
        step("На русском: сброс курса, книжка и «Пропустить»", reset_course_and_skip_book)
        step("На русском: курс на шаге 1", course_on_step_one)
        step("Итог: процесс жив, падений нет", lambda: f"pid {ensure_alive()}")
        if rustore_apk_arg:
            # Сборка для RuStore ставится поверх (та же подпись и версия) и проверяется отдельно
            step("RuStore: установка поверх", install_rustore)
            step("RuStore: запуск", launch_rustore)
            step("RuStore: «Настройки» без проверки обновлений", rustore_settings)
            step("RuStore: процесс жив, падений нет", lambda: f"pid {ensure_alive()}")
    except Exception:
        exit_code = 1
    finally:
        try:
            (out_dir / "logcat.txt").write_text(adb("logcat", "-d", "-b", "all", check=False, timeout=60), encoding="utf-8")
        except Exception as error:
            print(f"Не удалось сохранить logcat: {error}")
        write_summary()
    return exit_code


if __name__ == "__main__":
    sys.exit(main())

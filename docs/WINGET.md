# Публикация в winget (Windows Package Manager)

После публикации Morse Trainer ставится и обновляется одной командой:

```
winget install Nevadimka1415.MorseTrainer
winget upgrade Nevadimka1415.MorseTrainer
```

Идентификатор пакета — `Nevadimka1415.MorseTrainer`, установщик — `MorseTrainer-Setup-x64.exe`
из GitHub Releases (Inno Setup, установка для текущего пользователя, без прав администратора).

## Один раз руками: токен и первая публикация

Пакет попадает в winget через pull request в репозиторий
[microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs). PR открывает утилита
`wingetcreate` от имени вашего аккаунта GitHub, поэтому нужен токен.

1. На GitHub: **Settings → Developer settings → Personal access tokens → Tokens (classic) →
   Generate new token**. Срок — на ваше усмотрение (год), галочка только **public_repo**.
   Скопируйте токен (он показывается один раз).
2. В репозитории morse: **Settings → Secrets and variables → Actions → New repository secret**,
   имя `WINGET_TOKEN`, значение — токен. Токен никуда больше не вставляйте и не пересылайте.
3. **Actions → Publish to winget → Run workflow**: версия последнего релиза (например `2.5.0`),
   галочка **submit**. Workflow скачает установщик, соберёт манифесты
   (`scripts/winget-manifests.ps1`), приложит их артефактом `winget-manifests` и откроет PR
   «New package: Nevadimka1415.MorseTrainer» из форка winget-pkgs в вашем аккаунте.
4. Дальше PR проверяют роботы и модераторы Microsoft (обычно 1–3 дня). Если бот оставит
   комментарий с просьбой поправить манифест — пришлите ссылку на PR, поправим скрипт.
   После слияния пакет появится в `winget search morse`.

Без токена можно так: запустить тот же workflow без галочки submit, скачать артефакт
`winget-manifests` и отправить папку вручную: `wingetcreate submit <папка с версией>`
(wingetcreate сам попросит войти в GitHub).

## Дальше автоматически

Когда пакет принят, каждый новый тег `v*` публикует релиз (`release.yml`), и job **winget**
в том же workflow открывает PR с новой версией (`wingetcreate update … --submit`).
Если секрета `WINGET_TOKEN` нет или пакет ещё не принят, job пропускает шаг с предупреждением,
релиз от этого не падает.

## Полезно знать

- Установщик не подписан сертификатом: winget это допускает (файлы проверяет антивирус
  Microsoft), но SmartScreen при ручном запуске может спросить подтверждение.
- `ProductCode` в манифесте — `{F5BA0D4D-BED2-4C8D-8963-42CE7B70C3AD}_is1` (AppId из
  `installer/MorseTrainer.iss` + `_is1`): по нему winget узнаёт уже установленную программу.
  AppId менять нельзя, иначе обновления встанут отдельной программой.
- Android-версия в магазине — см. [docs/RUSTORE.md](RUSTORE.md) (RuStore и Obtainium).

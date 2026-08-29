# Как выложить проект на GitHub

## 1. Создайте репозиторий

На GitHub нажмите **New repository** и укажите:

- Repository name: `morse-trainer`;
- Visibility: **Public**;
- не добавляйте README, `.gitignore` и лицензию — они уже есть в проекте.

## 2. Загрузите исходники

Распакуйте архив, откройте PowerShell внутри папки `morse-trainer-v2` и выполните:

```powershell
git init
git add .
git commit -m "Initial Morse Trainer release"
git branch -M main
git remote add origin https://github.com/ВАШ_ЛОГИН/morse-trainer.git
git push -u origin main
```

Замените `ВАШ_ЛОГИН` на имя пользователя GitHub.

## 3. Получите EXE и установщик

После загрузки откройте вкладку **Actions** → **Build Windows app**. Сборка запустится автоматически. Готовый архив будет доступен внизу страницы запуска в разделе **Artifacts**.

Если GitHub попросит разрешить Actions: **Settings** → **Actions** → **General** → **Allow all actions and reusable workflows**.

## 4. Опубликуйте релиз

Когда сборка прошла успешно, выполните:

```powershell
git tag v2.2.0
git push origin v2.2.0
```

Workflow автоматически создаст публичный GitHub Release и приложит:

- `MorseTrainer-Setup-x64.exe`;
- `MorseTrainer-Windows-x64.zip`.

Мобильная сборка находится в отдельном процессе **Actions → Build mobile apps**. Артефакт `MorseTrainer-Android` содержит APK. Сборка iPhone без подписи проверяется на симуляторе; порядок установки на настоящий iPhone описан в `MOBILE.md`.

При следующем релизе используйте новый тег, например `v2.1.5` или `v2.2.0`.

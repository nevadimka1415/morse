# Мобильная версия

Одна кодовая база .NET MAUI используется для Android и iPhone. В неё входят тренировка, карточки обучения, проверка на слух, темы, визуальный выбор символов, именованные профили и встроенные офлайн-напевы.

## Android

Готовый APK лежит в каждом релизе: откройте на телефоне страницу **Releases** репозитория, нажмите `MorseTrainer-Android.apk`, после загрузки откройте файл и подтвердите установку. Релиз создаётся автоматически при отправке тега вида `v2.2.1`.

Android может показать предупреждение об установке приложения не из Google Play. Разрешение нужно выдать только приложению, через которое открыт APK (обычно браузеру), а после установки его можно снова отключить.

Чтобы обновления приходили сами, поставьте Obtainium или опубликуйте приложение в RuStore: порядок в [docs/RUSTORE.md](docs/RUSTORE.md).

Пробные сборки без релиза лежат в **Actions → Build mobile apps** как артефакт `MorseTrainer-Android`: для скачивания нужен вход в GitHub, внутри zip с APK.

APK подписывается постоянным ключом из секретов репозитория (`ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASSWORD`), поэтому новые версии ставятся поверх старых. Если секреты не заданы, сборка подписывается временным debug-ключом, и обновление потребует удаления приложения. Сам keystore хранится у владельца проекта вне репозитория.

## iPhone

В каждом релизе лежит `MorseTrainer-iOS-unsigned.ipa` — сборка под настоящий iPhone без подписи Apple (GitHub дополнительно проверяет, что приложение собирается и для симулятора). App Store и TestFlight требуют платный Apple Developer Program, поэтому для своего iPhone используется бесплатный путь:

1. На компьютере (Windows или Mac) поставить [Sideloadly](https://sideloadly.io) или [AltStore](https://altstore.io).
2. Подключить iPhone кабелем, в программе указать свой Apple ID и файл `MorseTrainer-iOS-unsigned.ipa`.
3. Программа подпишет приложение вашим Apple ID и установит его. На iPhone: Настройки → Основные → VPN и управление устройством → доверять разработчику.

Ограничения бесплатной подписи Apple: приложение работает 7 дней, потом его нужно переподписать тем же способом (AltStore делает это сам, пока компьютер в одной сети с телефоном); одновременно не больше трёх таких приложений. Для установки другим людям без этих ограничений нужен Apple Developer Program и TestFlight.

## Локальная сборка

Весь проект собирается на .NET 10: мобильный (`net10.0-android`, `net10.0-ios`) и Windows-приложение. Нужен .NET SDK 10, а для Android ещё JDK 21 и Android SDK.

Android:

```powershell
dotnet workload install maui-android
dotnet publish .\src\MorseTrainer.Mobile\MorseTrainer.Mobile.csproj -f net10.0-android -c Release -p:MobileTargetFrameworks=net10.0-android -p:AndroidPackageFormats=apk
```

iPhone на Mac:

```bash
dotnet workload install maui-ios
dotnet build ./src/MorseTrainer.Mobile/MorseTrainer.Mobile.csproj -f net10.0-ios -c Release -p:MobileTargetFrameworks=net10.0-ios
```

Голосовые файлы уже находятся в проекте. Ни во время обучения, ни во время тренировки интернет не используется.

# Мобильная версия

Одна кодовая база .NET MAUI используется для Android и iPhone. В неё входят тренировка, карточки обучения, проверка на слух, темы, визуальный выбор символов, именованные профили и встроенные офлайн-напевы.

## Android

Готовый APK лежит в каждом релизе: откройте на телефоне страницу **Releases** репозитория, нажмите `MorseTrainer-Android.apk`, после загрузки откройте файл и подтвердите установку. Релиз создаётся автоматически при отправке тега вида `v2.2.1`.

Android может показать предупреждение об установке приложения не из Google Play. Разрешение нужно выдать только приложению, через которое открыт APK (обычно браузеру), а после установки его можно снова отключить.

Пробные сборки без релиза лежат в **Actions → Build mobile apps** как артефакт `MorseTrainer-Android`: для скачивания нужен вход в GitHub, внутри zip с APK.

APK подписывается постоянным ключом из секретов репозитория (`ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASSWORD`), поэтому новые версии ставятся поверх старых. Если секреты не заданы, сборка подписывается временным debug-ключом, и обновление потребует удаления приложения. Сам keystore хранится у владельца проекта вне репозитория.

## iPhone

GitHub автоматически проверяет, что iPhone-версия собирается для симулятора. Для установки на настоящий iPhone Apple требует:

- компьютер Mac с Xcode;
- Apple ID;
- подпись приложения личной или платной учётной записью разработчика.

Для разовой установки на собственный iPhone подходит бесплатная личная подпись Xcode, но её обычно приходится обновлять раз в семь дней. Для передачи другим людям нужен Apple Developer Program и распространение через TestFlight или App Store.

## Локальная сборка

Мобильный проект собирается на .NET 10 (`net10.0-android`, `net10.0-ios`): для него нужен .NET SDK 10, а для Android ещё JDK 21 и Android SDK. Windows-приложение при этом по-прежнему собирается на .NET SDK 8.

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

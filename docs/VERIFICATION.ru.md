# Проверка LocalControl 0.3.0-dev — 2026-10-06

## Выполнено

- TypeScript typecheck и Vite production build: exit 0, 72 modules.
- 35 browser contract checks compiled production bundle: exit 0. Ширины 1440/393/320, overflow, snapshot display, registered app launch+CSRF, audio API binding, themes, permission-disabled controls, power intent/cancel/confirm, clipboard без автоматического чтения, offline indicator, pairing nonce/fragment/approval wait.
- 21 C# files: grammar parse, 0 ERROR nodes. Это не Roslyn compilation, не typecheck и не подтверждение Windows API.
- Project XML: csproj/props/slnx/manifest разобраны без ошибок.
- Исходники расширены: apps/custom/close/restart/favorites, hardware, startup, media, power, transfers, clipboard, Remote v1, settings, signed updater/rollback и prerequisite Setup flow.

## Не выполнено

- .NET restore/build, C# test executable, Windows publish, NSIS compile.
- Готовый Setup.exe и portable binary НЕ получены.
- Windows/Core Audio/WinRT/COM/GDI/input реальное выполнение, iPhone Safari, clean install/uninstall, signed update rollback integration, длительный soak.
- Native file/folder picker, полное tray menu, Setup autostart↔settings sync; прочие детали — docs/TZ-STATUS.ru.md.

## Блокер

В Linux workspace отсутствуют dotnet/PowerShell/NSIS. Официальные .NET SDK/NuGet endpoints недоступны через сетевой proxy. GitHub подключён как kknoriakidev: доступные репозитории перечислены, LocalControl отсутствует. Создание repository через доступные connector tools не предоставлено. Другие проекты владельца не используются как временный runner без указания владельца. LocalCloud read-only.

Нужен отдельный репозиторий LocalControl с доступом connector и Actions: подготовленный Windows workflow выполнит сборку и отдаст development Setup/portable, после исправления возможных compile failures. Успешная сборка сама по себе не заменяет runtime/release QA.

## Состав исходного пакета

Core, Server, Windows, Desktop, Updater, React TS, production web bundle, fonts/licenses, locked npm dependencies, C# security/API tests, browser checks, build/installer/firewall/prerequisite/update-signing scripts, Windows Actions workflow, архитектура и честная матрица ТЗ. Нет private signing keys, npm/NuGet caches или фиктивного executable.

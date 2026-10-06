# LocalControl

Самостоятельное приложение для управления Windows 11 x64 с телефона в домашней сети. Не использует AI, облачный аккаунт или LocalCloud.

**0.3.0-dev — расширенные исходники приложения, не проверенный stable release.** Production web bundle собирается, браузерные проверки выполняются. .NET-компиляция и Windows runtime/Setup в текущей среде заблокированы: нет .NET SDK, его официальные загрузки недоступны. Готового Setup.exe в исходном архиве нет. Синтаксический разбор C# не заменяет компиляцию.

## Что находится в исходниках

- WinForms/WebView2, отдельный native loopback UI, tray, single instance, закрытие окна в tray, `--tray` после завершённой настройки.
- Отдельный LAN listener на выбранном приватном IPv4 и настраиваемом порту, по умолчанию выключен. Проверка Host/Origin/CSRF, bounded requests, без CORS.
- QR 120 с, nonce/claim secret, сверка шестизначного кода на ПК, atomic single-use claim, HttpOnly/SameSite cookie, сессия 30 минут, отзыв с прекращением соединений и активных запросов.
- Отдельные права status, audio, apps launch/close, power, media, transfer send/receive, clipboard read/write, remote view/input, configured hotkey. Ввод требует просмотра; опасные права по умолчанию выключены.
- Core Audio: output/mic gain и mute, sessions/system sounds. Discord output через аудиосессию; локально настраиваемые глобальные keybind Mute/Deafen. Состояние Discord неизвестно, injection нет.
- CPU/RAM/disks/uptime; Windows/user/CPU/GPU/board/power plan/adapters/displays metadata. Недоступные данные не синтезируются.
- Start Menu shortcuts, registry App Paths/uninstall, Steam libraries/manifests, custom exe, icon, args/cwd, favorites, поиск/фильтры, PID/running, graceful close, отдельное force close, restart, rescan.
- Windows GSMTC media sessions и play/pause/next/previous.
- Startup: HKCU/HKLM Run, пользовательская/общая Startup, boot/logon scheduled tasks. Изменение пользовательских Run/Startup с резервной копией; системные записи и задачи только читаются.
- Lock/sleep/hibernate/restart/shutdown с одноразовым 10-секундным подтверждением. Никаких произвольных shell-команд с телефона.
- Phone→PC upload до 1 ГБ, progress/cancel/retry, ограниченная очередь, безопасные имена, уникальная папка Downloads/LocalControl. PC→phone — явно указанный локальный файл, отдельное выбранное paired device, временная очередь.
- Clipboard text/URL по явному действию, отдельные read/write права.
- Remote v1: JPEG-снимок выбранного монитора, click/right click/scroll/text/special keys. Защищённый desktop/UAC не обходится. Видеострим не реализован.
- Desktop/mobile React UI, dark/light/system, local fonts, ошибки, SignalR + fallback polling; настройки имени, порта, transfer directory и автозапуска, первый запуск.
- NSIS RU/EN setup source: WebView2 prerequisite с согласием, проверкой Microsoft Authenticode, shortcuts/autostart/uninstall. Self-contained win-x64 portable build script.
- Updater source: pinned ECDSA manifest + SHA256, идентификация собственного parent process, handshake перед выходом, отдельный runner, safe ZIP paths, owned-files backup, health check, rollback. Настройки/устройства не перезаписываются.

Реализация в исходниках не доказывает работу Windows API на устройстве. Подробная матрица ТЗ — `docs/TZ-STATUS.ru.md`; обязательная проверка — `docs/RELEASE-CHECKLIST.ru.md`.

## Сборка Windows

.NET 10 SDK, Node.js 24 LTS, PowerShell 7, NSIS 3.x. Из корня:

```powershell
pwsh -File ./scripts/build.ps1 -Installer
```

Скрипт: npm ci/build → dotnet restore/build → Core/API/security tests → self-contained desktop/updater publish → portable ZIP → NSIS Setup → SHA256. Остановка при любой ошибке. Артефакты: `artifacts/LocalControl-0.3.0-dev-win-x64.zip`, `artifacts/LocalControl-0.3.0-dev-Setup.exe`. Эти файлы создаются только после успешной Windows-сборки.

`.github/workflows/build.yml` выполняет сборку и browser checks на Windows runner, загружает development artifacts. Его нужно поместить в отдельный репозиторий LocalControl; stable публикация требует ручной проверки точных пакетов.

```powershell
cd src/LocalControl.Web
npx playwright install chromium
npm test
```

## Подписанные обновления

Перед первым доверенным publish инициализировать ключ **на Windows машине издателя**:

```powershell
pwsh -File ./scripts/sign-update.ps1 -InitializeKey
pwsh -File ./scripts/build.ps1 -Installer
pwsh -File ./scripts/sign-update.ps1 -Archive ./artifacts/LocalControl-0.3.0-dev-win-x64.zip -Version 0.3.0
```

Публичный `build-config/publisher-public.pem` включается в publish и может храниться в Git. Закрытый ключ хранится вне проекта, DPAPI CurrentUser, не помещать его в архив/GitHub. Без pinned public key updater отказывается применять обновление. Нельзя генерировать новый ключ для каждой версии. DPAPI ключ привязан к учётной записи издателя; резервное копирование и восстановление ключа до релиза требуют отдельной процедуры.

Это подпись update package, а не Authenticode подпись Setup. Authenticode/SmartScreen стратегия ещё не настроена. Автоматического release channel/network updater нет; manifest и ZIP выбираются локально в native UI.

## Запуск и подключение

Запуск обычным Windows пользователем; WebView2 Evergreen Runtime обязателен для native UI. Setup source предлагает загрузить его с Microsoft только при отсутствии и с согласия. Офлайн-пакет Runtime ещё не вложен.

Завершить первый запуск → «Подключение» → выбрать домашний IPv4 → LAN → QR → запрос с телефона → сверить код и разрешить права на ПК. HTTP/IP работает без облака и без PWA. HTTP не шифрует данные и не защищает от активного MITM в LAN; только доверенная домашняя сеть. Restart выключает LAN и требует нового подтверждения сессии.

Брандмауэр: helper рядом с installed exe, только Private/LocalSubnet. Для него отдельно нужен elevated PowerShell; приложение запускается без elevation:

```powershell
./firewall.ps1 -Port 41017
./firewall.ps1 -Remove
```

После изменения порта правило нужно обновить с новым `-Port`.

Данные `%LOCALAPPDATA%\LocalControl`: settings/devices/apps/hotkeys JSON, startup backups, transfer temp, update backup, WebView2 profile. Секреты и сессии в памяти. JSON хранится атомарно; SQLite migration нет. Удаление сохраняет пользовательские данные и удаляет только installer-owned файлы.

## Ограничения до полного релиза

Нужны компиляция C#, Windows/iPhone QA, установочная проверка, signed-update/rollback integration tests и длительный тест устойчивости. Native file/folder picker, полное tray меню и синхронизация autostart option Setup↔first-run settings ещё требуют доведения. Store discovery, default audio switching, artwork/sensors при наличии, HTTPS/PWA и low-latency video — отдельные дальнейшие возможности. Не обещать PWA на обычном HTTP/IP.

LocalCloud read-only, его репозиторий не изменяется. Дизайн: https://www.figma.com/design/ei6PqETHMPsUEkj1ehmRBr. Собственная лицензия продукта определяется владельцем; сторонние notices — `THIRD_PARTY_NOTICES.md`, `LICENSES/`.

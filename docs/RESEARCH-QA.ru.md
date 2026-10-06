# Исследование и QA первого этапа

Проверено 6 октября 2026. Это запись выполненной работы, отдельно от плана Windows QA.

## LocalCloud reference

Репозиторий читался через GitHub **без изменений**. Снимок `main`: `5dcae3660c399ad8b0ff80795cbdf53e19e122d4`.

Прочитаны:

- `LocalCloud/src/LocalCloud.Desktop/Program.cs`: WinForms tray, self-owned server process, named pipe `CurrentUserOnly`, first run, startup, graceful stop timeout.
- `LocalCloud/src/LocalCloud.Desktop/MainWindow.cs`: WebView2 userDataFolder в LocalAppData, exact-origin navigation, browser fallback, hide-to-tray.
- `LocalCloud/scripts/Update-LocalCloud.ps1`: install selection, manifest SHA-256 проверки updater components, rollback invocation.
- `LocalCloud/installer/LocalCloud.nsi`: NSIS, per-user install, RU/EN, optional WebView2, uninstall.

Использованы паттерны lifecycle и разделения данных/программы. Код LocalCloud не скопирован. Наличие класса с именем `LocalControl` внутри LocalCloud — внутренняя control IPC helper старого проекта, не зависимость нового продукта. Для нового LocalControl выбран in-process Kestrel host, чтобы сократить проблемы отдельных owned процессов. Отдельный updater остаётся необходим.

Новый GitHub репозиторий/PR/релиз в этом этапе не создавался. Пакет готов для локального review; публикация полноценной версии — после backend и Windows QA.

## Primary sources

| Источник | Что проверяли |
|---|---|
| [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy) | .NET 10 — актуальная LTS основа |
| [Core Audio interfaces](https://learn.microsoft.com/en-us/windows/win32/coreaudio/core-audio-interfaces) | Sessions, endpoints, notifications |
| [ISimpleAudioVolume](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nn-audioclient-isimpleaudiovolume) | Shared-mode session volume/mute |
| [IAudioEndpointVolume](https://learn.microsoft.com/en-us/windows/win32/api/endpointvolume/nn-endpointvolume-iaudioendpointvolume) | Render/capture endpoint volume/mute |
| [Session notification](https://learn.microsoft.com/en-us/windows/win32/api/audiopolicy/nf-audiopolicy-iaudiosessionmanager2-registersessionnotification) | Event subscription и COM worker lifetime |
| [Discord RPC](https://docs.discord.com/developers/topics/rpc) | Auth, restrictions, GET/SET voice settings, events; API существует, доступ не гарантирован |
| [SendInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput) | Typed input и ограничения UIPI |
| [WebView2 security](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/security) | Origin, navigation, web messages, host objects |
| [SignalR security](https://learn.microsoft.com/en-us/aspnet/core/signalr/security) | CORS != WebSocket Origin defense |
| [SignalR authentication](https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz) | Browser cookie auth |
| [Secure contexts](https://developer.mozilla.org/en-US/docs/Web/Security/Defenses/Secure_Contexts) | Private LAN HTTP IP не equivalent localhost secure context |
| [Application registration](https://learn.microsoft.com/en-us/windows/win32/shell/app-registration) | App Paths, правильная launch metadata |
| [IApplicationActivationManager](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-iapplicationactivationmanager) | Store app activation через AUMID |
| [Run/RunOnce](https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys) | Startup sources и ограничения |
| [Media sessions](https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssessionmanager) | Windows media session manager |
| [Desktop Duplication](https://learn.microsoft.com/en-us/windows/win32/direct3ddxgi/desktop-dup-api) | Будущий capture pipeline, не уже готовый WebRTC |
| [System shutdown functions](https://learn.microsoft.com/en-us/windows/win32/shutdown/system-shutdown-functions) | Lock/shutdown/restart |
| [GetSystemTimes](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getsystemtimes) | Системная CPU метрика |
| [LibreHardwareMonitor license](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/master/LICENSE) | MPL-2.0 для возможного sensor provider Later |
| [Manrope OFL](https://github.com/google/fonts/blob/main/ofl/manrope/OFL.txt) / [Space Grotesk OFL](https://github.com/google/fonts/blob/main/ofl/spacegrotesk/OFL.txt) | Embedded local fonts и сохранение notices |

Context7 использован для ASP.NET Core/SignalR authentication и WebView2 security; выбор архитектуры — наш вывод из требований, а не обещание Microsoft. Steam manifest parsing — клиентский формат, не стабильный публичный Windows API: нужны tolerant parser, fixtures и fallback при изменении формата. Artwork только из локального cache, без обязательных внешних downloads.

## Что прошло в текущем пакете

- JavaScript syntax check в Node.
- 27 model/DOM checks с LinkeDOM: navigation, session slider, mute/Windows mic, поиск/empty state, HTML escaping custom names, light theme, pairing/revoke, power confirmation/cancel, blocked remote input, explicit clipboard и все страницы. После финальной правки quick launch выполнен повторный прогон.
- Шрифты проверены fontTools: Manrope содержит кириллицу и веса 200–800; Space Grotesk 300–700 использован для Latin/numerals, кириллица UI — Manrope.
- Figma: 8 editable screen frames, четыре component families, 31 vector icon component, 58 tokens. Внутри screen frames нет raster-filled UI; проверены реальные Manrope/Space Grotesk text nodes. Навигация связана между существующими экранами.
- Визуальная проверка всех восьми Figma экранов; исправлены auto-layout heights, подписи метрик и контраст light-theme иконок. Точные screenshots находятся в `design`.

## Что ещё не прошло

**Rendered HTML browser smoke не выполнен:** в среде не было Chromium; установка Playwright browser завершилась ошибкой загрузки. `scripts/check-prototype.cjs` сохранён как воспроизводимый browser test, но его 60 responsive checks **не заявляются прошедшими**. LinkeDOM не проверяет layout, drag/touch, font rendering, native file picker или Safari API. Figma макеты и HTML имеют общую визуальную систему, однако pixel-perfect browser comparison пока не проведён.

Windows integration, physical iPhone Safari, WebView2, installer, firewall, token security, power API, audio device unplug/replug, updater rollback не тестировались: этих implementation modules в v0.1 нет. Не выдавать дизайн-пакет за готовый 1.0 release.

## Обязательный release QA для 1.0

| Группа | Acceptance |
|---|---|
| Pairing/security | Reject/expiry/replay, no anonymous state/input, separate listener routes, CSRF/Origin, websocket revoke, no permanent HTTP credential |
| Audio | Несколько Discord sessions, Windows external volume changes, endpoint unplug/default device change, fast slider coalescing, COM cleanup |
| Process/discovery | Unicode/space paths, shortcut launch, Store AUMID, Steam libraries, PID reuse, close timeout, explicit force close |
| Mobile | Safari HTTP iPhone on same LAN, safe area, portrait/landscape, Home Screen behavior, disabled APIs graceful fallback |
| Remote | Permission denied, monitor removed, scaling/DPI/multi-monitor, key release on disconnect, lock/UAC limitation |
| Transfers | Filenames/path traversal/reparse, quotas/disk full, cancel, retry, queued download ACL and TTL |
| Lifecycle | Close vs Exit, server pause/restart, no orphan processes, repeated startup, port conflict |
| Setup/updater | Clean Windows, missing WebView2, RU/EN, no internet after prerequisites, upgrade preserving credentials/custom apps, failed migration rollback, uninstall retains user files |

Сначала вертикальный срез Audio + pairing на реальном Windows/iPhone, затем расширение функций. Полный streaming не блокирует выпуск качественного Remote snapshot v1.

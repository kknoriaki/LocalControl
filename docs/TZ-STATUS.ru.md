# Состояние полного ТЗ — 0.3.0-dev, 6 октября 2026

«Исходники» означает код и API/UI wiring. Windows execution не проверено: SDK/runner недоступен. До runtime QA ни одна native функция не обозначается как стабильная.

| Часть ТЗ | Состояние исходников | Что ещё требуется |
|---|---|---|
| Self-contained Windows app, WebView2, tray | Написано, --tray поддержан | .NET compile/publish, clean launch, lifecycle; расширить tray меню |
| LAN/direct IP/mobile UI | Написано, HTTP, LAN выключен по умолчанию | Windows+iPhone Safari, firewall/reconnect |
| Pairing, permissions, revoke, CSRF/Host/Origin | Написано, новые права добавлены | Запуск C#/HTTP security suite; transport HTTP limitation |
| Dashboard CPU/RAM/disks/uptime | Native adapters написаны | Сравнить с Windows, idle/8h soak |
| Hardware Windows/user/CPU/GPU/board/net/displays | WMI/native metadata написаны | WMI/device availability; sensors optional отсутствуют |
| Apps LNK/AppPaths/uninstall/Steam/custom | Написано, launch/close/restart/favorites/icons/PIDs | Window/child-process races, unknown inaccessible processes; native exe picker и rename UI |
| Audio master/mic/sessions, Discord output | NAudio adapter написан | Hardware hotplug, real mixer consistency |
| Discord mute/deafen keybind | Fixed local configuration, state unknown | Проверить комбинации на Discord; не injection |
| Media playback | Windows GSMTC adapter написан | Реальные media apps; artwork пока нет |
| Startup | User Run/Startup edits+backup; other sources readonly | Registry/COM/tasks smoke; восстановление backup |
| Power | Fixed actions + one-time confirmation | Реальные lock/suspend/hibernate/shutdown capabilities |
| Phone→PC transfer | 1GB bounded upload, cancel/retry/progress | iPhone browser uploads, disk/cancel/revoke races |
| PC→phone transfer | Explicit local path, selected trusted device, temporary queue | Native file picker, Safari attachment download |
| Clipboard text/URL | Separate explicit read/write | Windows clipboard concurrency, UTF text |
| Remote v1 | Monitor JPEG snapshots + typed input | Multi-monitor/DPI/negative coordinates, locked/UAC, input cancellation |
| Themes/mobile/settings/first run | Web components wired | Реальное safe-area/touch/Safari; Setup autostart sync |
| Setup RU/EN, shortcut/autostart/WebView2/uninstall | NSIS and prerequisite scripts written | Compile NSIS; install/update/uninstall; offline Runtime bundle absent |
| Signed updater/backup/rollback | ECDSA/owned files/parent identity/health check written | Owner key initialization, integration rollback tests; no release channel |
| Sources | Source package assembled | Separate connected GitHub repo still required |
| Setup.exe + portable binary | НЕ собраны | .NET SDK + Windows runner + NSIS execution |
| Stable release | НЕ готов | Exact-binary Windows+iPhone validation and remaining UI/native integration |
| Later/optional | Store discovery, default audio switch, sensors/artwork, custom buttons, HTTPS/PWA, video | Не заявлять реализованными |

Полная работа ещё не завершена: исходники расширены, executable поставка и подтверждение всех обязательных сценариев остаются открытыми.

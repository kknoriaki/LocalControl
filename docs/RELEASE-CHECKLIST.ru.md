# Ворота стабильного релиза LocalControl

0.3.0-dev — расширенный реализованный срез, не готовность всей спецификации 1.0. Не переименовывать source archive или непроверенный publish в stable.

## Автоматические проверки

- [x] Production UI TypeScript typecheck + Vite build в текущей среде.
- [x] 35 browser contract checks compiled UI: desktop 1440, mobile 393/320, отсутствие горизонтального overflow, API launch ID/CSRF, audio volume/mute, тема, status-only permissions, pairing request/nonce/fragment/approval wait.
- [x] Синтаксический разбор 21 C# файлов без parser errors; это **не компиляция**.
- [ ] .NET restore/build всей solution на Windows 11 x64.
- [ ] Запуск `LocalControl.Tests`: истечение invite/session, single-use concurrent claim, permission denial, logout/revoke abort, persistence без credentials, Host/Origin/CSRF и actual HTTP API.
- [ ] Actual LAN tests, без SKIP private-interface test.
- [ ] Self-contained publish, offline запуск без установленного .NET SDK/runtime.
- [ ] NSIS compilation + чистая установка RU/EN + uninstall/reinstall + сохранение данных.

## Windows smoke и устойчивость

- [ ] First launch с WebView2 и сценарий отсутствующего WebView2, корректное сообщение/prerequisite flow.
- [ ] Close → tray, show из tray, один экземпляр, Exit освобождает оба listening ports, повторный старт.
- [ ] Exit во время startup и при открытом телефонном соединении, OS logoff/shutdown.
- [ ] USB/headset unplug/replug, default device change, аудиосессия завершилась во время чтения/записи, Windows Audio service restart.
- [ ] Master/output, mic gain/mute, system sounds и несколько sessions одного приложения: сравнить с Windows mixer. Discord mic state не выводится как известный.
- [ ] CPU/RAM/disk/uptime соответствуют Windows; первый CPU sample может быть неизвестен.
- [ ] Catalog/launch для обычного shortcut, отсутствующего exe, Steam с несколькими libraries; неизвестный ID не запускает ничего.
- [ ] Длительный тест 8 часов: CPU idle, память/COM handles не растут, очередь не зависает, UI остаётся отзывчивым.
- [ ] IPv4 adapter disappears/change IP/port conflict, firewall Private/Public, LAN off и restart приложения завершают сессии.

## iPhone / Safari

- [ ] Прямой `http://IP:41017` в домашней сети, QR без облачной зависимости.
- [ ] Pairing code совпадает, reject/expiry дают понятный результат, approve один раз, replay secret запрещён.
- [ ] Проверка каждого сочетания grants: каждого права, включая clipboard, transfer, remote view/input и power. Отказ сервера при ручном запросе, а не только disabled UI.
- [ ] Сессия истекает через 30 минут; logout/revoke закрывает SignalR и запрещает следующий REST command.
- [ ] Safari foreground/background/reconnect после Wi-Fi interruption; ошибочная команда не повторяется автоматически.
- [ ] Touch sliders на реальном Safari, volume/mute ↔ Windows state, safe-area/orientation/клавиатура телефона.

## До stable версии полной спецификации

- [ ] Все согласованные обязательные функции 1.0 реализованы и проверены; текущий срез не подменяет всю спецификацию.
- [ ] Доверенный HTTPS transport и certificate setup, если предлагается hardened режим/PWA.
- [ ] Подписанный manifest/update binary, owned-process stop, binary/data backup и rollback на несовместимости. Hash сам по себе не устанавливает подлинность.
- [ ] Название/версия, собственная license policy, dependency notices, SBOM, checksums, code signing/SmartScreen strategy и release notes.
- [ ] Installer-owned file deletion, update overwrite, settings schema migration, отсутствие удаления чужих/пользовательских файлов.
- [ ] Финальное тестирование **точных байтов** Setup/portable artifact, которые будут опубликованы.

Автоматически подготовленный SHA256 в build.ps1 используется для проверки целостности; он не является подписью, доверенным updater manifest или доказательством runtime QA.

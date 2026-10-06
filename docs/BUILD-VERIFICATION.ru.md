> Исторический отчёт для 0.2/0.3.0. Актуальные изменения и проверки: README.md, CHANGELOG.md, scripts/check-windows.ps1 и Windows QA artifacts.

# Проверка LocalControl 0.2.0-dev

Дата: 6 октября 2026. Среда проверки: Linux, Node.js 24.19.0, Chromium 153.0.8010.0.

| Проверка | Результат | Что подтверждает |
|---|---|---|
| `npm run build` | PASS | TypeScript typecheck, production Vite bundle, local fonts |
| `npm test` с Chromium | PASS, 27 проверок | Контракты production UI и responsive layout |
| C# синтаксический parser | PASS, 10 файлов | Нет ошибок grammar parser; не проверяет типы/ссылки/NuGet |
| .NET restore/build/publish | НЕ ЗАПУСКАЛОСЬ | SDK отсутствует; официальный SDK download недоступен из этой среды |
| Core/API security checks | НАПИСАНЫ, НЕ ЗАПУСКАЛИСЬ | Запускаются build.ps1/GitHub Windows workflow |
| Tray/WebView2/Core Audio/Steam на Windows | НЕ ПРОВЕРЕНО | Требуется Windows 11 x64 |
| NSIS compile/install/uninstall | НЕ ПРОВЕРЕНО | Script подготовлен, Setup executable не выдаётся |
| iPhone/Safari на реальном LAN | НЕ ПРОВЕРЕНО | Требуется физический Windows-PC и iPhone |
| GitHub Actions | НЕ ЗАПУСКАЛОСЬ | Workflow подготовлен; отдельный репозиторий пока не создан |

Браузерный тест работает с **собранным production bundle** и перехватывает API ответами из QA fixtures. Это проверяет реальные JS/React bindings, HTTP paths/methods/payloads/CSRF, отображение snapshot, permissions и pairing UI. Fixtures находятся только в `scripts/check-web.mjs`; production UI не генерирует аппаратные показатели и не содержит mock API. Эти тесты не подтверждают чтение оборудования или работу Core Audio.

На ширинах 1440/393/320 проверено: отсутствие горизонтального overflow, CPU snapshot rendering, launch registered ID + CSRF, keyboard slider → numeric volume, microphone endpoint mute, theme switch, отсутствие uncaught UI errors. Отдельно: у телефона нет admin navigation, status-only permission запрещает audio edit, pairing передаёт invite и случайный 256-bit nonce, удаляет fragment из browser history и ждёт подтверждения.

Windows build pipeline не публикует stable release автоматически. Итоговый release требует checklist из `RELEASE-CHECKLIST.ru.md`; успешный TypeScript build не является доказательством стабильности Windows-программы.

После исправления обработки разрыва связи добавлена 27-я проверка: offline снимает online indicator; старые данные не показываются как текущие. Финальные исходники повторно прошли syntax parser, а XML solution/projects/manifest — XML validation.

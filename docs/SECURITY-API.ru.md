# Pairing, безопасность, данные и API

Целевой security/API plan полной версии. В 0.2.0-dev backend/auth реализованы для status/audio/launch и desktop enrollment; прочие маршруты из таблицы ещё не регистрируются. Реальные текущие маршруты смотрите в `src/LocalControl.Server/ControlHost.cs`. HTTPS/refresh/SQLite/remote ещё впереди. HTTP session expiry/Origin/Host/CSRF/grants/revoke уже заложены в исходники, но .NET/Windows runtime checks в этой среде не запускались.

## Модель угроз и транспорт

LAN не является authentication. По умолчанию доступ выключен до first run consent, firewall rule только Private profile + LocalSubnet. Не делать port forwarding, UPnP или bind на public interface по умолчанию. Peer должен принадлежать выбранной локальной сети (IPv4/IPv6), но IP не является идентификатором trusted device. Host/Origin сверяются с точным allowlist реально показанных адресов/портов. Нельзя принимать произвольные forwarded headers.

HTTP по LAN поддерживается ради прямого Safari `http://192.168.x.x:41017`. **Pairing и токены поверх HTTP не дают шифрования или защиты от атакующего внутри сети.** Это нельзя исправить CSRF, PIN или SameSite. UI при включении LAN показывает выбранный transport. Production hardened режим — HTTPS с локальным сертификатом и явным доверием на телефоне; самоподписанный сертификат без trust не превращает Safari в надёжный secure context. Не обещаем бесшовный HTTPS/PWA без установки доверия.

HTTP-compatible режим ограничивает session TTL, не выдаёт долгоживущий remember-me cookie; после expiry нужна повторная approval. Remote input, clipboard read и power по умолчанию выключены. Пользователь может включить их локально для конкретного телефона в доверенной домашней сети с понятным сообщением о HTTP. HTTPS режим допускает remember-device credential с rotation. Не заявляем, что HTTP режим выдерживает активный MITM. Локальные malware того же Windows user и скомпрометированный PC вне границы защиты этого utility.

## Pairing

1. Только native Desktop через authenticated loopback endpoint включает окно pairing на 120 s. QR содержит адрес и краткоживущий enrollment invite в URL fragment, без постоянных credentials. Fragment не попадает в server access logs. UI удаляет fragment после передачи invite по POST.
2. Safari отправляет `POST /api/v1/pairing/requests`: invite, browser label и случайный client nonce. Сервер генерирует request id, одноразовый claim secret 256 bit и случайный verification code. Применяет rate limits, хранит hash secret, не пишет его в logs. На телефоне и ПК показывается одинаковый код, имя/браузер/IP только подсказки, не достоверная identity.
3. Desktop approval показывает permissions. Пользователь сверяет код и явно Approve/Reject. Не autoapprove по Wi-Fi, имени iPhone или старому IP. Request привязан к invite и client nonce, после use/expiry недействителен.
4. Телефон polls **только свой** claim secret или scoped pending channel. Approval завершается атомарным single-use claim и выдаёт opaque short session. Secret никогда не лежит в query string.
5. Cookie HttpOnly, SameSite=Strict, Path=/, host-only, Secure в HTTPS. HTTP-cookie имеет более короткий TTL и не может иметь Secure. В БД — hash credential, device id и expiry/revocation generation. Cookies/tokens удалены из logs и exception details.
6. HTTPS remember credential rotated атомарно; reuse старого refresh revoke family. HTTP не хранит durable credential. Device settings остаются после upgrade, но HTTP active sessions не обязаны переживать restart. Revoke немедленно закрывает SignalR/remote, удаляет queued device commands и не ждёт cookie expiry.

Pairing invites не являются authorization на действия. Anonymous получает только статический shell, pairing endpoints и минимальный health; hostname/hardware/username, apps и capture требуют authentication. Только desktop может Approve, grant Remote, add custom exe/action, change LAN listeners или startup. Нельзя доверять `role=desktop` из JS, query, IP или client header.

## Permissions

| Permission | Действия | Default при pairing |
|---|---|---|
| `status.read` | Ограниченный status, нагрузки | On |
| `audio.control` | Volume/mute endpoints и apps | On |
| `apps.launch` | Только зарегистрированные app IDs | On |
| `apps.close` | Close/restart зарегистрированных apps | Off |
| `media.control` | Windows media sessions | On |
| `transfer.send` | Upload в approved folder | On |
| `transfer.receive` | Получить явно поставленные в очередь файлы | Off |
| `clipboard.read/write` | Explicit actions text/URL | Off |
| `power.control` | Lock/sleep/restart/shutdown | Off |
| `remote.view` | Снимок/будущий video | Off |
| `remote.input` | Click/scroll/keyboard | Off, требует remote.view |
| `custom.invoke` | Только locally registered action ID | Off |

Permissions проверяются и в REST и в каждом Hub method, включая уже установленные sockets. Remote ticket expiring, monitor identity, координаты/кнопки/длина текста валидируются. Все held keys отпускаются при disconnect, revoke, timeout. Lock/UAC/secure desktop не обходятся.

## API map

Base `/api/v1`; production UI same-origin. Loopback admin routes физически не мапятся на LAN listener. DTO не содержит executable paths в публичных commands.

| Метод / маршрут | Назначение | Permission |
|---|---|---|
| `GET /health` | Минимальная readiness без идентификации PC | Anonymous |
| `POST /pairing/requests` | Создать request в открытом pairing window | invite + limits |
| `POST /pairing/claim` | Atomic exchange claim secret → cookie | One-time secret |
| `POST /session/logout`, `/session/refresh` | Logout/HTTPS rotation | Session + CSRF |
| `GET /state` | Snapshot + revision/capabilities | status.read |
| `GET /apps` | Discovered каталог безопасных IDs | apps.launch |
| `POST /apps/{id}/launch`, `/close`, `/restart` | Typed registered app operation | apps.launch / apps.close |
| `PUT /audio/endpoints/{id}` | `{volume,muted,expectedRevision}` | audio.control |
| `PUT /audio/groups/{id}` | Apply к sessions app group | audio.control |
| `POST /media/{id}/{action}` | allowlist play/pause/previous/next | media.control |
| `POST /power/intents` | 10 s action/device-bound confirmation nonce | power.control |
| `POST /power/confirm` | Single-use intent, no arbitrary action replacement | power.control |
| `POST /transfers` | Upload init: sanitized name, length, MIME hint | transfer.send |
| `PUT /transfers/{id}/content` | Streaming bytes, bounded total/count | transfer.send |
| `DELETE /transfers/{id}` | Cancel own temp upload | transfer.send |
| `GET /transfers/{id}/download` | Owner/device bound queue item, TTL | transfer.receive |
| `POST /clipboard/read`, `/clipboard/write` | Explicit text exchange, length cap | clipboard.read/write |
| `POST /remote/sessions` | Monitor + capability → expiring session | remote.view |
| `GET /remote/sessions/{id}/frame` | Bounded JPEG, no-store | remote.view |
| `POST /remote/sessions/{id}/input` | Typed mouse/key/text, rate caps | remote.input |
| `DELETE /remote/sessions/{id}` | Stop + release keys | remote.view |
| `POST /actions/{id}/invoke` | Approved definition, no mobile arguments | custom.invoke |
| `POST /desktop/pairing/open`, `/approve`, `/reject` | Enrollment | Native desktop only |
| `GET /desktop/devices`; `PUT/DELETE /desktop/devices/{id}` | Permissions/revoke | Native desktop only |
| `POST /desktop/apps/custom`, `/rescan` | Registered targets/scan | Native desktop only |
| `PUT /desktop/startup/{id}` | Safe supported source only | Native desktop only |
| `PUT /desktop/settings`, `/network`; `POST /desktop/updates` | Config/lifecycle/update | Native desktop only |
| `/hubs/state` | Authenticated SignalR, subscriptions/deltas | Per-topic permission |

Responses: ProblemDetails with stable code + понятное сообщение + diagnostic reference; sensitive paths/usernames only on authorised diagnostic views. Commands имеют correlation id, revision и deadline. Dangerous commands single-use, не повторяются при automatic retry. Rate limits: pairing global/per-peer; input/slider/session byte quotas отдельно. No raw shell, PowerShell, dynamic exe/PID/URL endpoint.

## Browser defense

Antiforgery token + exact Origin на всех mutation routes. SameSite не заменяет antiforgery; GET не меняет состояние. WebSocket Origin проверяется отдельно: CORS не защищает upgrade. Не разрешать `*` CORS с credentials. Bootstrap status не сообщает секреты. CSP: локальные scripts/styles/fonts, no remote CDNs, no eval, restrictive object/frame/base/form. WebView2 navigation allowlist exact authority; validate native web messages и запретить host object для untrusted origins. File names/app labels/messages выводятся text nodes, не `innerHTML` из данных.

Uploads: sanitize basename, server-generated filename, containment+reparse checks, no auto execute, streaming/temp `.part`, per-file/per-device quotas, свободное место, cancel cleanup. Downloads — explicit queue allowlist, без arbitrary path query, Content-Disposition attachment, expiring device scope. Папку/имя/аргументы/working directory может задавать только Desktop.

## Данные

`%LOCALAPPDATA%\LocalControl\`: `settings.json`, `state.sqlite`, `keys/`, `logs/`, `cache/icons/`, `transfer-temp/`, `update-state/`, `WebView2/`. Data folder ACL current user; DPAPI CurrentUser для secret material, server signing keys вне Web bundle. Credentials — hashes, master keys DPAPI; никогда не полные токены в БД/logs. SQLite migration schema version separate от app version, WAL consistent backup.

| Таблица | Основные поля |
|---|---|
| Devices | id, label, permissions, approvedAt, lastSeenAt, revokedAt, authGeneration |
| Credentials | hashedToken, deviceId, familyId, expiry, consumedAt, transport |
| Apps | id, targetType, canonicalTarget, identity, arguments, cwd, source, favorite, customName |
| Actions | id, actionType, trustedTargetId, immutable locally configured definition |
| StartupBackups | id, source, key/path, originalType/value, disabledAt, compareBeforeRestore |
| Transfers | id, ownerDeviceId, direction, safePath, length, status, expiry |
| Audit | time, deviceId, actionKind, targetId, result; no clipboard/file contents |
| SchemaMigrations | version, appliedAt |

Hardware/audio/process live state не спамит DB: хранится в памяти. Browser stores theme/preferences; не permanent LAN credential в localStorage. Backup files не доступны mobile API. Pairing pending data lives in memory with expiry. Uninstall предлагает отдельно удалить local settings/credentials, не удаляет Downloads/transfer files по умолчанию.

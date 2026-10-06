# LocalControl — визуальная система

Направление: сдержанная desktop utility. Тонкие нейтральные границы, плотность рабочих списков, один тёплый accent. Мобильный UI имеет собственную компоновку и нижнюю навигацию.

## Tokens

| Role | Dark | Light |
|---|---|---|
| Background | #0E0F11 | #F2F3F4 |
| Surface | #15171A | #FAFAFA |
| Raised | #1B1E22 | #FFFFFF |
| Border | #2B2F34 | #D8DCDF |
| Primary text | #E9EAEC | #25292D |
| Secondary text | #92989F | #626B73 |
| Accent | #D6AE78 | #8D612E |
| Active surface | #29251F | #EDE5DA |
| Success | #8DB39B | #426E52 |
| Danger | #D49595 | #AE5353 |

Radius: 6 px buttons, 8 px icons/controls, 10 px panels. Spacing: 4/8/12/16/20/24/32. Desktop sidebar 216 px, toolbar 70 px; phone reference 393 × 852 с safe-area padding в HTML. Touch controls увеличиваются на mobile. В production минимальные interactive targets 44 px, включая микшер и overflow controls.

Typography: Manrope 400/500/600 для русскоязычного UI. Space Grotesk Regular для numerals/Latin identity; не заставлять им рисовать кириллицу. Файлы локальные, SIL OFL notices в LICENSES. Манипуляции 160–200 ms, reduced-motion support. Theme System отслеживает preference браузера; выбранная тема сохраняется локально.

## Экраны

1. Desktop Dashboard — metrics, CPU graph, favorites, power, audio, LAN.
2. Mobile Home — identity, compact metrics, favorites, mic/master/media, Lock.
3. Mobile Audio Mixer — app sessions и slider per session.
4. Mobile Applications — search, categories, running state/action.
5. Mobile Remote — snapshot preview, touchpad и keyboard.
6. Desktop Applications — dense list и Steam reference.
7. Desktop Audio — sessions + devices/mic/Discord shortcut.
8. Desktop Settings & Pairing — Light, network, permissions/trusted devices.

Figma: https://www.figma.com/design/ei6PqETHMPsUEkj1ehmRBr

Все UI elements — editable text, vectors, auto-layout frames и instances. Button/NavItem/SessionRow/AppRow — reusable component sets с theme и state properties. Colors/spacing привязаны к variables, text к shared styles. Starter plan допускает один mode в collection, поэтому Dark/Light semantic collections разделены и alias primitives; не требуется paid variable mode feature.

HTML prototype показывает дополнительные Startup/Hardware/Devices/Transfers screens и реальную browser-side demo state. Figma задаёт визуальное основание; это не Code Connect mapping и не пиксельная копия browser rendering. Сложные native operations в prototype явно симулируются. Fake QR не используется: место QR помечено «появится с backend».

## Review status

Пройдены Figma structural/font checks и visual review. Для HTML выполнены state/DOM и syntax checks. Browser layout и physical iPhone ещё нужно проверить; до этого не утверждаем идеальную адаптацию на всех устройствах.

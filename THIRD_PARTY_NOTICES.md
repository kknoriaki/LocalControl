# Third-party notices

Production dependencies are pinned in npm package-lock.json and .csproj files. The source archive does not bundle npm/NuGet caches. A Windows publish includes .NET self-contained binaries and WebView2 SDK loader; WebView2 Evergreen Runtime is a separate Microsoft installation. Before public release, retain the resolved publish license inventory and SDK license terms.

| Component | Source / license | Notice |
|---|---|---|
| Manrope variable TTF | Google Fonts / SIL OFL 1.1 | LICENSES/Manrope-OFL.txt |
| Space Grotesk variable TTF | Google Fonts / SIL OFL 1.1 | LICENSES/SpaceGrotesk-OFL.txt |
| NAudio.Wasapi + NAudio.Core 2.2.1 | https://github.com/naudio/NAudio/tree/v2.2.1 / MIT | LICENSES/NAudio-MIT.txt |
| .NET 10 / ASP.NET Core | Microsoft + .NET Foundation / MIT and distribution notices | https://github.com/dotnet/runtime/blob/main/LICENSE.TXT |
| Microsoft.Web.WebView2 1.0.3537.50 | Microsoft SDK license terms | https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.3537.50/License |
| System.Management 10.0.0 | .NET runtime / MIT | https://github.com/dotnet/runtime/blob/main/LICENSE.TXT |
| Windows SDK / WinRT projections | Microsoft / SDK distribution terms | Resolved Windows targeting pack notices must be retained at publish |
| NSIS | https://nsis.sourceforge.io/License | Builder, not bundled in source |

Runtime npm dependency notices copied from the resolved installed packages:

| Package | Version | License | Included notice |
|---|---|---|---|
| @microsoft/signalr | 10.0.11 | MIT | See package distribution |
| abort-controller | 3.0.0 | MIT | LICENSES/npm/abort-controller/LICENSE |
| ansi-regex | 5.0.1 | MIT | LICENSES/npm/ansi-regex/license |
| ansi-styles | 4.3.0 | MIT | LICENSES/npm/ansi-styles/license |
| camelcase | 5.3.1 | MIT | LICENSES/npm/camelcase/license |
| cliui | 6.0.0 | ISC | LICENSES/npm/cliui/LICENSE.txt |
| color-convert | 2.0.1 | MIT | LICENSES/npm/color-convert/LICENSE |
| color-name | 1.1.4 | MIT | LICENSES/npm/color-name/LICENSE |
| decamelize | 1.2.0 | MIT | LICENSES/npm/decamelize/license |
| dijkstrajs | 1.0.3 | MIT | LICENSES/npm/dijkstrajs/LICENSE.md |
| emoji-regex | 8.0.0 | MIT | LICENSES/npm/emoji-regex/LICENSE-MIT.txt |
| event-target-shim | 5.0.1 | MIT | LICENSES/npm/event-target-shim/LICENSE |
| eventsource | 2.0.2 | MIT | LICENSES/npm/eventsource/LICENSE |
| fetch-cookie | 2.2.0 | Unlicense | See package distribution |
| find-up | 4.1.0 | MIT | LICENSES/npm/find-up/license |
| get-caller-file | 2.0.5 | ISC | LICENSES/npm/get-caller-file/LICENSE.md |
| is-fullwidth-code-point | 3.0.0 | MIT | LICENSES/npm/is-fullwidth-code-point/license |
| locate-path | 5.0.0 | MIT | LICENSES/npm/locate-path/license |
| node-fetch | 2.7.0 | MIT | LICENSES/npm/node-fetch/LICENSE.md |
| p-limit | 2.3.0 | MIT | LICENSES/npm/p-limit/license |
| p-locate | 4.1.0 | MIT | LICENSES/npm/p-locate/license |
| p-try | 2.2.0 | MIT | LICENSES/npm/p-try/license |
| path-exists | 4.0.0 | MIT | LICENSES/npm/path-exists/license |
| pngjs | 5.0.0 | MIT | LICENSES/npm/pngjs/LICENSE |
| psl | 1.15.0 | MIT | LICENSES/npm/psl/LICENSE |
| punycode | 2.3.1 | MIT | LICENSES/npm/punycode/LICENSE-MIT.txt |
| qrcode | 1.5.4 | MIT | LICENSES/npm/qrcode/license |
| querystringify | 2.2.0 | MIT | LICENSES/npm/querystringify/LICENSE |
| react | 19.3.0 | MIT | LICENSES/npm/react/LICENSE |
| react-dom | 19.3.0 | MIT | LICENSES/npm/react-dom/LICENSE |
| require-directory | 2.1.1 | MIT | LICENSES/npm/require-directory/LICENSE |
| require-main-filename | 2.0.0 | ISC | LICENSES/npm/require-main-filename/LICENSE.txt |
| requires-port | 1.0.0 | MIT | LICENSES/npm/requires-port/LICENSE |
| scheduler | 0.28.0 | MIT | LICENSES/npm/scheduler/LICENSE |
| set-blocking | 2.0.0 | ISC | LICENSES/npm/set-blocking/LICENSE.txt |
| set-cookie-parser | 2.7.2 | MIT | LICENSES/npm/set-cookie-parser/LICENSE |
| string-width | 4.2.3 | MIT | LICENSES/npm/string-width/license |
| strip-ansi | 6.0.1 | MIT | LICENSES/npm/strip-ansi/license |
| tough-cookie | 4.1.4 | BSD-3-Clause | LICENSES/npm/tough-cookie/LICENSE |
| tr46 | 0.0.3 | MIT | See package distribution |
| universalify | 0.2.0 | MIT | LICENSES/npm/universalify/LICENSE |
| url-parse | 1.5.10 | MIT | LICENSES/npm/url-parse/LICENSE |
| webidl-conversions | 3.0.1 | BSD-2-Clause | LICENSES/npm/webidl-conversions/LICENSE.md |
| whatwg-url | 5.0.0 | MIT | LICENSES/npm/whatwg-url/LICENSE.txt |
| which-module | 2.0.1 | ISC | LICENSES/npm/which-module/LICENSE |
| wrap-ansi | 6.2.0 | MIT | LICENSES/npm/wrap-ansi/license |
| ws | 7.5.13 | MIT | LICENSES/npm/ws/LICENSE |
| y18n | 4.0.3 | ISC | LICENSES/npm/y18n/LICENSE |
| yargs | 15.4.1 | MIT | LICENSES/npm/yargs/LICENSE |
| yargs-parser | 18.1.3 | ISC | LICENSES/npm/yargs-parser/LICENSE.txt |

React and QRCode dependencies are embedded in the Vite production bundle; the copied license files must ship with the program. Development-only dependencies (TypeScript, Vite, Playwright and test tooling) are not included in the runtime bundle. Their license terms remain available in their npm distributions. Fonts are unmodified; custom UI SVG paths are authored for LocalControl.

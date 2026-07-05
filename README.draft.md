<p align="center">
  <img src="src/Zapret.App/Assets/icon.ico" alt="Zapret 2 GUI" width="96" height="96"><br>
</p>

<h1 align="center">Zapret 2 GUI</h1>

<p align="center"><i>Современный GUI в стиле Windows 11 для движка обхода DPI winws2 (zapret v2) — окно и трей вместо .bat</i></p>

<p align="center">
  <a href="#возможности">Возможности</a> •
  <a href="#как-это-работает">Как это работает</a> •
  <a href="#установка-и-сборка">Сборка</a> •
  <a href="#структура-проекта">Структура</a> •
  <a href="#скриншоты">Скриншоты</a>
</p>

<p align="center">
  <a href="https://github.com/LoST202/zapret2gui/blob/main/LICENSE"><img src="https://img.shields.io/badge/License-MIT-blue" alt="License: MIT"></a>
  <img src="https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-0078D6?logo=windows" alt="Platform: Windows 10 | 11">
  <img src="https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet" alt=".NET 10">
  <a href="https://github.com/LoST202/zapret2gui/stargazers"><img src="https://img.shields.io/github/stars/LoST202/zapret2gui?style=flat" alt="GitHub stars"></a>
  <!-- OPTIONAL — раскомментируйте после первого релиза:
  <a href="https://github.com/LoST202/zapret2gui/releases"><img src="https://img.shields.io/github/v/release/LoST202/zapret2gui" alt="Latest release"></a>
  <a href="https://github.com/LoST202/zapret2gui/releases"><img src="https://img.shields.io/github/downloads/LoST202/zapret2gui/total" alt="Downloads"></a>
  -->
</p>

> [!CAUTION]
> **Дисклеймер:** инструмент предназначен для законного восстановления доступа к легитимным ресурсам и для исследовательских задач. Ответственность за использование несёт пользователь; соблюдайте законы своей юрисдикции.

**Zapret 2 GUI** (Zapret2) — десктопное приложение для Windows, которое оборачивает движок обхода DPI **winws2** (zapret v2 от [bol-van](https://github.com/bol-van/zapret)). Оно **не переписывает DPI-логику** — оно запускает и обслуживает `winws2`, а сверху даёт удобное окно в стиле Fluent/Windows 11, иконку в трее, диагностику и управление стратегиями вместо возни с `.bat`-файлами. Движок и все файлы (`bin/` `lua/` `lists/`) уже лежат в репозитории — докачивать ничего не нужно.

Стек: **C# / .NET 10**, **WPF + WPF-UI** (Fluent), Hardcodet.NotifyIcon.Wpf (трей), AvalonEdit (редактор стратегий). Два проекта: `Zapret.Core` (без UI, `net10.0`) и `Zapret.App` (WPF, `net10.0-windows`).

## Возможности

- 🟢 **Подключение в один клик** — большой круг-статус; стратегия выбирается из списка.
- 🎯 **Авто-подбор** — перебирает стратегии и оставляет ту, через которую сайты реально открываются.
- 🧩 **Составной дашборд** из карточек (Списки / Быстрые действия / Профили / Сеть).
- 💾 **Профили** — сохранение и переключение наборов стратегий в один клик.
- 📋 **Менеджер списков** — хостлисты и IP-сеты (`lists/*.txt`), обновление ipset по URL, правка и валидация.
- ✏️ **Редактор стратегий** — полная команда `winws2` с подсветкой синтаксиса, автодополнением и справочником по аргументам (грамматика winws2), либо режим полей.
- 🩺 **Диагностика** — проверки конфликтов (другие обходы, VPN, прокси, службы), тесты доступности (HTTP · TLS · ping + DPI-checker) и панель «Система и сеть» (публичный IP / ASN / ISP) с маской приватности для скриншотов.
- 🔔 **Трей** — сворачивание в трей, иконка с цветом-статусом, уведомления, watchdog с авто-перезапуском, авто-подключение при старте.
- 🪶 **Лёгкость и резидентность** — GC настроен на низкий RAM, рабочий набор ужимается при сворачивании; single-file exe (~6.7 МБ) или portable (~69 МБ).

## Скриншоты

<!-- TODO: положите реальные PNG в docs/screenshots/ (dashboard.png, diagnostics.png, strategy-editor.png).
     Пока это плейсхолдеры — при отсутствии файлов картинки не отобразятся. -->

<p align="center">
  <img src="docs/screenshots/dashboard.png" alt="Главный дашборд" width="80%"><br>
  <i>Главный экран: статус-круг, выбор стратегии, карточки</i>
</p>

<p align="center">
  <img src="docs/screenshots/diagnostics.png" alt="Диагностика" width="80%"><br>
  <i>Диагностика: конфликты, тесты доступности, «Система и сеть»</i>
</p>

<p align="center">
  <img src="docs/screenshots/strategy-editor.png" alt="Редактор стратегий" width="80%"><br>
  <i>Редактор стратегий: полная команда winws2 с подсветкой и автодополнением</i>
</p>

## Как это работает

Zapret 2 GUI — это **обёртка**, а не форк движка. Он не реализует обход DPI сам: он собирает из выбранной стратегии аргументы командной строки `winws2`, запускает движок (zapret v2 / NFQWS2 + WinDivert + Lua-скрипты) и следит за его жизненным циклом (старт/стоп, watchdog, авто-перезапуск). Всё, что касается перехвата пакетов и трюков с DPI, остаётся на стороне `winws2`; GUI отвечает за интерфейс, стратегии, списки, диагностику и удобство.

Практическое следствие: проблемы **интерфейса, запуска или подбора стратегий** — это Zapret 2 GUI; проблемы самой **DPI-логики** — это upstream [bol-van/zapret](https://github.com/bol-van/zapret).

## Установка и сборка

Нужен **[.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0)**.

**Обычная сборка** (single-file `Zapret.exe` в `dist/` рядом с движком; пользователю нужен .NET 10 Desktop Runtime):

```bat
build.bat
```

**Portable-сборка** (self-contained, без предварительных требований, ~69 МБ):

```bat
build.bat portable
```

**Для разработки:**

```bat
dotnet build -c Release
```

Обе команды `build.bat` кладут результат в `dist/` рядом с уже присутствующими `bin/` `lua/` `lists/`.

## Запуск

- Запускайте `dist/Zapret.exe` **от имени администратора** — драйверу WinDivert нужны права, поэтому появится запрос UAC.
- Движок **уже в комплекте** (`bin/` `lua/` `lists/`) — ничего докачивать не требуется.
- При первом запуске рядом с exe создаётся папка `state/` (стратегии и настройки).

Поддерживаются только **Windows 10 / 11**.

## Структура проекта

- **`src/Zapret.Core`** (`net10.0`): вся логика без UI.
- **`src/Zapret.App`** (`net10.0-windows`): интерфейс на WPF + WPF-UI.
- **`sample-state`**: дефолтные стратегии и настройки (копируются в `state/` при первом запуске).
- **`bin/` `lua/` `lists/`**: движок zapret v2 (`winws2`, Lua-скрипты, списки) — уже в репозитории.
- **`Zapret.slnx`**: solution-файл.
- **`Directory.Build.props`**: общие свойства сборки.
- **`build.bat`**: сборочный скрипт (`build.bat` / `build.bat portable`).

## Как устроен код

**`Zapret.Core`:**

- **`AppPaths`**: пути и `ZAPRET_ROOT`.
- **`Models` / `AppState`**: модель состояния + сериализация в JSON.
- **`CommandParser` / `CommandBuilder`**: преобразование стратегии в аргументы `winws2` и обратно.
- **`WinwsRunner`**: жизненный цикл движка (старт/стоп, наблюдение).
- **`AutoPicker`**: авто-подбор рабочей стратегии.
- **`Autostart` / `Lists` / `ListsManager`**: автозапуск и работа со списками.

**`Zapret.App`:**

- **`MainWindow`**: главное окно и боковое меню (Главная / Списки / Редактор / Журнал / Диагностика / Настройки). Редактор стратегий вынесен в `MainWindow.Editor.cs`, подсветка winws2 — в `WinwsSyntax.cs` + `Assets/WinwsHighlighting.xshd`.
- **`Diagnostics`**: проверки конфликтов и тесты доступности.
- **`App`**: single-instance, трей, экономия памяти.

## Обратная связь

Проблемы **интерфейса, запуска или стратегий** — заводите в [issues](https://github.com/LoST202/zapret2gui/issues). Приложите, пожалуйста:

- таблицу диагностики (вкладка «Диагностика» → «Проверить»);
- провайдера / регион / версию Windows;
- выбранную стратегию и лог из вкладки «Журнал».

Проблемы самой **DPI-логики** — в [bol-van/zapret](https://github.com/bol-van/zapret).

## Благодарности

- **[bol-van/zapret](https://github.com/bol-van/zapret)** — движок `winws2`, на котором всё держится.
- **[Flowseal/zapret-discord-youtube](https://github.com/Flowseal/zapret-discord-youtube)** — образец реализации zapret под Windows.
- **[basil00/WinDivert](https://github.com/basil00/WinDivert)** — перехват пакетов.
- **[lepoco/wpfui](https://github.com/lepoco/wpfui)** — Fluent-контролы Windows 11.
- **[icsharpcode/AvalonEdit](https://github.com/icsharpcode/AvalonEdit)** — редактор кода с подсветкой.

## Лицензия

Код обёртки (`src/`) — [MIT](LICENSE).

Включённый движок (`bin/` `lua/` `lists/`) не является частью обёртки и распространяется под лицензиями своих авторов: zapret (bol-van), WinDivert (LGPLv3 / GPLv3), `cygwin1.dll` (LGPLv3). При дальнейшем распространении соблюдайте их условия.

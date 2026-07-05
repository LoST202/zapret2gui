<p align="center">
  <img src="src/Zapret.App/Assets/icon.ico" alt="Zapret 2 GUI" width="96" height="96">
</p>

<h1 align="center">Zapret 2 GUI</h1>

<p align="center"><i>Современный GUI в стиле Windows 11 для движка обхода DPI winws2 (zapret v2) — окно и трей вместо .bat-файлов.</i></p>

<p align="center">
  <a href="#возможности">Возможности</a> •
  <a href="#как-это-работает">Как это работает</a> •
  <a href="#установка-и-сборка">Сборка</a> •
  <a href="#структура-проекта">Структура</a> •
  <a href="#скриншоты">Скриншоты</a>
</p>

<p align="center">
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-blue" alt="License: MIT"></a>
  <img src="https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-0078D6?logo=windows&logoColor=white" alt="Platform: Windows 10 | 11">
  <img src="https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white" alt=".NET 10">
  <a href="https://github.com/LoST202/zapret2gui/stargazers"><img src="https://img.shields.io/github/stars/LoST202/zapret2gui?style=flat" alt="GitHub stars"></a>
  <!-- ОПЦИОНАЛЬНО — раскомментируйте, когда появится первый релиз в GitHub Releases:
  <a href="https://github.com/LoST202/zapret2gui/releases"><img src="https://img.shields.io/github/v/release/LoST202/zapret2gui" alt="Latest release"></a>
  <a href="https://github.com/LoST202/zapret2gui/releases"><img src="https://img.shields.io/github/downloads/LoST202/zapret2gui/total" alt="Downloads"></a>
  -->
</p>

> [!Caution]
> **Дисклеймер об использовании:** <br>
> Инструмент предназначен для законного восстановления доступа к легитимным ресурсам. Ответственность за использование несёт пользователь; соблюдайте законы своей юрисдикции.

**Zapret 2 GUI** (Zapret2) — десктопное приложение для Windows, которое оборачивает движок обхода DPI **winws2** (zapret v2 от [bol-van](https://github.com/bol-van/zapret)). Оно **не переписывает DPI-логику** — оно запускает и обслуживает `winws2`, а сверху даёт удобное окно в стиле Fluent / Windows 11, иконку в трее, диагностику и управление стратегиями вместо возни с `.bat`-файлами. Движок и все файлы (`bin/` `lua/` `lists/`) уже лежат в репозитории — докачивать ничего не нужно.

Стек: **C# / .NET 10**, **WPF + WPF-UI** (Fluent), Hardcodet.NotifyIcon.Wpf (трей), AvalonEdit (редактор стратегий). Два проекта: `Zapret.Core` (без UI, `net10.0`) и `Zapret.App` (WPF, `net10.0-windows`).

<!-- TODO: замените плейсхолдер ниже на реальный скриншот главного окна. Положите PNG в docs/screenshots/dashboard.png -->
<p align="center">
  <img src="docs/screenshots/dashboard.png" alt="Главное окно Zapret 2 GUI" width="820">
</p>

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
  <img src="docs/screenshots/dashboard.png" alt="Главный дашборд" width="820"><br>
  <sub>Главный экран · статус-круг, выбор стратегии, карточки</sub>
</p>

<p align="center">
  <img src="docs/screenshots/diagnostics.png" alt="Диагностика" width="820"><br>
  <sub>Диагностика · конфликты, тесты доступности, «Система и сеть»</sub>
</p>

<p align="center">
  <img src="docs/screenshots/strategy-editor.png" alt="Редактор стратегий" width="820"><br>
  <sub>Редактор стратегий · полная команда winws2 с подсветкой и автодополнением</sub>
</p>

## Как это работает

Zapret 2 GUI — это **обёртка**, а не форк движка. Он не реализует обход DPI сам: он собирает из выбранной стратегии аргументы командной строки `winws2`, запускает движок (zapret v2 / NFQWS2 + WinDivert + Lua-скрипты) и следит за его жизненным циклом (старт / стоп, watchdog, авто-перезапуск, снятие драйвера при остановке). Всё, что касается перехвата пакетов и трюков с DPI, остаётся на стороне `winws2`; GUI отвечает за интерфейс, стратегии, списки, диагностику и удобство.

Практическое следствие: проблемы **интерфейса, запуска или подбора стратегий** — это Zapret 2 GUI; проблемы самой **DPI-логики** — это upstream [bol-van/zapret](https://github.com/bol-van/zapret).

## Установка и сборка

Нужен **[.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0)**.

**Обычная сборка** — single-file `Zapret.exe` в `dist/` рядом с движком (требует .NET 10 Desktop Runtime у пользователя):

```bat
build.bat
```

**Portable-сборка** — self-contained, без предварительных требований (~69 МБ):

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
- **`sample-state`**: дефолтные стратегии и настройки (шаблоны для `state/`).
- **`bin/` `lua/` `lists/`**: движок zapret v2 (`winws2`, WinDivert, Lua-скрипты, хостлисты / ipset) — уже в репозитории.
- **`Zapret.slnx`**: solution-файл.
- **`Directory.Build.props`**: общие свойства сборки.
- **`build.bat`**: сборочный скрипт (`build.bat` / `build.bat portable`).

## Как устроен код

**`Zapret.Core`:**

- **`AppPaths`** — пути и `ZAPRET_ROOT`.
- **`Models` / `AppState`** — модель состояния и сериализация в JSON.
- **`CommandParser` / `CommandBuilder`** — преобразование стратегии в аргументы `winws2` и обратно.
- **`WinwsRunner`** — жизненный цикл движка (старт / стоп, наблюдение, снятие драйвера).
- **`AutoPicker`** — авто-подбор рабочей стратегии.
- **`Autostart` / `Lists` / `ListsManager`** — автозапуск и работа со списками.

**`Zapret.App`:**

- **`MainWindow`** — главное окно и боковое меню (Главная / Списки / Редактор / Журнал / Диагностика / Настройки).
- **`Diagnostics`** — проверки конфликтов и тесты доступности.
- **`App`** — single-instance, трей, экономия памяти.

## Обратная связь

Проблемы **интерфейса, запуска или стратегий** — заводите в [issues](https://github.com/LoST202/zapret2gui/issues). Приложите, пожалуйста:

- таблицу диагностики;
- провайдера / регион / версию Windows;
- выбранную стратегию и лог.

Проблемы самой **DPI-логики** — в upstream-проект [bol-van/zapret](https://github.com/bol-van/zapret).

## Благодарности

- [**bol-van/zapret**](https://github.com/bol-van/zapret) — движок `winws2`, на котором всё держится.
- [**Flowseal/zapret-discord-youtube**](https://github.com/Flowseal/zapret-discord-youtube) — образец конфигураций под Windows.
- [**basil00/WinDivert**](https://github.com/basil00/WinDivert) — перехват пакетов.
- [**WPF-UI**](https://github.com/lepoco/wpfui) — Fluent-компоненты интерфейса.
- [**AvalonEdit**](https://github.com/icsharpcode/AvalonEdit) — редактор с подсветкой синтаксиса.

## Лицензия

Код обёртки в `src/` — **[MIT](LICENSE)**. Встроенный движок (`bin/` `lua/` `lists/`) распространяется под лицензиями своих авторов: zapret (bol-van); WinDivert (LGPLv3 / GPLv3); `cygwin1.dll` (LGPLv3). При дальнейшем распространении соблюдайте их условия.

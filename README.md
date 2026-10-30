# MetroModern — современный Яндекс.Метро для Windows 10 Mobile

Порт идеи «современного» Яндекс.Метро на HP Elite x3 (Windows 10 Mobile), т.к.
штатный клиент Яндекса в магазине для W10M — из 2015-го и не знает новых станций.

> Важно: приложение использует **публичный веб-API Яндекс.Метро** (yandex.ru/metro)
> для получения схем. Данные принадлежат Яндексу; ассеты и кеш — только для
> персонального использования, распространять (appx с содержимым API) нельзя.

## Архитектура

```
MetroModern/
  src/MetroCore/           ядро (netstandard2.0), десктопный эталон маршрутизации
    Network.cs             модели: станции/узлы/связи/линии + NetworkBuilder
    Router.cs              Дейкстра с релаксацией + штраф пересадки (150 с за цепочку)
    SchemeLoader.cs        JSON -> модель
    SvgScheme.cs           векторные примитивы схемы (graphics)
    SvgExporter.cs         экспорт в SVG (--crop для QA-зумов)
    SvgPathParser.cs       SVG d-пути -> фигуры (дуги A = 7 аргументов!)
  src/Preview/             консольный QA-harness: парсинг, маршруты, SVG-выгрузка
    dotnet run --project src/Preview -- data/saint-petersburg.json out/spb.svg "Девяткино|Купчино"
    флаги: --edges <ст> | --station <имя> | --group <id> | --crop "x y w h"
  src/MetroModern.Web/     >>> ПРИЛОЖЕНИЕ ДЛЯ ТЕЛЕФОНА (JS/HTML UWP) <<<
    AppxManifest.xml       Windows.Universal, Min 10240, MaxTested 15254
    default.html           чипы A/B, карточка маршрута, шторка станции, поиск
    css/app.css            flexbox only (CSS grid на EdgeHTML нет!)
    js/lib/metro.js        сеть + Дейкстра (порт MetroCore) + поиск
    js/lib/scheme.js       graphics JSON -> SVG DOM; позиции станций из graphicsIds
    js/app.js              пан/зум/пинч, тап-выбор, подсветка маршрута, localStorage
    js/data/*.json         дампы схем (офлайн-первый)
  tools/                   сборка и деплой (Windows SDK, без Visual Studio)
  data/                    скачанные схемы (не для распространения!)
```

## Почему приложение — JS, а не C#

Windows 10 Mobile исполняет только **ARM32**. C#-UWP собирается в ARM32 только
.NET Native из Visual Studio (workload ~8 ГБ); современный .NET (8/9/10) UWP
поддерживает, но без ARM32 → до телефона не доезжает. JS/HTML UWP не требует
компиляции вообще: EdgeHTML на телефоне рендерит настоящий SVG из графики
Яндекса (дуги, якоря текста — сразу правильно), пакет собирается makeappx из
Windows SDK. MetroCore остаётся быстрым десктопным эталоном: маршруты,
вычисленные в Preview, обязаны совпадать с тем, что считает телефон.

## Ограничения EdgeHTML на W10M (для кода приложения)

- JS: ES2015, без async/await надёжнее, без `?.`, без модулей; сеть — XHR.
- CSS: flexbox + absolute; **никакого grid**, без `position: sticky`.
- Иконки: обычные unicode-символы (Segoe MDL2 есть на телефоне, но не в десктоп-тестах).

## Структура данных схем Яндекса (реверс веб-версии)

```
GET https://yandex.ru/metro/api/scheme/getMetroSchemeBySchemeId
    ?ajax=1&id=<schemeId>&lang=ru
  -> {"csrfToken":"..."}                       # bootstrap
GET .../getMetroSchemeBySchemeId?ajax=1&csrfToken=<enc>&id=<schemeId>&lang=ru&s=<rnd>&sessionId=<uid>
  -> { data: { schemeMeta, schemeData, resourcesByCacheKey } }
```

schemeId: Москва `sc34974011`, СПб `sc60983525`. Внутри `schemeData`:
- `stations` — `name`, `coordinates` (**гео lat/lon!**), `stops[{nodeId,lineId}]`,
  `graphicsIds` — ссылки на группы графики этой станции;
- `graphics` — объект: группа на фичу (маркер станции = Circle id "background",
  подпись = пара Text id textStroke+text). Подписи — два зум-набора (fs20/fs12);
  якоря нестандартные: `h`=слева-направо, `f`=справа-налево, `a`=сверху, `b`=снизу;
  у Text НЕТ свойства opacity (дефолт = 1!);
- `links` — рёбра: `fromNodeId -> toNodeId`, `time` (сек), `lineIds`;
  пересадки закодированы как `"lineIds":[null]` — чистить;
- `schemeMeta.center` + `scale` — `{initial, overview, detailed, min, max}` для вида.

Инструмент дампа: `tools/dump-metro.ps1`.

## Сборка appx (Windows SDK, без Visual Studio)

```
powershell -File tools\make-icons.ps1      # один раз: иконки
powershell -File tools\make-cert.ps1       # один раз: самоподписанный серт
powershell -File tools\build-web.ps1       # stage -> makeappx -> signtool
#  -> build\out\MetroModern_<версия>_neutral.appx
```

SDK ищется в `C:\Program Files (x86)\Windows Kits\10\bin` и на других дисках
(у нас живёт в `T:\Windows Kits\10`).

## Деплой на телефон (HP Elite x3)

1. На телефоне: **Настройки → Обновление и безопасность → Для разработчиков**:
   Режим разработчика ВКЛ, Обнаружение устройства ВКЛ.
2. Если лимит dev-приложений исчерпан (ошибка 0x80073D11) — снести лишний
   sideload-пакет (скрипт умеет через `.../api/app/packagemanager/package` DELETE).
3. Деплой:
   ```
   powershell -File tools\deploy-web.ps1 -Ip <телефон-ip> -Pin <PIN-из-«Сопряжение»> -Launch
   ```
   PIN — 6-символьный код с экрана «Pair»; он же пароль WDP (логин `Administrator`).
   Скрипт: пара сессии → загрузка appx+cer (POST `/api/app/packagemanager/package?package=<имя>`,
   поля формы названы именами файлов) → опрос состояния → запуск.

Десктоп-превью без телефона: `tools\devserver.ps1 -Root src\MetroModern.Web -Port 8766`
→ http://127.0.0.1:8766/default.html

## Планы (v2)

- [ ] онлайн-обновление схем (WinRT HttpClient из UWP)
- [ ] трассировка маршрута по геометрии линий (сейчас — прямые полилинии по станциям)
- [ ] ночные закрытия станций: getMetroEventsBySchemeId
- [ ] онлайн-время прихода поездов
- [ ] избранное/история, тёмная тема (styles: light/dark в schemeMeta)
- [ ] resources: getSchemeResourcesBySchemeId (иконки объектов вместо заглушек)

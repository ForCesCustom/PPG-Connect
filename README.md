# Connect for People Playground

Current package: **v0.1.47** · protocol **v9** · People Playground **1.27.17**

## Скачать и установить

[Скачать полный Connect-v0.1.47.zip](https://github.com/ForCesCustom/PPG-Connect/raw/main/Releases/Connect-v0.1.47.zip)

У всех игроков должны совпадать версия Connect, сборка игры и используемый контент.
Полностью закрой игру, распакуй весь ZIP в папку с `People Playground.exe`,
разреши объединение папок и запусти игру через Steam. В архив входят BepInEx 5
x64, Doorstop, Connect и карточка для обычного меню Mods. Панель открывается
по F8, диагностика — F10. Не заменяй игровые DLL/exe и не удаляй другие плагины.

Хост создаёт Lobby в F8, приглашает друга через Steam Overlay и запускает
сессию/выбирает карту. Гость автоматически загружает установленную у него
карту хоста. Дождитесь статуса PLAYING. Камера, масштаб, выбор предмета в Tab,
выделение и настройки интерфейса остаются отдельными у каждого игрока.

## Что изменилось в 0.1.47

- Исправлен пропадающий спавн хоста: Connect наблюдает сам вызов игровых
  событий, поэтому очистка подписчиков при загрузке каталога больше его не отключает.
- Структура реплики теперь берётся из исходного предмета каталога. Добавленные
  во время игры эффекты и подсветка не меняют номера частей и не блокируют состояние.
- Восстановление существующих предметов учитывает исходный ключ каталога;
  после смены карты оно ждёт удаления старых объектов. Исправлена очистка
  контекста спавна при исключениях и восстановление после отключения Steam.

Ранее добавлено в 0.1.46:

- Новая адаптивная панель: карточки игроков, более читаемые статусы, настройки
  и прокрутка на небольших экранах.
- Общий мир получает номер поколения. Повторная загрузка той же карты тоже
  обновляет поколение; старые пакеты больше не применяются к новому миру.
- Хост обнаруживает распознаваемые предметы каталога, созданные до сессии или
  скопированные штатным способом. Повторные ограниченные проходы восстанавливают
  пропущенные объекты, а полные списки присутствия убирают лишние реплики.
- Передаются положения частей объекта, масштаб, отображение/коллайдеры и
  выбранные типизированные параметры физики, температуры, огня, здоровья и
  состояния конечностей/кожи. Исходные ссылки на части сохраняются после их
  отделения у хоста.
- Кроме спавна, grab и Activate/Delete, гость может запросить у хоста Freeze,
  No Collide, Weightless и Ignite. Очистка, Undo общей истории хоста, пауза,
  замедление и поддерживаемые настройки окружения тоже идут через хост с
  проверкой разрешений.
- Линии проводов хоста передаются как ограниченное визуальное представление.

Это расширение поддерживаемой синхронизации, а не обещание одинакового
поведения любого Workshop-мода. Подробные ограничения:
[KNOWN_LIMITATIONS.md](KNOWN_LIMITATIONS.md).

## Проверка с другом

Оба установите v0.1.47 и создайте новую Lobby. Выберите разные предметы в Tab
(например Human и Android), спавните с обеих сторон и сравните предметы,
число объектов, позы и перетаскивание удержанием ЛКМ. Затем проверьте удаление,
Freeze/Ignite, паузу и повторную загрузку той же карты. Для проверки восстановления
войдите гостем в уже заполненную хостом карту.

Реальный двухаккаунтный Steam-тест нужно проводить с двумя разными аккаунтами.
Сборка и автоматические проверки сами по себе его не заменяют.
Результаты сборки, 1035 проверок в движке и SHA-256:
[SYNC_TEST_REPORT_v0.1.47.md](SYNC_TEST_REPORT_v0.1.47.md).

## English

Connect is a BepInEx 5 multiplayer prototype using People Playground's existing
Steam context and Steam Relay. The host simulates the shared world; guests send
validated intents and display replicated state. Each player keeps an independent
camera, Tab catalogue, cursor and selection.

[Download Connect-v0.1.47.zip](https://github.com/ForCesCustom/PPG-Connect/raw/main/Releases/Connect-v0.1.47.zip).
Close the game, extract the complete archive beside `People Playground.exe`,
merge the supplied folders, then launch through Steam and press F8. Every
player needs this exact Connect version and matching game/content.

Version 0.1.47 fixes host spawn observation being erased by the game's event
reset and uses catalogue-authored state layouts instead of transient runtime
effects. It retains epoch-gated reloads, world reconciliation, typed state,
shared permitted controls, host wire visuals and the adaptive menu from 0.1.46.
It does not replicate arbitrary Workshop scripts,
dynamic object graphs, complete wound textures or all projectile/explosion
effects. Wires are visual on guests; guest wire creation is not implemented.

## Development and diagnostics

Build `Dev/PPGTogether.BepInEx.csproj` against the installed game's managed
assemblies and BepInEx core. The project includes `Source/*.cs`.
Run the applicable protocol, bot, installation, world-manifest, object-state
and shared-world smoke tests from `Dev/`. Record actual build/test/runtime
results separately; never infer a live two-player pass from codec tests.

Logs: `<People Playground>/BepInEx/LogOutput.log`.

See [PROTOCOL.md](PROTOCOL.md), [PATCHES.md](PATCHES.md),
[BOT_INTELLIGENCE.md](BOT_INTELLIGENCE.md) and [CHANGELOG.md](CHANGELOG.md).

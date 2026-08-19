# Connect for People Playground

[Русская версия](#русская-версия) · [English](#english)

> Current package: **v0.1.35** · protocol **v4** · People Playground **1.27.16**

## English

Connect is a BepInEx 5 multiplayer prototype for **People Playground**. It uses
the Steam context already created by the game and Steam relay networking: it
does not initialise a second Steam client, open public ports, or expose IP
addresses.

### Download and install

Download the complete plug-and-play ZIP:

**[Connect-v0.1.35.zip](https://github.com/ForCesCustom/PPG-Connect/raw/main/Releases/Connect-v0.1.35.zip)**

1. Close People Playground.
2. Extract the full ZIP into the folder containing `People Playground.exe`.
3. Allow Windows to merge the included `BepInEx` folder. Do not overwrite the
   game executable or remove unrelated plugins.
4. Start People Playground through Steam and press `F8`.

The ZIP already includes BepInEx 5 Unity.Mono-win-x64, the Connect plugin,
icon, Doorstop files and installation documentation. All players must use the
same People Playground build and the same Connect version.

The Workshop Companion is published under **Mercury**, matching the current
Steam Workshop account and preventing the author-mismatch upload prompt.
If someone installs only that Companion, or its version differs from the
BepInEx runtime, it shows a native People Playground recovery dialog with
**OPEN CONNECT ON GITHUB** instead of pretending multiplayer is available.
The same direct URL is printed in the game notice.

### How multiplayer works

- One player creates a Steam lobby and is the host.
- The host invites friends through the official Steam Overlay `[ + ]` cards.
- Clients join via the Steam lobby callback or the safe `+connect_lobby`
  launch argument. Once the host starts and selects a map, clients load that
  same locally installed map automatically.
- Steam relay transport carries the handshake, cursor updates and approved game
  actions.
- Version 0.1.35 follows People Playground's full map-selection route: the
  client runs the matching base-game sandbox scene transition, then verifies
  the requested map root. Connected non-hosts cannot select or enter a
  divergent local map. It also sends compact cursor updates at 60–120 Hz even
  while UI is open.
- Version 0.1.33 fixes the host relay poll-group registration that previously
  let a lobby connection appear accepted while `Hello`, cursor and map packets
  were never received. Focused relay and map diagnostics are written to
  `BepInEx\\LogOutput.log`.
- Every player has a separate world-space cursor, local camera, zoom, Tab
  catalog, right-click selection and context UI.
- The host remains authoritative for shared physics. Clients submit intents;
  the host validates them and broadcasts approved spawn/despawn and physics
  state.

### Controls

| Control | Result |
|---|---|
| `F8` | Open/close Connect panel |
| `F10` | Network diagnostics |
| `Tab` | Your own normal People Playground catalog |
| Left mouse | Host-authoritative object drag |
| Configured `activateDirect` key | Host-validated Use; holding it supports continuous Use for automatic vanilla firearms |
| Context menu Activate/Delete | Host-validated action on a registered Connect object |

### Current scope and limitations

Implemented: Steam lobby/invites, Steam relay handshake, independent coloured
cursors visible from the Steam Relay handshake onward, host-authoritative grab
leases, host-led installed-map follow, post-session vanilla catalog spawns,
despawns, root Rigidbody2D snapshots, bounded Use/Delete actions, automatic
weapon continuous Use, bot cursors, and host/player settings.

Not yet implemented as full replication: pre-existing world transfer,
ragdoll limb biology and dismemberment topology, joints, wires, arbitrary
Workshop context actions, projectile/hit/explosion state, complete pre-existing
world reconstruction, undo, host migration and a public lobby browser. See
[KNOWN_LIMITATIONS.md](KNOWN_LIMITATIONS.md) before playing.

No player can send files, DLLs, shell commands, paths, arbitrary type data or
method names through Connect. Releases are distributed as reviewed ZIP files
from this repository.

### Build and verification

The plugin is compiled against the local People Playground `1.27.16` Mono
assemblies and Facepunch Steamworks wrapper. Current checks include a full
compile, 10,000 malformed packet fuzz cases, cursor codec tests, continuous
Use lease tests and bot-brain smoke tests. A real two-account Steam session
still requires two separate Steam accounts/devices.

Logs: `<People Playground>\BepInEx\LogOutput.log`

---

## Русская версия

Connect — это мультиплеерный прототип People Playground на BepInEx 5. Он
использует уже созданный игрой Steam-контекст и Steam Relay: не создаёт второй
Steam-клиент, не открывает порты и не передаёт IP-адреса игроков.

### Скачать и установить

Скачай полный plug-and-play архив:

**[Connect-v0.1.35.zip](https://github.com/ForCesCustom/PPG-Connect/raw/main/Releases/Connect-v0.1.35.zip)**

1. Полностью закрой People Playground.
2. Распакуй весь ZIP в папку, где лежит `People Playground.exe`.
3. Разреши Windows объединить папку `BepInEx`. Не заменяй exe игры и не удаляй
   другие плагины.
4. Запусти игру через Steam и нажми `F8`.

В ZIP уже есть BepInEx 5 Unity.Mono-win-x64, Connect, иконка, Doorstop и
инструкции. У всех игроков должна быть одинаковая версия игры и Connect.

Workshop Companion опубликован от автора **Mercury** — это значение совпадает
с текущим Steam Workshop-профилем и не вызывает предупреждение о другом авторе
при загрузке.
Если пользователь установил только Companion или его версия не совпадает с
BepInEx runtime, он увидит нативное окно People Playground с кнопкой
**OPEN CONNECT ON GITHUB**. Та же прямая ссылка выводится в игровом уведомлении.

### Как работает мультиплеер

- Один игрок создаёт Steam Lobby и становится хостом.
- Хост приглашает друзей через официальные карточки `[ + ]` Steam Overlay.
- Клиенты входят через Steam invite или безопасный параметр `+connect_lobby`.
  После старта хоста и выбора карты они автоматически загружают ту же локально
  установленную карту.
- Steam Relay передаёт handshake, курсоры и подтверждённые игровые действия.
- Версия 0.1.35 проводит клиента через полный штатный переход карты People
  Playground: сначала sandbox-сцена, затем проверка root нужной карты.
  Подключённый не-хост не может сам выбрать или открыть другую карту. Курсор
  отправляется с частотой 60–120 Гц и не замедляется в UI.
- В версии 0.1.33 исправлена регистрация соединения в host poll group: раньше
  лобби могло выглядеть подключённым, но хост не принимал `Hello`, курсоры и
  команду карты. Подробный relay/map журнал находится в
  `BepInEx\\LogOutput.log`.
- У каждого игрока независимые мировой курсор, камера, zoom, Tab-каталог,
  выделение и ПКМ-меню.
- Общая физика авторитетна у хоста. Клиенты отправляют намерения; хост их
  проверяет и рассылает spawn/despawn/physics state.

### Управление

| Кнопка | Действие |
|---|---|
| `F8` | Открыть/закрыть Connect |
| `F10` | Сетевая диагностика |
| `Tab` | Твой обычный каталог People Playground |
| ЛКМ | Авторитетное перетаскивание через хост |
| Клавиша `activateDirect` | Подтверждённый хостом Use; удержание поддерживает автоматическое vanilla-оружие |
| Activate/Delete из ПКМ | Подтверждённое действие для зарегистрированного Connect-объекта |

### Что реально есть и чего пока нет

Уже есть: Steam Lobby/инвайты, Steam Relay handshake, независимые цветные
курсоры, видимые уже после Steam Relay handshake, leases для grab, автопереход
на выбранную хостом установленную карту, vanilla-спавн после старта сессии, despawn, root
Rigidbody2D snapshots, ограниченные Use/Delete, continuous Use для
автоматического оружия, боты и настройки хоста/игрока.

Пока нет полной синхронизации: существующего до старта мира, конечностей и
дисмембера, joints/wires, произвольных Workshop-действий, состояния пуль/
попаданий/взрывов, undo, host migration и публичного списка лобби.
Перед игрой прочитай [KNOWN_LIMITATIONS.md](KNOWN_LIMITATIONS.md).

Connect не принимает по сети файлы, DLL, команды, пути, произвольные типы или
имена методов. Обновления распространяются проверенными ZIP-архивами из этого
репозитория.

### Сборка и проверки

Плагин собран против локальной People Playground `1.27.16` и её Facepunch
Steamworks. Пройдены compile, 10 000 fuzz-пакетов, cursor codec, continuous Use
lease и Bot Brain smoke tests. Для настоящего Steam-теста всё ещё нужны два
разных Steam-аккаунта/устройства.

Лог: `<People Playground>\BepInEx\LogOutput.log`

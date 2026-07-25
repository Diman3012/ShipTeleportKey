# ShipTeleportKey

[![Thunderstore](https://img.shields.io/badge/Thunderstore-ShipTeleportKey-blue)](https://thunderstore.io/c/lethal-company/p/SHLUHA/ShipTeleportKey/)
[![Game: Lethal Company](https://img.shields.io/badge/Game-Lethal%20Company-red)](https://store.steampowered.com/app/1966720/Lethal_Company/)
[![BepInEx 5](https://img.shields.io/badge/BepInEx-5.x-green)](https://thunderstore.io/c/lethal-company/p/BepInEx/BepInExPack/)

Client-side mod for **Lethal Company**. Press a key (default **F4**) and the ship teleporter beams you up — exactly as if a crewmate pressed the teleporter button for you.

**[Download on Thunderstore](https://thunderstore.io/c/lethal-company/p/SHLUHA/ShipTeleportKey/)** · **[GitHub](https://github.com/Diman3012/ShipTeleportKey)**

---

## Features

- **F4 outside the ship** — teleport yourself to the ship using the ship teleporter.
- **F4 on the ship** — trigger the teleporter for the player currently selected on the monitor.
- **No teleporter = no action** — if the ship teleporter is not purchased/installed, the key does nothing.
- **Cooldown feedback** — if the teleporter is on cooldown, a HUD tip shows the remaining time.
- **Keep held items** — items stay in your hands on both regular and inverse teleports (configurable).
- **Shorter inverse cooldown** — inverse teleporter cooldown reduced from 210 s to **10 s** (configurable).
- **Safe input** — disabled while typing in chat or using the terminal.

## Controls

| Action | Default key | Description |
| --- | --- | --- |
| Teleport | `F4` | Trigger ship teleporter (configurable) |

## Installation

### Thunderstore Mod Manager (recommended)

1. Install [r2modman](https://thunderstore.io/package/ebkr/r2modman/) or [Gale Mod Manager](https://thunderstore.io/c/lethal-company/p/Kastraliss/GaleModManager/).
2. Search for **ShipTeleportKey** by **SHLUHA**.
3. Install and launch the game through the mod manager.

### Manual

1. Install [BepInEx Pack](https://thunderstore.io/c/lethal-company/p/BepInEx/BepInExPack/) for Lethal Company.
2. Download `ShipTeleportKey.dll` from [Thunderstore](https://thunderstore.io/c/lethal-company/p/SHLUHA/ShipTeleportKey/) or build from source.
3. Place the DLL in:

```text
Lethal Company/BepInEx/plugins/
```

4. Launch the game once to generate the config file.

## Configuration

Config path: `BepInEx/config/dp991.ShipTeleportKey.cfg`

| Option | Default | Description |
| --- | --- | --- |
| `TeleportKey` | `F4` | Key that triggers the teleport (Unity InputSystem `Key` names) |
| `KeepItemsOnTeleport` | `true` | Don't drop held items on teleport |
| `InverseTeleportCooldownSeconds` | `10` | Inverse teleporter cooldown in seconds (vanilla: 210) |

## Multiplayer

- Teleporting **yourself to the ship** works with the mod installed only on your client.
- **Keeping items on teleport** is reliable in multiplayer only if all players have the mod installed.

## Building from source

Requires [.NET SDK](https://dotnet.microsoft.com/download). Set your game path in `ShipTeleportKey.csproj` (`GameDir` property).

```powershell
dotnet build -c Release
```

The built `ShipTeleportKey.dll` is copied automatically to `BepInEx/plugins` if that folder exists.

---

## Русский

Клиентский мод для **Lethal Company**. Нажми клавишу (по умолчанию **F4**) — телепорт корабля заберёт тебя на корабль, как если бы кто-то нажал кнопку телепорта вручную.

### Возможности

- **F4 вне корабля** — телепорт на корабль через обычный (не инверсный) телепорт.
- **F4 на корабле** — телепортирует игрока, выбранного на мониторе.
- **Нет телепорта — ничего не происходит.**
- **Предметы не выпадают** из рук при телепортации (можно отключить в конфиге).
- **Перезарядка инверсного телепорта** — 10 секунд вместо 210 (настраивается).
- Не работает в чате и терминале.

### Установка

1. Установите [BepInEx Pack](https://thunderstore.io/c/lethal-company/p/BepInEx/BepInExPack/).
2. Скачайте мод с [Thunderstore](https://thunderstore.io/c/lethal-company/p/SHLUHA/ShipTeleportKey/) или соберите из исходников.
3. Положите `ShipTeleportKey.dll` в `Lethal Company/BepInEx/plugins/`.

Конфиг: `BepInEx/config/dp991.ShipTeleportKey.cfg`

### Мультиплеер

- Телепорт **себя на корабль** работает, если мод стоит только у вас.
- **Сохранение предметов** надёжно работает, только если мод установлен у всех игроков.

---

**Author:** [Diman3012](https://github.com/Diman3012) · **Version:** 1.5.4

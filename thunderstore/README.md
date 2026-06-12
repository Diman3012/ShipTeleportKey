# ShipTeleportKey

Press a key (default **F4**) and the ship teleporter beams you up to the ship — exactly as if a crewmate pressed the teleporter button for you.

## Features

- **F4** outside the ship — teleport yourself to the ship using the ship teleporter.
- **F4 on the ship** — press the teleporter button for the player currently selected on the monitor.
- If there is **no teleporter** on the ship, the key does nothing.
- If the teleporter is on cooldown, a HUD tip shows the remaining time.
- **Keep held items** on teleport (both regular and inverse teleporter). Can be disabled in config.
- Inverse teleporter cooldown reduced to **10 seconds** (configurable).
- Doesn't work while typing in chat or using the terminal.

## Configuration

After the first launch, edit `BepInEx/config/dp991.ShipTeleportKey.cfg`:

| Option | Default | Description |
|---|---|---|
| `TeleportKey` | `F4` | Key that triggers the teleport |
| `KeepItemsOnTeleport` | `true` | Don't drop held items on teleport |
| `InverseTeleportCooldownSeconds` | `10` | Inverse teleporter cooldown (vanilla: 210) |

## Русский

Нажми **F4** вне корабля — телепорт заберёт тебя на корабль. На корабле **F4** телепортирует того, кто выбран на мониторе. Если телепорта на корабле нет, кнопка ничего не делает. Предметы из рук не выпадают (отключается в конфиге). Перезарядка инверсного телепорта — 10 секунд. Кнопка настраивается в конфиге.

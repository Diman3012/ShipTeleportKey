using System.Collections;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using GameNetcodeStuff;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ShipTeleportKey
{
    [BepInPlugin(Guid, Name, Version)]
    public class ShipTeleportKeyPlugin : BaseUnityPlugin
    {
        public const string Guid = "dp991.ShipTeleportKey";
        public const string Name = "ShipTeleportKey";
        public const string Version = "1.5.4";

        internal static ManualLogSource Log;
        internal static ConfigEntry<Key> TeleportKey;
        internal static ConfigEntry<bool> KeepItems;
        internal static ConfigEntry<float> InverseCooldown;

        private void Awake()
        {
            Log = Logger;
            TeleportKey = Config.Bind(
                "General",
                "TeleportKey",
                Key.F4,
                "Кнопка, по которой телепорт корабля забирает тебя на корабль.");
            KeepItems = Config.Bind(
                "General",
                "KeepItemsOnTeleport",
                true,
                "Не выбрасывать предметы из рук при телепортации (и обычным, и инверсным телепортом).");
            InverseCooldown = Config.Bind(
                "General",
                "InverseTeleportCooldownSeconds",
                10f,
                "Перезарядка инверсного телепорта (который кидает в помещение) в секундах. В игре по умолчанию 210.");

            var harmony = new Harmony(Guid);
            harmony.PatchAll(typeof(KeepItemsPatches));
            harmony.PatchAll(typeof(CooldownPatches));
            harmony.PatchAll(typeof(TeleportTargetPatches));
            harmony.PatchAll(typeof(NetworkPrefabPatch2));

            // Логика живёт на отдельном скрытом объекте: Lethal Company при запуске
            // отключает посторонние GameObject'ы в сцене, включая объект BepInEx.
            var runnerObject = new GameObject("ShipTeleportKeyRunner")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            DontDestroyOnLoad(runnerObject);
            runnerObject.AddComponent<TeleportRunner>();

            Log.LogInfo($"{Name} {Version} загружен. Кнопка телепорта: {TeleportKey.Value}");
        }
    }

    internal class TeleportRunner : MonoBehaviour
    {
        // cooldownTime — приватное поле ShipTeleporter, читаем через рефлексию
        private static readonly FieldInfo CooldownField =
            typeof(ShipTeleporter).GetField("cooldownTime", BindingFlags.NonPublic | BindingFlags.Instance);

        private bool _teleportInProgress;
        private bool _firstUpdateLogged;
        private bool _keyWasPressed;
        private float _lastPressTime = -10f;
        private InputAction _teleportAction;

        private void Update()
        {
            if (!_firstUpdateLogged)
            {
                _firstUpdateLogged = true;
                ShipTeleportKeyPlugin.Log.LogInfo($"Update работает. Клавиатура обнаружена: {Keyboard.current != null}.");
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            Key key = ShipTeleportKeyPlugin.TeleportKey.Value;

            // Основной механизм — InputAction, как у самой игры
            if (_teleportAction == null)
            {
                string controlPath = keyboard[key].path;
                _teleportAction = new InputAction("ShipTeleportKey.Teleport", InputActionType.Button, controlPath);
                _teleportAction.performed += _ => OnTeleportKeyPressed("InputAction");
                _teleportAction.Enable();
                ShipTeleportKeyPlugin.Log.LogInfo($"Кнопка привязана через InputAction: {controlPath}");
            }

            // Запасной механизм — ручное отслеживание isPressed
            bool pressed = keyboard[key].isPressed;
            if (pressed && !_keyWasPressed)
                OnTeleportKeyPressed("опрос isPressed");
            _keyWasPressed = pressed;
        }

        private void OnDisable()
        {
            ShipTeleportKeyPlugin.Log.LogWarning("TeleportRunner отключён (OnDisable) — кнопка перестанет работать!");
        }

        private void OnDestroy()
        {
            ShipTeleportKeyPlugin.Log.LogWarning("TeleportRunner уничтожен (OnDestroy) — кнопка перестанет работать!");
            _teleportAction?.Disable();
            _teleportAction?.Dispose();
        }

        private void OnTeleportKeyPressed(string source)
        {
            // Оба механизма могут сработать на одно нажатие — гасим дубль
            if (Time.unscaledTime - _lastPressTime < 0.3f)
                return;
            _lastPressTime = Time.unscaledTime;

            if (_teleportInProgress)
                return;

            ShipTeleportKeyPlugin.Log.LogInfo($"Нажата кнопка {ShipTeleportKeyPlugin.TeleportKey.Value} (источник: {source}) — пробую телепортироваться.");
            TryTeleportSelf();
        }

        private void TryTeleportSelf()
        {
            ManualLogSource log = ShipTeleportKeyPlugin.Log;

            StartOfRound round = StartOfRound.Instance;
            if (round == null)
            {
                log.LogInfo("StartOfRound.Instance == null (мы в главном меню?) — отмена.");
                return;
            }

            PlayerControllerB localPlayer = round.localPlayerController;
            if (localPlayer == null)
            {
                log.LogInfo("localPlayerController == null — отмена.");
                return;
            }

            if (localPlayer.isTypingChat || localPlayer.inTerminalMenu)
            {
                log.LogInfo($"Игрок занят (чат: {localPlayer.isTypingChat}, терминал: {localPlayer.inTerminalMenu}) — отмена.");
                return;
            }

            ShipTeleporter teleporter = FindShipTeleporter();
            if (teleporter == null)
            {
                // Телепорт не куплен/не установлен — кнопка ничего не делает
                log.LogInfo("Телепорт на корабле не найден — телепортация невозможна.");
                return;
            }

            float cooldown = CooldownField != null ? (float)CooldownField.GetValue(teleporter) : 0f;
            if (cooldown > 0f)
            {
                log.LogInfo($"Телепорт на перезарядке: ещё {cooldown:F0} сек.");
                HUDManager.Instance?.DisplayTip("Телепорт", $"Перезарядка: ещё {(int)cooldown} сек.");
                return;
            }

            ManualCameraRenderer mapScreen = round.mapScreen;
            if (mapScreen == null)
            {
                log.LogInfo("mapScreen == null — отмена.");
                return;
            }

            // На корабле — цель с монитора. На луне — только себя.
            bool onShip = round.inShipPhase && !localPlayer.isInsideFactory;
            int targetIndex = onShip
                ? mapScreen.targetTransformIndex
                : FindPlayerRadarIndex(mapScreen, localPlayer);

            if (targetIndex < 0 || targetIndex >= mapScreen.radarTargets.Count)
            {
                log.LogWarning("Не удалось определить цель на радаре — отмена.");
                return;
            }

            string targetName = mapScreen.radarTargets[targetIndex]?.name ?? "?";
            log.LogInfo(onShip
                ? $"На корабле — цель [{targetIndex}]: {targetName}."
                : $"На луне — цель (я) [{targetIndex}]: {targetName}.");

            // На луне радар часто смотрит на другого — нужна синхронизация.
            // На корабле достаточно текущего выбора на мониторе.
            bool needsRadarSync = !onShip
                && (mapScreen.targetTransformIndex != targetIndex
                    || mapScreen.targetedPlayer == null
                    || mapScreen.targetedPlayer != localPlayer);

            if (needsRadarSync)
            {
                StartCoroutine(SyncRadarAndPress(mapScreen, targetIndex, teleporter));
                return;
            }

            PressTeleportWithLockedTarget(mapScreen, targetIndex, teleporter);
        }

        private static int FindPlayerRadarIndex(ManualCameraRenderer mapScreen, PlayerControllerB player)
        {
            for (int i = 0; i < mapScreen.radarTargets.Count; i++)
            {
                TransformAndName target = mapScreen.radarTargets[i];
                if (target != null && target.transform == player.transform)
                    return i;
            }

            return -1;
        }

        private static void PressTeleportWithLockedTarget(
            ManualCameraRenderer mapScreen,
            int targetIndex,
            ShipTeleporter teleporter)
        {
            TeleportTargetPatches.ApplyRadarTarget(mapScreen, targetIndex);
            TeleportTargetPatches.LockedTargetIndex = targetIndex;
            try
            {
                ShipTeleportKeyPlugin.Log.LogInfo("Нажимаю кнопку телепорта.");
                teleporter.PressTeleportButtonOnLocalClient();
            }
            catch (System.Exception ex)
            {
                ShipTeleportKeyPlugin.Log.LogError($"Ошибка при нажатии телепорта: {ex}");
            }
            finally
            {
                TeleportTargetPatches.LockedTargetIndex = -1;
            }
        }

        private IEnumerator SyncRadarAndPress(ManualCameraRenderer mapScreen, int targetIndex, ShipTeleporter teleporter)
        {
            _teleportInProgress = true;
            try
            {
                ShipTeleportKeyPlugin.Log.LogInfo($"Синхронизирую радар на индекс {targetIndex}...");
                mapScreen.SwitchRadarTargetAndSync(targetIndex);
                yield return new WaitForSecondsRealtime(0.35f);
                PressTeleportWithLockedTarget(mapScreen, targetIndex, teleporter);
            }
            finally
            {
                _teleportInProgress = false;
            }
        }

        private static ShipTeleporter FindShipTeleporter()
        {
            ShipTeleporter[] teleporters = FindObjectsOfType<ShipTeleporter>();
            foreach (ShipTeleporter teleporter in teleporters)
            {
                // Инверсный телепорт кидает ВНУТРЬ объекта — нам нужен обычный
                if (!teleporter.isInverseTeleporter)
                    return teleporter;
            }

            return null;
        }
    }

    /// <summary>
    /// Перед beamUpPlayer фиксирует одну цель на всех клиентах по индексу радара.
    /// Иначе каждый клиент тянет своего mapScreen.targetedPlayer — «телепорт всех».
    /// </summary>
    internal static class TeleportTargetPatches
    {
        internal static int LockedTargetIndex = -1;

        internal static void ApplyRadarTarget(ManualCameraRenderer mapScreen, int index)
        {
            if (mapScreen == null || index < 0 || index >= mapScreen.radarTargets.Count)
                return;

            TransformAndName entry = mapScreen.radarTargets[index];
            if (entry?.transform == null)
                return;

            mapScreen.targetTransformIndex = index;
            mapScreen.targetedPlayer = entry.transform.GetComponentInParent<PlayerControllerB>();
        }

        [HarmonyPatch(typeof(ShipTeleporter), nameof(ShipTeleporter.PressTeleportButtonClientRpc))]
        [HarmonyPrefix]
        private static void LockTargetBeforeBeamUp(ShipTeleporter __instance)
        {
            if (__instance.isInverseTeleporter)
                return;

            ManualCameraRenderer mapScreen = StartOfRound.Instance?.mapScreen;
            if (mapScreen == null)
                return;

            int index = LockedTargetIndex >= 0 ? LockedTargetIndex : mapScreen.targetTransformIndex;
            ApplyRadarTarget(mapScreen, index);
        }
    }

    /// <summary>
    /// Патчи, которые не дают игре выбрасывать предметы из рук при телепортации.
    /// </summary>
    internal static class KeepItemsPatches
    {
        // true, пока выполняется TeleportPlayerOutWithInverseTeleporter (телепорт С корабля)
        private static bool _inverseTeleportInProgress;

        private static bool IsLocalPlayer(PlayerControllerB player)
        {
            return GameNetworkManager.Instance != null
                && player == GameNetworkManager.Instance.localPlayerController;
        }

        // Обычный телепорт (на корабль): во время переноса у игрока shipTeleporterId == 1,
        // и именно в этот момент beamUpPlayer вызывает DropAllHeldItemsAndSync.
        [HarmonyPatch(typeof(PlayerControllerB), nameof(PlayerControllerB.DropAllHeldItemsAndSync))]
        [HarmonyPrefix]
        private static bool SkipDropOnBeamUp(PlayerControllerB __instance)
        {
            if (ShipTeleportKeyPlugin.KeepItems.Value
                && __instance.shipTeleporterId == 1
                && IsLocalPlayer(__instance))
            {
                ShipTeleportKeyPlugin.Log.LogInfo("Телепорт на корабль: оставляю предметы в руках.");
                return false;
            }

            return true;
        }

        [HarmonyPatch(typeof(ShipTeleporter), "TeleportPlayerOutWithInverseTeleporter")]
        [HarmonyPrefix]
        private static void InverseTeleportStarted()
        {
            _inverseTeleportInProgress = true;
        }

        [HarmonyPatch(typeof(ShipTeleporter), "TeleportPlayerOutWithInverseTeleporter")]
        [HarmonyFinalizer]
        private static void InverseTeleportFinished()
        {
            _inverseTeleportInProgress = false;
        }

        // Инверсный телепорт (с корабля): DropAllHeldItems вызывается изнутри
        // TeleportPlayerOutWithInverseTeleporter.
        [HarmonyPatch(typeof(PlayerControllerB), nameof(PlayerControllerB.DropAllHeldItems))]
        [HarmonyPrefix]
        private static bool SkipDropOnInverseTeleport(PlayerControllerB __instance)
        {
            if (ShipTeleportKeyPlugin.KeepItems.Value
                && _inverseTeleportInProgress
                && IsLocalPlayer(__instance))
            {
                ShipTeleportKeyPlugin.Log.LogInfo("Телепорт с корабля: оставляю предметы в руках.");
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Уменьшает перезарядку инверсного телепорта (по умолчанию в игре — 210 секунд).
    /// </summary>
    internal static class CooldownPatches
    {
        private static readonly FieldInfo CooldownTimeField =
            typeof(ShipTeleporter).GetField("cooldownTime", BindingFlags.NonPublic | BindingFlags.Instance);

        [HarmonyPatch(typeof(ShipTeleporter), "Awake")]
        [HarmonyPostfix]
        private static void ReduceInverseCooldown(ShipTeleporter __instance)
        {
            if (!__instance.isInverseTeleporter)
                return;

            float newCooldown = Mathf.Max(0f, ShipTeleportKeyPlugin.InverseCooldown.Value);
            __instance.cooldownAmount = newCooldown;

            // Если телепорт заспавнился уже «на перезарядке», укорачиваем и текущий отсчёт
            if (CooldownTimeField != null && (float)CooldownTimeField.GetValue(__instance) > newCooldown)
                CooldownTimeField.SetValue(__instance, newCooldown);

            ShipTeleportKeyPlugin.Log.LogInfo($"Перезарядка инверсного телепорта установлена: {newCooldown} сек.");
        }
    }

    /// <summary>
    /// Регистрирует сетевой префаб мода при инициализации NetworkManager.
    /// </summary>
    [HarmonyPatch(typeof(NetworkManager))]
    internal static class NetworkPrefabPatch2
    {
        private static readonly string MOD_GUID = ShipTeleportKeyPlugin.Guid;

        [HarmonyPostfix]
        [HarmonyPatch(nameof(NetworkManager.SetSingleton))]
        private static void RegisterPrefab()
        {
            var prefab = new GameObject(MOD_GUID + " Prefab");
            prefab.hideFlags |= HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(prefab);
            var networkObject = prefab.AddComponent<NetworkObject>();
            var fieldInfo = typeof(NetworkObject).GetField("GlobalObjectIdHash", BindingFlags.Instance | BindingFlags.NonPublic);
            fieldInfo!.SetValue(networkObject, GetHash(MOD_GUID));

            NetworkManager.Singleton.PrefabHandler.AddNetworkPrefab(prefab);
            return;

            static uint GetHash(string value)
            {
                return value?.Aggregate(17u, (current, c) => unchecked((current * 31) ^ c)) ?? 0u;
            }
        }
    }
}

using System.Collections;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ShipTeleportKey
{
    [BepInPlugin(Guid, Name, Version)]
    public class ShipTeleportKeyPlugin : BaseUnityPlugin
    {
        public const string Guid = "dp991.ShipTeleportKey";
        public const string Name = "ShipTeleportKey";
        public const string Version = "1.4.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<Key> TeleportKey;
        internal static ConfigEntry<bool> KeepItems;

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

            new Harmony(Guid).PatchAll(typeof(KeepItemsPatches));

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

            int targetIndex = -1;
            for (int i = 0; i < mapScreen.radarTargets.Count; i++)
            {
                TransformAndName target = mapScreen.radarTargets[i];
                if (target != null && target.transform == localPlayer.transform)
                {
                    targetIndex = i;
                    break;
                }
            }

            log.LogInfo($"Телепорт найден: {teleporter.name}. Индекс игрока на радаре: {targetIndex} (целей на радаре: {mapScreen.radarTargets.Count}).");

            if (targetIndex < 0)
            {
                log.LogWarning("Не нашёл себя в списке целей радара — отмена.");
                return;
            }

            StartCoroutine(SwitchTargetAndTeleport(mapScreen, targetIndex, teleporter));
        }

        private IEnumerator SwitchTargetAndTeleport(ManualCameraRenderer mapScreen, int targetIndex, ShipTeleporter teleporter)
        {
            _teleportInProgress = true;
            try
            {
                // Наводим радар монитора на себя и даём время на синхронизацию по сети
                if (mapScreen.targetTransformIndex != targetIndex)
                {
                    ShipTeleportKeyPlugin.Log.LogInfo($"Переключаю радар с цели {mapScreen.targetTransformIndex} на {targetIndex}...");
                    mapScreen.SwitchRadarTargetAndSync(targetIndex);
                    yield return new WaitForSeconds(0.4f);
                }

                ShipTeleportKeyPlugin.Log.LogInfo("Нажимаю кнопку телепорта.");
                teleporter.PressTeleportButtonOnLocalClient();
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
}

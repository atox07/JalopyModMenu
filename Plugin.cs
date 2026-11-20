using System;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace JalopyModMenu
{
    [BepInPlugin(PluginId, "Jalopy Mod Menu", "0.1.0")]
    public sealed class Plugin : BaseUnityPlugin
    {
        private const string PluginId = "atox.jalopy.modmenu";
        private static Plugin current;

        private GameBindings game;
        private Harmony harmony;
        private MovementSession movement;
        private bool menuOpen;
        private int selectedRow;
        private float speed = 8f;
        private string status;

        private void Start()
        {
            try
            {
                game = new GameBindings();
                harmony = new Harmony(PluginId);
                harmony.Patch(game.WalkingTick, prefix: new HarmonyMethod(
                    typeof(Plugin), "BeforeWalkingTick"));
                current = this;
                Logger.LogInfo("Jalopy Mod Menu loaded. Press F7 to open it.");
            }
            catch (Exception exception)
            {
                game = null;
                status = "Could not bind this Jalopy build. See BepInEx logs.";
                Logger.LogError(exception);
            }
        }

        private void Update()
        {
            if (movement != null)
                ValidateMovement();

            if (Input.GetKeyDown(KeyCode.F7))
                menuOpen = !menuOpen;

            if (!menuOpen)
                return;

            if (Input.GetKeyDown(KeyCode.PageUp))
                selectedRow = (selectedRow + 2) % 3;
            if (Input.GetKeyDown(KeyCode.PageDown))
                selectedRow = (selectedRow + 1) % 3;

            if (selectedRow == 2)
            {
                if (Input.GetKeyDown(KeyCode.Home))
                    speed = Mathf.Max(1f, speed - 1f);
                if (Input.GetKeyDown(KeyCode.End))
                    speed = Mathf.Min(30f, speed + 1f);
                return;
            }

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                ToggleMode(selectedRow == 0);
        }

        private void OnGUI()
        {
            if (!menuOpen)
                return;

            GUILayout.BeginArea(new Rect(20f, 20f, 355f, 226f), GUI.skin.box);
            GUILayout.Label("Jalopy Mod Menu");
            GUILayout.Space(6f);
            DrawRow(0, "Fly", movement != null && movement.Fly ? "ON" : "OFF");
            DrawRow(1, "Noclip", movement != null && movement.Noclip ? "ON" : "OFF");
            DrawRow(2, "Speed", speed.ToString("0") + " m/s");
            GUILayout.Space(5f);
            GUILayout.Label("PgUp / PgDn   Select    Enter   Toggle");
            GUILayout.Label("Home / End   Speed    F7   Close");
            GUILayout.Label("Space / Ctrl   Vertical fly    Shift   Boost");
            if (!string.IsNullOrEmpty(status))
                GUILayout.Label(status);
            GUILayout.EndArea();
        }

        private void DrawRow(int index, string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label((selectedRow == index ? ">  " : "    ") + label);
            GUILayout.FlexibleSpace();
            GUILayout.Label(value);
            GUILayout.EndHorizontal();
        }

        private void ToggleMode(bool fly)
        {
            if (game == null)
                return;

            status = null;
            if (!game.IsWalkingAvailable())
            {
                status = "Resume the game and get out of the car.";
                return;
            }

            if (movement == null)
            {
                Behaviour walker = game.FindWalker(Camera.main);
                var body = walker ? walker.GetComponent<Rigidbody>() : null;
                if (!walker || !walker.isActiveAndEnabled || !body || body.isKinematic || game.IsSeated(walker))
                {
                    status = "Movement mods are available while on foot.";
                    return;
                }

                movement = new MovementSession(walker, body);
            }

            if (fly)
                movement.SetFly(!movement.Fly);
            else
                movement.TrySetNoclip(!movement.Noclip, out status);

            if (!movement.Fly && !movement.Noclip)
                EndMovement(true);
        }

        private bool ValidateMovement()
        {
            bool seated = movement.Walker && game.IsSeated(movement.Walker);
            if (seated || !movement.IsUsable)
            {
                EndMovement(!seated);
                return false;
            }

            return true;
        }

        private void EndMovement(bool restorePosition)
        {
            var previous = movement;
            movement = null;
            if (previous != null)
                previous.Release(restorePosition);
        }

        private static bool BeforeWalkingTick(object __instance)
        {
            var plugin = current;
            if (!plugin || plugin.movement == null || !ReferenceEquals(plugin.movement.Walker, __instance))
                return true;

            return plugin.RunMovement();
        }

        private bool RunMovement()
        {
            if (!ValidateMovement())
                return true;

            var view = Camera.main;
            if (!view || menuOpen || !game.IsWalkingAvailable())
            {
                movement.Move(Vector3.zero);
                return false;
            }

            Vector2 input = game.ReadMovement();
            Vector3 forward = movement.Fly ? view.transform.forward : movement.Walker.transform.forward;
            Vector3 right = movement.Walker.transform.right;
            Vector3 direction = forward * input.y + right * input.x;
            if (movement.Fly)
            {
                float up = Input.GetKey(KeyCode.Space) ? 1f : 0f;
                float down = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) ? 1f : 0f;
                direction += Vector3.up * (up - down);
            }

            bool boost = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            movement.Move(Vector3.ClampMagnitude(direction, 1f) * (boost ? speed * 2f : speed));
            return false;
        }

        private void OnDisable()
        {
            bool seated = movement != null && movement.Walker && game != null && game.IsSeated(movement.Walker);
            EndMovement(!seated);
            menuOpen = false;
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(current, this))
            {
                current = null;
                harmony.Unpatch(game.WalkingTick, HarmonyPatchType.Prefix, PluginId);
            }
        }
    }
}

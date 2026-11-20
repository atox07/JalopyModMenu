using System;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace JalopyModMenu
{
    [BepInPlugin(PluginId, "Jalopy Mod Menu", "0.7.1")]
    public sealed class Plugin : BaseUnityPlugin
    {
        private const string PluginId = "atox.jalopy.modmenu";
        private static Plugin current;

        private GameBindings game;
        private GameTools tools;
        private Harmony harmony;
        private MovementSession movement;
        private VehicleNoclip carNoclip;
        private WorldPlacement placement;
        private FreecamEditor propEditor;
        private FreecamEditor mapEditor;
        private FreecamEditor freecam;
        private WorldMeshPickup carriedMesh;
        private float carryStatusUntil;
        private MapBuilder mapBuilder;
        private int mapCategory;
        private WorldEnvironment environment;
        private bool menuOpen;
        private bool infiniteMoney;
        private bool infiniteFuel;
        private float nextRefresh;
        private Page page;
        private int selectedRow;
        private float speed = 8f;
        private string status;
        private const int VisibleItems = 9;

        private enum Page { Main, Movement, Vehicle, Items, Money, Editor, Time, Weather, EditorItems, Paint, Map, MapAssets }
        private static readonly float[] TimePresets = { 8f, 12f, 18.5f, 22f };
        private static readonly string[] PaintNames = {
            "Ivory", "White", "Silver", "Graphite", "Black", "Red", "Burgundy",
            "Orange", "Mustard", "Olive", "Teal", "Sky Blue", "Navy", "Violet"
        };
        private static readonly Color32[] PaintColors = {
            new Color32(226, 215, 184, 255), new Color32(238, 238, 232, 255), new Color32(154, 160, 164, 255),
            new Color32(69, 74, 77, 255), new Color32(26, 27, 28, 255), new Color32(178, 38, 35, 255),
            new Color32(101, 28, 44, 255), new Color32(221, 101, 34, 255), new Color32(201, 153, 38, 255),
            new Color32(102, 112, 60, 255), new Color32(35, 117, 107, 255), new Color32(93, 163, 204, 255),
            new Color32(42, 63, 105, 255), new Color32(115, 61, 132, 255)
        };

        private void Start()
        {
            try
            {
                game = new GameBindings();
                tools = new GameTools(game);
                mapBuilder = new MapBuilder(game);
                propEditor = new FreecamEditor(game, tools, mapBuilder, false);
                mapEditor = new FreecamEditor(game, tools, mapBuilder, true);
                harmony = new Harmony(PluginId);
                harmony.Patch(game.WalkingTick, prefix: new HarmonyMethod(
                    typeof(Plugin), "BeforeWalkingTick"));
                if (tools.StopDriving != null)
                    harmony.Patch(tools.StopDriving, prefix: new HarmonyMethod(
                        typeof(Plugin), "BeforeDrivingStop"));
                if (tools.NativePickupUpdate != null)
                    harmony.Patch(tools.NativePickupUpdate, prefix: new HarmonyMethod(
                        typeof(Plugin), "BeforeNativePickup"));
                current = this;
                try
                {
                    environment = new WorldEnvironment(game);
                    harmony.Patch(environment.ClockUpdate, prefix: new HarmonyMethod(
                        typeof(Plugin), "BeforeClockUpdate"));
                    harmony.Patch(environment.NativeSetWeather, prefix: new HarmonyMethod(
                        typeof(Plugin), "BeforeNativeWeather"), postfix: new HarmonyMethod(
                        typeof(Plugin), "AfterNativeWeather"));
                }
                catch (Exception exception)
                {
                    UnpatchEnvironment();
                    environment = null;
                    Logger.LogWarning("Time and weather controls could not be initialized: " + exception);
                }
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
            RefreshCheats();
            if (environment != null)
                environment.TickLightning();

            if (freecam != null && freecam.Active)
            {
                bool exit = Input.GetKeyDown(KeyCode.F7);
                if (exit || !freecam.HasView || !game.IsWalkingAvailable() ||
                    !freecam.Walker || game.IsSeated(freecam.Walker))
                {
                    freecam.Close();
                    menuOpen = exit;
                }
                else
                    freecam.Update();
                return;
            }

            if (placement != null)
            {
                UpdatePlacement();
                return;
            }

            if (Input.GetKeyDown(KeyCode.F7))
                menuOpen = !menuOpen;

            if (!menuOpen)
                return;

            if (Input.GetKeyDown(KeyCode.Backspace) && page != Page.Main)
            {
                switch (page)
                {
                    case Page.Paint: page = Page.Vehicle; selectedRow = 6; break;
                    case Page.EditorItems: page = Page.Editor; selectedRow = 2; break;
                    case Page.Map: page = Page.Editor; selectedRow = 4; break;
                    case Page.MapAssets: page = Page.Map; selectedRow = mapCategory; break;
                    default: selectedRow = (int)page - 1; page = Page.Main; break;
                }
                status = null;
                return;
            }

            int rows = RowCount();
            if (rows == 0)
                return;
            if (Input.GetKeyDown(KeyCode.UpArrow))
                selectedRow = (selectedRow + rows - 1) % rows;
            if (Input.GetKeyDown(KeyCode.DownArrow))
                selectedRow = (selectedRow + 1) % rows;

            int horizontal = (Input.GetKeyDown(KeyCode.RightArrow) ? 1 : 0) -
                (Input.GetKeyDown(KeyCode.LeftArrow) ? 1 : 0);
            if (page == Page.Movement && selectedRow == 2)
                speed = Mathf.Clamp(speed + horizontal, 1f, 30f);
            if ((IsItemPage || page == Page.MapAssets) && horizontal != 0)
                selectedRow = Mathf.Clamp(selectedRow + horizontal * VisibleItems, 0, rows - 1);

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                try { ActivateRow(); }
                catch (Exception exception)
                {
                    status = "Action failed. Check BepInEx/LogOutput.log.";
                    Logger.LogError(exception);
                }
            }
        }

        private bool IsItemPage { get { return page == Page.Items || page == Page.EditorItems; } }

        private int RowCount()
        {
            switch (page)
            {
                case Page.Movement: return 3;
                case Page.Vehicle: return 7;
                case Page.Paint: return PaintNames.Length;
                case Page.Money: return 2;
                case Page.Editor: return 5;
                case Page.Map: return MapBuilder.Categories.Length + 3;
                case Page.MapAssets: return mapBuilder.Visible.Count;
                case Page.Time:
                case Page.Weather: return 5;
                case Page.EditorItems:
                case Page.Items: return tools != null ? tools.Items.Count : 0;
                default: return 7;
            }
        }

        private void ActivateRow()
        {
            if (game == null)
                return;

            status = null;
            if (page == Page.Main)
            {
                page = (Page)(selectedRow + 1);
                selectedRow = 0;
                if (page == Page.Items)
                    tools.LoadItems();
                return;
            }
            if (!game.IsWalkingAvailable())
            {
                status = "Enter or resume the game first.";
                return;
            }

            switch (page)
            {
                case Page.Time:
                    status = environment == null ? "Time controls unavailable." : selectedRow == TimePresets.Length
                        ? environment.ToggleFreeze() : environment.SetTime(TimePresets[selectedRow]);
                    break;
                case Page.Weather:
                    status = environment == null ? "Weather controls unavailable." :
                        environment.SetWeather((WeatherMode)(selectedRow - 1));
                    break;
                case Page.Movement:
                    if (selectedRow < 2)
                        ToggleMode(selectedRow == 0);
                    break;
                case Page.Vehicle:
                    ActivateVehicleRow();
                    break;
                case Page.Paint:
                    status = tools.PaintLaika(PaintColors[selectedRow], PaintNames[selectedRow]);
                    break;
                case Page.Items:
                    status = tools.Spawn(tools.Items[selectedRow]);
                    break;
                case Page.Editor:
                    if (selectedRow == 0)
                    {
                        mapBuilder.RefreshRoutes();
                        OpenFreecam(Camera.main, propEditor);
                    }
                    else if (selectedRow == 2)
                    {
                        tools.LoadItems();
                        page = Page.EditorItems;
                        selectedRow = 0;
                    }
                    else if (selectedRow == 4)
                    {
                        mapBuilder.Refresh();
                        if (OpenFreecam(Camera.main, mapEditor))
                        {
                            page = Page.Map;
                            selectedRow = 0;
                        }
                    }
                    else
                    {
                        mapBuilder.RefreshRoutes();
                        BeginPlacement(WorldPlacement.FromAim(tools, mapBuilder, Camera.main, selectedRow == 3, out status));
                    }
                    break;
                case Page.Map:
                    if (selectedRow == MapBuilder.Categories.Length)
                        status = mapBuilder.Save();
                    else if (selectedRow == MapBuilder.Categories.Length + 1)
                    {
                        mapBuilder.Refresh();
                        status = mapBuilder.Load();
                    }
                    else if (selectedRow == MapBuilder.Categories.Length + 2)
                    {
                        mapBuilder.RefreshRoutes();
                        OpenFreecam(Camera.main, mapEditor);
                    }
                    else
                    {
                        mapCategory = selectedRow;
                        mapBuilder.Show(mapCategory);
                        page = Page.MapAssets;
                        selectedRow = 0;
                    }
                    break;
                case Page.MapAssets:
                    Camera mapView = Camera.main;
                    if (!mapView || !tools.EditorAvailable(mapView))
                    {
                        status = "Stand on foot and put down any held items first.";
                        break;
                    }
                    var piece = mapBuilder.Spawn(mapBuilder.Visible[selectedRow], mapView);
                    if (!piece)
                        status = "This asset has no geometry available in this scene.";
                    else if (OpenFreecam(mapView, mapEditor))
                        mapEditor.SelectNewMapPiece(piece);
                    break;
                case Page.EditorItems:
                    var view = Camera.main;
                    if (!view || !tools.EditorAvailable(view))
                    {
                        status = "Stand on foot and put down any held items first.";
                        break;
                    }
                    GameObject spawned;
                    status = tools.Spawn(tools.Items[selectedRow], out spawned);
                    if (spawned)
                        BeginPlacement(WorldPlacement.FromSpawn(spawned, view));
                    break;
                case Page.Money:
                    if (selectedRow == 0)
                        status = tools.UpdateMoney(false) ? "Added 100." : "Wallet unavailable.";
                    else if (infiniteMoney)
                        infiniteMoney = false;
                    else
                    {
                        infiniteMoney = tools.UpdateMoney(true);
                        if (!infiniteMoney)
                            status = "Wallet unavailable.";
                    }
                    break;
            }
        }

        private bool OpenFreecam(Camera source, FreecamEditor editor)
        {
            if (!source || !tools.EditorAvailable(source))
            {
                status = "Stand on foot and put down any held items first.";
                return false;
            }
            EndMovement(true);
            DropWorldMesh();
            freecam = editor;
            freecam.Open(source, game.FindWalker(source));
            menuOpen = false;
            return true;
        }

        private static bool BeforeNativePickup()
        {
            return !current || !current.isActiveAndEnabled || !current.HandleWorldPickup();
        }

        private bool HandleWorldPickup()
        {
            if ((freecam != null && freecam.Active) || placement != null || !game.IsWalkingAvailable())
                return false;

            if (carriedMesh)
            {
                if (game.WorldItemActionPressed(true))
                    DropWorldMesh(false);
                return true;
            }
            if (!game.WorldItemActionPressed(false))
                return false;

            var camera = Camera.main;
            if (!camera || !tools.EditorAvailable(camera))
                return false;
            RaycastHit hit;
            if (!Physics.Raycast(camera.transform.position, camera.transform.forward, out hit, 3.5f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return false;

            var mesh = hit.collider.GetComponentInParent<WorldMeshPickup>();
            if (!mesh)
                return false;
            mesh.Take(camera, game.FindWalker(camera).transform);
            carriedMesh = mesh;
            return true;
        }

        private void LateUpdate()
        {
            if (!carriedMesh)
                return;
            var camera = Camera.main;
            var walker = game.FindWalker(camera);
            if (!camera || !walker || game.IsSeated(walker))
                DropWorldMesh();
            else if (game.IsWalkingAvailable())
                carriedMesh.Follow(camera);
        }

        private void DropWorldMesh(bool cancel = true)
        {
            if (carriedMesh && !carriedMesh.Drop(cancel))
            {
                carryStatusUntil = Time.unscaledTime + 3f;
                return;
            }
            carriedMesh = null;
        }

        private static bool BeforeClockUpdate(object __instance)
        {
            var plugin = current;
            return !plugin || plugin.environment == null || plugin.environment.AllowClockUpdate(__instance);
        }

        private static void BeforeNativeWeather(object __instance, out GameObject __state)
        {
            var plugin = current;
            __state = plugin && plugin.environment != null ? plugin.environment.NativeRainEmitter(__instance) : null;
        }

        private static void AfterNativeWeather(object __instance, GameObject __state)
        {
            var plugin = current;
            if (!plugin || plugin.environment == null)
                return;

            try { plugin.environment.AfterNativeWeather(__instance, __state); }
            catch (Exception exception) { plugin.Logger.LogWarning("Could not keep manual weather: " + exception); }
        }

        private void UnpatchEnvironment()
        {
            if (environment == null)
                return;
            if (environment.ClockUpdate != null)
                harmony.Unpatch(environment.ClockUpdate, HarmonyPatchType.Prefix, PluginId);
            if (environment.NativeSetWeather != null)
            {
                harmony.Unpatch(environment.NativeSetWeather, HarmonyPatchType.Prefix, PluginId);
                harmony.Unpatch(environment.NativeSetWeather, HarmonyPatchType.Postfix, PluginId);
            }
        }

        private void BeginPlacement(WorldPlacement next)
        {
            if (next == null)
                return;

            placement = next;
            menuOpen = false;
            status = null;
        }

        private void UpdatePlacement()
        {
            var view = Camera.main;
            bool reopen = Input.GetKeyDown(KeyCode.F7);
            if (!placement.IsAlive || !view || !game.IsWalkingAvailable() ||
                !tools.EditorAvailable(view) || reopen || Input.GetKeyDown(KeyCode.Backspace))
            {
                placement.Finish(true, false);
                placement = null;
                menuOpen = reopen;
                status = "Placement cancelled.";
                return;
            }

            float turn = ((Input.GetKey(KeyCode.L) ? 1f : 0f) - (Input.GetKey(KeyCode.J) ? 1f : 0f))
                * 85f * Time.unscaledDeltaTime;
            float tilt = ((Input.GetKey(KeyCode.K) ? 1f : 0f) - (Input.GetKey(KeyCode.I) ? 1f : 0f))
                * 85f * Time.unscaledDeltaTime;
            placement.Preview(view, Input.mouseScrollDelta.y, turn, tilt);
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                bool freeze = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                placement.Finish(false, freeze);
                placement = null;
                status = "Object placed.";
            }
        }

        private void ActivateVehicleRow()
        {
            switch (selectedRow)
            {
                case 0:
                    status = tools.RepairCar();
                    break;
                case 1:
                    status = tools.FillTank(false) ? "Fuel tank filled." : "Install a fuel tank first.";
                    break;
                case 2:
                    if (infiniteFuel)
                        infiniteFuel = false;
                    else
                    {
                        infiniteFuel = tools.FillTank(false);
                        if (!infiniteFuel)
                            status = "Install a fuel tank first.";
                    }
                    break;
                case 3:
                    if (carNoclip == null)
                    {
                        var player = tools.Player;
                        carNoclip = VehicleNoclip.TryStart(tools.DrivenCar(), player ? player.transform : null, out status);
                    }
                    else if (carNoclip.TryStop(false, out status))
                        carNoclip = null;
                    break;
                case 4:
                    status = tools.WashCar();
                    break;
                case 5:
                    status = tools.InstallAllAccessories();
                    break;
                case 6:
                    page = Page.Paint;
                    selectedRow = 0;
                    break;
            }
        }

        private void RefreshCheats()
        {
            if ((!infiniteMoney && !infiniteFuel) || !game.IsWalkingAvailable() || Time.unscaledTime < nextRefresh)
                return;

            nextRefresh = Time.unscaledTime + 0.25f;
            try
            {
                if (infiniteMoney)
                    tools.UpdateMoney(true);
                if (infiniteFuel)
                    tools.FillTank(true);
            }
            catch (Exception exception)
            {
                infiniteFuel = infiniteMoney = false;
                status = "Could not refresh money or fuel. See BepInEx logs.";
                Logger.LogError(exception);
            }
        }

        private void FixedUpdate()
        {
            if (carNoclip == null)
                return;

            var body = carNoclip.Body;
            if (tools.DrivenCar() != body || !body)
            {
                StopCarNoclip();
                return;
            }
            if (game.IsWalkingAvailable())
                carNoclip.Step(game.ReadDriving(menuOpen), Input.GetKey(KeyCode.Space), Time.fixedDeltaTime);
        }

        private static void BeforeDrivingStop(object __instance)
        {
            var plugin = current;
            if (plugin && plugin.carNoclip != null && plugin.tools.IsPlayerCar(__instance))
                plugin.StopCarNoclip();
        }

        private void StopCarNoclip()
        {
            if (carNoclip != null)
            {
                carNoclip.TryStop(true, out status);
                carNoclip = null;
            }
        }

        private void OnGUI()
        {
            if (freecam != null && freecam.Active && freecam.HasView)
            {
                freecam.Draw();
                return;
            }

            if (placement != null)
            {
                GUI.Label(new Rect(Screen.width * 0.5f - 5f, Screen.height * 0.5f - 10f, 18f, 20f), "+");
                GUILayout.BeginArea(new Rect(20f, 20f, 460f, 100f), GUI.skin.box);
                GUILayout.Label("World Editor / " + placement.Name);
                GUILayout.Label("Aim to move   Scroll distance   J / L yaw   I / K pitch");
                GUILayout.Label("Enter place   Shift + Enter freeze   Backspace cancel");
                GUILayout.EndArea();
                return;
            }

            if (!menuOpen)
            {
                if (carriedMesh && Time.unscaledTime < carryStatusUntil)
                    GUI.Box(new Rect(20f, 20f, 360f, 36f), carriedMesh.IsVehicle
                        ? "Move to clear, solid ground to place the Laika."
                        : "Move to a clear spot to drop the object.");
                return;
            }

            float menuHeight = page == Page.Paint ? 420f : (IsItemPage || page == Page.MapAssets) ? 340f : 270f;
            GUILayout.BeginArea(new Rect(20f, 20f, 410f, menuHeight), GUI.skin.box);
            GUILayout.Label(page == Page.Main ? "Jalopy Mod Menu" : "Jalopy Mod Menu / " + page);
            GUILayout.Space(6f);
            DrawPage();
            GUILayout.Space(5f);
            GUILayout.Label("Up / Down   Select     Enter   Use     F7   Close");
            if (page != Page.Main)
                GUILayout.Label("Backspace   Back");
            if (page == Page.Movement)
                GUILayout.Label("Left / Right   Speed     Space / Ctrl   Altitude     Shift   Boost");
            if (IsItemPage)
                GUILayout.Label("Left / Right   Jump " + VisibleItems + " items");
            if (!string.IsNullOrEmpty(status))
                GUILayout.Label(status);
            GUILayout.EndArea();
        }

        private void DrawPage()
        {
            switch (page)
            {
                case Page.Time:
                    GUILayout.Label("World time   " + (environment != null ? environment.TimeLabel() : "--:--"));
                    DrawRow(0, "Morning", "08:00");
                    DrawRow(1, "Midday", "12:00");
                    DrawRow(2, "Sunset", "18:30");
                    DrawRow(3, "Night", "22:00");
                    DrawRow(4, "Freeze time", ToggleText(environment != null && environment.IsFrozen));
                    break;
                case Page.Weather:
                    WeatherMode mode = environment != null ? environment.CurrentMode : WeatherMode.Automatic;
                    DrawRow(0, "Automatic", mode == WeatherMode.Automatic ? "ACTIVE" : "");
                    DrawRow(1, "Sunny", mode == WeatherMode.Sunny ? "ACTIVE" : "");
                    DrawRow(2, "Fog", mode == WeatherMode.Fog ? "ACTIVE" : "");
                    DrawRow(3, "Rain", mode == WeatherMode.Rain ? "ACTIVE" : "");
                    DrawRow(4, "Storm", mode == WeatherMode.Storm ? "ACTIVE" : "");
                    break;
                case Page.Movement:
                    DrawRow(0, "Fly", ToggleText(movement != null && movement.Fly));
                    DrawRow(1, "Noclip", ToggleText(movement != null && movement.Noclip));
                    DrawRow(2, "Speed", speed.ToString("0") + " m/s");
                    break;
                case Page.Vehicle:
                    DrawRow(0, "Repair Laika", "");
                    DrawRow(1, "Fill fuel tank", "");
                    DrawRow(2, "Infinite fuel", ToggleText(infiniteFuel));
                    DrawRow(3, "Car noclip", ToggleText(carNoclip != null));
                    DrawRow(4, "Wash Laika", "");
                    DrawRow(5, "Max Upgrades", "");
                    DrawRow(6, "Paint Laika", "");
                    break;
                case Page.Paint:
                    for (int i = 0; i < PaintNames.Length; i++)
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label((selectedRow == i ? ">  " : "    ") + PaintNames[i]);
                        GUILayout.FlexibleSpace();
                        Color original = GUI.color;
                        GUI.color = PaintColors[i];
                        GUILayout.Label(Texture2D.whiteTexture, GUILayout.Width(24f), GUILayout.Height(14f));
                        GUI.color = original;
                        GUILayout.EndHorizontal();
                    }
                    break;
                case Page.Money:
                    DrawRow(0, "Add 100", "");
                    DrawRow(1, "Infinite money", ToggleText(infiniteMoney));
                    break;
                case Page.Editor:
                    DrawRow(0, "Open Freecam / Gizmo", "");
                    DrawRow(1, "Grab aimed object", "");
                    DrawRow(2, "Spawn and place", "");
                    DrawRow(3, "Duplicate aimed prop", "");
                    DrawRow(4, "Map Builder", "");
                    break;
                case Page.Map:
                    for (int i = 0; i < MapBuilder.Categories.Length; i++)
                        DrawRow(i, MapBuilder.Categories[i], "");
                    DrawRow(MapBuilder.Categories.Length, "Save custom map", "");
                    DrawRow(MapBuilder.Categories.Length + 1, "Load saved map", "Replaces placed pieces");
                    DrawRow(MapBuilder.Categories.Length + 2, "Open Map Editor", "");
                    break;
                case Page.MapAssets:
                    var assets = mapBuilder.Visible;
                    if (assets.Count == 0)
                    {
                        GUILayout.Label("No assets from this category are loaded here.");
                        break;
                    }
                    int start = Mathf.Clamp(selectedRow - VisibleItems / 2, 0, Mathf.Max(0, assets.Count - VisibleItems));
                    GUILayout.Label(MapBuilder.Categories[mapCategory] + "   " + (selectedRow + 1) + " / " + assets.Count);
                    for (int i = start; i < Mathf.Min(start + VisibleItems, assets.Count); i++)
                        DrawRow(i, assets[i].Name, "");
                    GUILayout.Label("Left / Right   Jump " + VisibleItems + " assets");
                    break;
                case Page.EditorItems:
                case Page.Items:
                    int total = RowCount();
                    if (total == 0)
                    {
                        GUILayout.Label("No items available. Open the list while in game.");
                        break;
                    }
                    int first = Mathf.Clamp(selectedRow - VisibleItems / 2, 0, Mathf.Max(0, total - VisibleItems));
                    GUILayout.Label((selectedRow + 1) + " / " + total +
                        (page == Page.EditorItems ? "    Enter to spawn and position" : "    Enter to spawn in front of you"));
                    for (int i = first; i < Mathf.Min(first + VisibleItems, total); i++)
                    {
                        var item = tools.Items[i];
                        DrawRow(i, item.Name, "#" + item.Id);
                    }
                    break;
                default:
                    DrawRow(0, "Movement", "");
                    DrawRow(1, "Vehicle", "");
                    DrawRow(2, "Items / Give", "");
                    DrawRow(3, "Money", "");
                    DrawRow(4, "World Editor", "");
                    DrawRow(5, "Time Control", "");
                    DrawRow(6, "Weather Control", "");
                    break;
            }
        }

        private static string ToggleText(bool enabled)
        {
            return enabled ? "ON" : "OFF";
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
            if (!plugin)
                return true;
            var walker = __instance as Behaviour;
            if (plugin.freecam != null && plugin.freecam.Active && plugin.freecam.Walker == walker)
                return false;
            if (plugin.movement == null || plugin.movement.Walker != walker)
                return true;

            return plugin.RunMovement();
        }

        private bool RunMovement()
        {
            if (!ValidateMovement())
                return true;

            var view = Camera.main;
            if (!view || !game.IsWalkingAvailable())
            {
                movement.Move(Vector3.zero);
                return false;
            }

            Vector2 input = game.ReadMovement(menuOpen);
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
            DropWorldMesh();
            if (environment != null)
            {
                try { environment.Release(); }
                catch (Exception exception) { Logger.LogWarning(exception); }
            }
            bool seated = movement != null && movement.Walker && game != null && game.IsSeated(movement.Walker);
            EndMovement(!seated);
            if (freecam != null && freecam.Active)
                freecam.Close();
            if (placement != null)
            {
                placement.Finish(true, false);
                placement = null;
            }
            StopCarNoclip();
            menuOpen = false;
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(current, this))
            {
                current = null;
                harmony.Unpatch(game.WalkingTick, HarmonyPatchType.Prefix, PluginId);
                if (tools.StopDriving != null)
                    harmony.Unpatch(tools.StopDriving, HarmonyPatchType.Prefix, PluginId);
                if (tools.NativePickupUpdate != null)
                    harmony.Unpatch(tools.NativePickupUpdate, HarmonyPatchType.Prefix, PluginId);
                UnpatchEnvironment();
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JalopyModMenu
{
    internal sealed class GameTools
    {
        private const float MoneyFloor = 10000f;
        private static readonly string[] EngineParts = {
            "installedEngine", "installedIgnitionCoil", "installedCarburettor",
            "installedFuelTank", "installedAirFilter", "installedBattery", "waterTankObj"
        };
        private static readonly string[] Tyres = {
            "frontLeftTyre", "frontRightTyre", "rearLeftTyre", "rearRightTyre"
        };
        private static readonly string[] DirtOverlays = {
            "frontWindowDirtWipers", "frontWindowDirtRim", "frontWindowDirtSmudge",
            "frontLeftWindowObj", "frontRightWindowObj", "rearLeftWindowObj", "rearRightWindowObj",
            "rearWindowObject", "flWheelDirt", "frWheelDirt", "rlWheelDirt", "rrWheelDirt",
            "frntDirtObj", "rearDirtObj", "frntlsideDirtObj", "frntrsideDirtObj",
            "doorlsideDirtObj", "doorlsideDirtObj2", "doorrsideDirtObj", "doorrsideDirtObj2",
            "rearlsideDirtObj", "rearrsideDirtObj"
        };
        // IDs 0–4 match ExtraComponentC.ActionPart2. The tool rack uses IDs 5–7.
        private static readonly string[] AccessorySlots = { "roofRack", "bullBar", "mudGuards", "dashUI", "lightRack" };
        private static readonly string[] AccessoryInstallers = {
            "RoofRackFitted", "BullBarFitted", "MudGuardsFitted", "DigitalDashFitted", "LightRackFitted"
        };

        private readonly GameBindings game;
        private readonly Type carLogicType;
        private readonly Type performanceType;
        private readonly Type engineComponentType;
        private readonly Type pickupType;
        private readonly Type directorType;
        private readonly Type walletType;
        private readonly Type dragType;
        private readonly Type extrasType;
        private readonly Type extraReceiverType;
        private readonly Type extraKitType;
        private readonly MethodInfo stopColourTween;

        public readonly List<GiveItem> Items = new List<GiveItem>();
        public readonly MethodInfo StopDriving;
        public readonly MethodInfo NativePickupUpdate;
        public GameObject Player { get { return game.Menu ? Read<GameObject>(game.Menu, "player") : null; } }
        public GameObject Car { get { return game.Menu ? Read<GameObject>(game.Menu, "carController") : null; } }

        public GameTools(GameBindings game)
        {
            this.game = game;
            var assembly = Assembly.Load("Assembly-CSharp");
            carLogicType = assembly.GetType("CarLogicC", true);
            performanceType = assembly.GetType("CarPerformanceC", true);
            engineComponentType = assembly.GetType("EngineComponentC", true);
            pickupType = assembly.GetType("ObjectPickupC", true);
            directorType = assembly.GetType("DirectorC", true);
            walletType = assembly.GetType("WalletC", true);
            dragType = assembly.GetType("DragRigidbodyC", true);
            extrasType = assembly.GetType("ExtraUpgradesC", true);
            extraReceiverType = assembly.GetType("ExtraReceiverC", true);
            extraKitType = assembly.GetType("ExtraComponentC", true);
            var firstPass = Assembly.Load("Assembly-CSharp-firstpass");
            var tween = firstPass.GetType("iTween", false);
            stopColourTween = tween != null ? AccessTools.Method(tween, "Stop",
                new[] { typeof(GameObject), typeof(string) }) : null;
            StopDriving = AccessTools.Method(carLogicType, "DrivingStop", new[] { typeof(bool) });
            NativePickupUpdate = AccessTools.Method(dragType, "Update");
        }

        public string PaintLaika(Color colour, string name)
        {
            var logic = ComponentFrom(game.Menu, "carLogic", carLogicType);
            if (!logic)
                return "The Laika is not available.";

            var panels = new HashSet<Renderer>();
            AddPaintPanel(logic.gameObject, panels);
            foreach (string field in new[] { "carInterior", "lHLight", "rHLight", "roof" })
                AddPaintPanel(Read<GameObject>(logic, field), panels);
            foreach (string field in new[] { "bonnetObj", "leftDoor", "rightDoor", "bootLid" })
            {
                var panel = Read<GameObject>(logic, field);
                if (panel && panel.transform.parent)
                    AddPaintPanel(panel.transform.parent.gameObject, panels);
            }

            int painted = 0;
            foreach (var panel in panels)
            {
                var material = panel.material;
                if (!material || !material.HasProperty("_Color"))
                    continue;
                Color tint = colour;
                tint.a = material.color.a;
                material.color = tint;
                if (material.HasProperty("_SpecColor"))
                    material.SetColor("_SpecColor", Color.black);
                painted++;
            }
            return painted > 0 ? "Laika painted " + name + "." : "No paintable panels found.";
        }

        private void AddPaintPanel(GameObject target, HashSet<Renderer> panels)
        {
            if (!target)
                return;
            var renderer = target.GetComponent<Renderer>();
            if (renderer)
                panels.Add(renderer);
            var relayType = carLogicType.Assembly.GetType("PaintCanRelayC", true);
            var relay = target.GetComponent(relayType);
            if (!relay)
                return;
            var linked = Read<GameObject[]>(relay, "targets");
            if (linked == null)
                return;
            foreach (var part in linked)
            {
                var surface = part ? part.GetComponent<Renderer>() : null;
                if (surface)
                    panels.Add(surface);
            }
        }

        public bool IsNativePickup(Transform target)
        {
            return target.GetComponentInParent(pickupType);
        }

        public string WashCar()
        {
            var logic = ComponentFrom(game.Menu, "carLogic", carLogicType);
            if (!logic)
                return "The Laika is not available.";

            var cleaned = new HashSet<Renderer>();
            foreach (string name in DirtOverlays)
                ClearDirt(Read<GameObject>(logic, name), cleaned);

            var extras = logic.GetComponent(extrasType);
            if (extras)
            {
                ClearDirt(Read<GameObject>(extras, "bullBarMudTarget"), cleaned);
                var guards = Read<GameObject[]>(extras, "mudGuardMudTargets");
                if (guards != null)
                {
                    foreach (var overlay in guards)
                        ClearDirt(overlay, cleaned);
                }
            }
            return cleaned.Count > 0 ? "Laika washed: body, windows and tyres." : "No dirt overlays found.";
        }

        private void ClearDirt(GameObject target, HashSet<Renderer> cleaned)
        {
            var renderer = target ? target.GetComponent<Renderer>() : null;
            if (!renderer || cleaned.Contains(renderer))
                return;

            var material = renderer.material;
            if (!material || !material.HasProperty("_Color"))
                return;

            if (stopColourTween != null)
                stopColourTween.Invoke(null, new object[] { target, "color" });
            Color colour = material.color;
            colour.a = 0f;
            material.color = colour;
            cleaned.Add(renderer);
        }

        public string InstallAllAccessories()
        {
            var extras = ComponentFrom(game.Menu, "carLogic", extrasType);
            var performance = extras ? extras.GetComponent(performanceType) : null;
            if (!extras || !performance)
                return "The Laika is not available.";

            var weights = new int?[8];
            // Include inactive shop prefabs so the native install methods use the actual kit weights.
            foreach (var candidate in Resources.FindObjectsOfTypeAll(extraKitType))
            {
                var kit = candidate as Component;
                if (!kit || Read<bool>(kit, "isCustomDecal"))
                    continue;
                int id = Read<int>(kit, "componentID");
                if (id < 0 || id >= weights.Length || weights[id].HasValue)
                    continue;
                var part = kit.GetComponent(engineComponentType);
                if (part)
                    weights[id] = Mathf.RoundToInt(Read<float>(part, "weight"));
            }

            int fitted = 0;
            int available = 0;
            for (int i = 0; i < AccessorySlots.Length; i++)
            {
                string slot = AccessorySlots[i];
                if (Read<bool>(extras, slot + "Installed"))
                {
                    available++;
                    continue;
                }
                if (!ComponentFrom(extras, slot, extraReceiverType))
                    continue;

                Call(extras, AccessoryInstallers[i], weights[i] ?? Read<int>(extras, slot + "Weight"));
                fitted++;
                available++;
            }

            bool rackInstalled = Read<bool>(extras, "toolRackInstalled");
            int rackLevel = Read<int>(extras, "toolRackLevel");
            if (rackInstalled && rackLevel >= 3)
                available++;
            else if (ComponentFrom(extras, "toolRack", extraReceiverType))
            {
                int oldWeight = Read<int>(extras, "toolRackWeight");
                int weight = weights[7] ?? weights[5] ?? oldWeight;
                if (!rackInstalled || rackLevel == 0)
                    Call(extras, "ToolRackLoad", weight, 3);
                else
                {
                    Call(extras, "ToolRackFitted", weight, 3);
                    // The native upgrade call only adds to total weight when installing level 1.
                    int difference = weight - oldWeight;
                    Write(performance, "carExtrasWeight", Read<float>(performance, "carExtrasWeight") + difference);
                    Write(performance, "totalCarWeight", Read<float>(performance, "totalCarWeight") + difference);
                }
                fitted++;
                available++;
            }

            if (available != 6)
                return "Accessories installed: " + available + "/6. Some mounts are unavailable here.";
            return fitted > 0 ? "All accessories installed. Tool rack level 3." : "All accessories are already at their maximum.";
        }

        public bool IsPlayerCar(object logic)
        {
            return ComponentFrom(game.Menu, "carLogic", carLogicType) == logic as Component;
        }

        public Rigidbody DrivenCar()
        {
            var menu = game.Menu;
            if (!menu)
                return null;

            var logic = ComponentFrom(menu, "carLogic", carLogicType);
            if (!logic || !Read<bool>(logic, "isDriving"))
                return null;

            var controller = Read<GameObject>(menu, "carController");
            return controller ? controller.GetComponent<Rigidbody>() : null;
        }

        public string RepairCar()
        {
            var menu = game.Menu;
            var performance = ComponentFrom(menu, "carLogic", performanceType);
            var logic = ComponentFrom(menu, "carLogic", carLogicType);
            if (!performance || !logic)
                return "The Laika is not available.";

            int count = 0;
            foreach (string part in EngineParts)
                count += RepairPart(performance, part, false) ? 1 : 0;
            foreach (string tyre in Tyres)
                count += RepairPart(performance, tyre, true) ? 1 : 0;

            var waterTank = ComponentFrom(performance, "waterTankObj", engineComponentType);
            if (waterTank)
                Write(waterTank, "currentWaterCharges", Read<int>(waterTank, "totalWaterCharges"));

            RechargeBattery(performance);
            Write(logic, "damageLvl", 0);
            Call(logic, "LowerEngineDamage");
            Call(logic, "EngineDamageNone");
            Call(logic, "CarDamageNone");
            Call(performance, "GetCarCondition");
            Call(performance, "UpdateIgnitionCoil");
            Call(performance, "UpdateFuelEconomy");
            Call(performance, "UpdateAirFilter");
            return "Laika repaired (" + count + " installed parts and tyres).";
        }

        private void RechargeBattery(Component performance)
        {
            var battery = ComponentFrom(performance, "installedBattery", engineComponentType);
            if (!battery)
                return;

            var catalogue = Read<GameObject[]>(performance, "batteryCatalogue");
            int id = Read<int>(battery, "loadID");
            if (catalogue == null || id < 0 || id >= catalogue.Length || !catalogue[id])
                return;

            var template = catalogue[id].GetComponent(engineComponentType);
            if (!template)
                return;

            float charge = Read<float>(template, "charge");
            if (charge > 0f)
            {
                Write(battery, "charge", Mathf.Max(charge, Read<float>(battery, "charge")));
                Write(battery, "batteryDepleted", false);
            }
        }

        private bool RepairPart(Component performance, string field, bool tyre)
        {
            var item = ComponentFrom(performance, field, engineComponentType);
            if (!item)
                return false;

            if (tyre && Read<bool>(item, "hasHitBrokenCondition"))
                Call(item, "TyreRepairKitActions");
            Call(item, "set_Condition", Read<float>(item, "durability"));
            Call(item, "UpdateCondition");
            var pickup = item.GetComponent(pickupType);
            if (pickup)
                Call(pickup, "SetPriceTag");
            return true;
        }

        public bool FillTank(bool continuous)
        {
            var performance = ComponentFrom(game.Menu, "carLogic", performanceType);
            var tank = ComponentFrom(performance, "installedFuelTank", engineComponentType);
            if (!tank)
                return false;

            float capacity = Read<float>(tank, "totalFuelAmount");
            if (capacity <= 0f)
                return false;
            if (!continuous || Read<float>(tank, "currentFuelAmount") < capacity)
                Write(tank, "currentFuelAmount", capacity);
            return true;
        }

        public bool UpdateMoney(bool infinite)
        {
            var director = ComponentFrom(game.Menu, "director", directorType);
            var wallet = ComponentFrom(director, "wallet", walletType);
            if (!wallet)
                return false;

            float money = Read<float>(director, "totalWealth");
            float walletMoney = Read<float>(wallet, "totalWealth");
            if (infinite && money >= MoneyFloor && walletMoney >= MoneyFloor)
                return true;

            float newTotal = infinite ? Mathf.Max(money, MoneyFloor) : money + 100f;
            Call(wallet, "set_TotalWealth", newTotal);
            Write(director, "totalWealth", newTotal);
            Call(wallet, "UpdateWealth");
            return true;
        }

        public void LoadItems()
        {
            Items.Clear();
            var menu = game.Menu;
            var catalogue = menu ? Read<GameObject[]>(menu, "objectIDCatalogue") : null;
            if (catalogue == null)
                return;

            var seen = new HashSet<GameObject>();
            for (int i = 0; i < catalogue.Length; i++)
            {
                var prefab = catalogue[i];
                if (!prefab || (prefab.scene.IsValid() && prefab.activeInHierarchy) || !seen.Add(prefab))
                    continue;

                var pickup = prefab.GetComponent(pickupType);
                if (!pickup)
                    continue;

                string name = Read<string>(pickup, "supplyName");
                if (string.IsNullOrEmpty(name) || name == "None")
                    name = prefab.name.Replace("_", " ");

                Items.Add(new GiveItem(name, i, prefab));
            }

            Items.Sort(delegate(GiveItem a, GiveItem b) {
                int order = StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name);
                return order != 0 ? order : a.Id.CompareTo(b.Id);
            });
        }

        public string Spawn(GiveItem entry)
        {
            GameObject ignored;
            return Spawn(entry, out ignored);
        }

        public string Spawn(GiveItem entry, out GameObject spawned)
        {
            spawned = null;
            var view = Camera.main;
            var walker = game.FindWalker(view);
            if (!walker || game.IsSeated(walker))
                return "Get out of the car to spawn items.";
            if (!entry.Prefab)
                return "This item is no longer available.";

            Vector3 forward = Vector3.ProjectOnPlane(view.transform.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.01f)
                forward = walker.transform.forward;
            forward.Normalize();

            RaycastHit hit;
            if (Physics.Raycast(view.transform.position, forward, out hit, 2.2f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return "Look toward an open space before spawning an item.";

            Vector3 position = view.transform.position + forward * 2.2f;
            if (Physics.Raycast(position, Vector3.down, out hit, 4f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                position = hit.point + Vector3.up * 0.55f;

            spawned = Object.Instantiate(entry.Prefab, position, Quaternion.identity);
            var pickup = spawned.GetComponent(pickupType);
            Write(pickup, "isPurchased", true);
            Write(pickup, "isInShoppingKart", false);
            Write(pickup, "isInEngine", false);
            Write(pickup, "forceCollision", true);
            spawned.transform.eulerAngles = Read<Vector3>(pickup, "throwRotAdjustment");
            var component = spawned.GetComponent(engineComponentType);
            if (component)
                Write(component, "isTutorialObj", false);
            Call(pickup, "ForceCollisions");
            var body = spawned.GetComponent<Rigidbody>();
            if (body)
            {
                body.isKinematic = false;
                body.detectCollisions = true;
                body.WakeUp();
            }
            if (!spawned.activeSelf)
                spawned.SetActive(true);
            return "Spawned " + entry.Name + ".";
        }

        public bool EditorAvailable(Camera view)
        {
            var walker = game.FindWalker(view);
            if (!walker || game.IsSeated(walker))
                return false;

            var drag = AccessTools.Field(dragType, "Global").GetValue(null) as Component;
            return !drag || (!Read<GameObject>(drag, "isHolding1") &&
                !Read<GameObject>(drag, "isHolding2") && !Read<GameObject>(drag, "isHolding3"));
        }

        public Transform EditorTarget(Collider collider)
        {
            var pickup = collider.GetComponentInParent(pickupType);
            if (pickup)
            {
                if (Read<bool>(pickup, "isInEngine") || Read<bool>(pickup, "isInGateLock") ||
                    Read<bool>(pickup, "isInShoppingKart") || Read<Transform>(pickup, "inventoryPlacedAt") ||
                    Read<Transform>(pickup, "bootPlacedAt"))
                    return null;

                return pickup.transform;
            }

            if (collider.attachedRigidbody)
                return collider.attachedRigidbody.transform;

            var prop = collider.GetComponentInParent<EditorProp>();
            if (prop)
                return prop.transform;

            if (collider.GetComponent<MeshFilter>())
                return collider.transform;
            var parent = collider.transform.parent;
            return parent && parent.GetComponent<MeshFilter>() ? parent : null;
        }

        public Behaviour[] FreecamInput(Behaviour walker)
        {
            var mouseLook = Assembly.Load("Assembly-CSharp").GetType("MouseLook", true);
            var scripts = new List<Behaviour>();
            foreach (var script in walker.GetComponentsInChildren(mouseLook))
            {
                var behaviour = script as Behaviour;
                if (behaviour && behaviour.enabled)
                    scripts.Add(behaviour);
            }
            var drag = AccessTools.Field(dragType, "Global").GetValue(null) as Behaviour;
            if (drag && drag.enabled)
                scripts.Add(drag);
            return scripts.ToArray();
        }

        private static Component ComponentFrom(Component source, string field, Type type)
        {
            var target = source ? Read<GameObject>(source, field) : null;
            return target ? target.GetComponent(type) : null;
        }

        internal static T Read<T>(Component component, string name)
        {
            return (T)AccessTools.Field(component.GetType(), name).GetValue(component);
        }

        internal static void Write(Component component, string name, object value)
        {
            AccessTools.Field(component.GetType(), name).SetValue(component, value);
        }

        internal static void Call(Component component, string name, params object[] args)
        {
            AccessTools.Method(component.GetType(), name).Invoke(component, args);
        }

        internal sealed class GiveItem
        {
            public readonly string Name;
            public readonly int Id;
            public readonly GameObject Prefab;

            public GiveItem(string name, int id, GameObject prefab)
            {
                Name = name;
                Id = id;
                Prefab = prefab;
            }
        }
    }
}

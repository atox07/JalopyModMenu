using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JalopyModMenu
{
    internal sealed class MapBuilder
    {
        public static readonly string[] Categories = { "Roads & junctions", "Terrain", "Buildings", "Models" };
        private readonly GameBindings game;
        private readonly Type routeType = Assembly.Load("Assembly-CSharp").GetType("RouteGeneratorC");
        private readonly List<Entry> entries = new List<Entry>();
        private readonly List<Entry> visible = new List<Entry>();
        private readonly Dictionary<string, Entry> byKey = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly HashSet<Mesh> routeMeshes = new HashSet<Mesh>();
        private readonly HashSet<Transform> worldGeometry = new HashSet<Transform>();
        private readonly List<MapTerrainPiece> pieces = new List<MapTerrainPiece>();
        private string SavePath { get { return Path.Combine(Application.persistentDataPath, "JalopyCustomMap.json"); } }
        public List<Entry> Visible { get { return visible; } }

        public MapBuilder(GameBindings game)
        {
            this.game = game;
        }

        public void Refresh()
        {
            entries.Clear();
            visible.Clear();
            byKey.Clear();
            RefreshRoutes(true);

            var seenRoots = new HashSet<GameObject>();
            foreach (var terrain in Resources.FindObjectsOfTypeAll<Terrain>())
            {
                if (terrain && terrain.terrainData && !terrain.GetComponentInParent<MapTerrainPiece>())
                    RegisterGroup(terrain.gameObject, "terrain:" + terrain.terrainData.name, 1, seenRoots);
            }
            foreach (var filter in Resources.FindObjectsOfTypeAll<MeshFilter>())
            {
                if (!filter)
                    continue;
                var mesh = filter.sharedMesh;
                var renderer = filter.GetComponent<MeshRenderer>();
                if (!mesh || !renderer || !renderer.enabled || renderer.isPartOfStaticBatch ||
                    filter.GetComponentInParent<MapTerrainPiece>())
                    continue;
                var materials = renderer.sharedMaterials;
                string key = "model:" + mesh.name;
                foreach (var material in materials)
                    key += "|" + (material ? material.name.Replace(" (Instance)", "") : "");
                Add(new Entry(key, mesh.name, Classify(filter.name + " " + mesh.name), filter.gameObject, true));
            }
            entries.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
        }

        public void RefreshRoutes(bool catalog = false)
        {
            routeMeshes.Clear();
            worldGeometry.RemoveWhere(target => !target);
            var seenRoots = new HashSet<GameObject>();
            var menu = game.Menu;
            var director = menu ? GameTools.Read<GameObject>(menu, "director") : null;
            var route = director && routeType != null ? director.GetComponent(routeType) : null;
            if (!route && routeType != null)
                route = Object.FindObjectOfType(routeType) as Component;
            if (route)
            {
                foreach (var field in route.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    string fieldName = field.Name.ToLowerInvariant();
                    int category;
                    if (fieldName.StartsWith("segmentlibrary") || fieldName.StartsWith("roundabout"))
                        category = 0;
                    else if (fieldName.StartsWith("destinationobjects") || fieldName.StartsWith("stopoff") ||
                        fieldName == "sceneryindustrial" || fieldName.StartsWith("sceneryresidential"))
                        category = 2;
                    else if (fieldName.StartsWith("scenery") || fieldName == "yugobackdrop")
                        category = 1;
                    else
                        continue;

                    var group = field.GetValue(route);
                    var prefabs = group as GameObject[];
                    if (prefabs != null)
                    {
                        for (int i = 0; i < prefabs.Length; i++)
                            RegisterRoute(prefabs[i], "route:" + field.Name + ":" + i, category, seenRoots, catalog);
                    }
                    else
                        RegisterRoute(group as GameObject, "route:" + field.Name, category, seenRoots, catalog);
                }
            }
        }

        private void RegisterRoute(GameObject source, string key, int category, HashSet<GameObject> seen, bool catalog)
        {
            if (!source || !seen.Add(source))
                return;
            var filters = source.GetComponentsInChildren<MeshFilter>(true);
            foreach (var filter in filters)
            {
                if (filter.sharedMesh)
                    routeMeshes.Add(filter.sharedMesh);
            }
            if (catalog && (filters.Length != 0 || source.GetComponentInChildren<Terrain>(true)))
                Add(new Entry(key, source.name, category, source, false));
        }

        public bool IsMapGeometry(Transform target)
        {
            if (!target)
                return false;
            if (target.GetComponentInParent<MapTerrainPiece>() || target.GetComponentInParent<Terrain>())
                return true;
            if (target.GetComponentInParent<Rigidbody>() || target.GetComponentInParent<EditorProp>())
                return false;
            if (worldGeometry.Contains(target))
                return true;

            var filter = target.GetComponent<MeshFilter>();
            if (!filter || !filter.sharedMesh)
                return false;
            var size = filter.sharedMesh.bounds.size;
            var scale = target.lossyScale;
            Vector3 dimensions = new Vector3(Mathf.Abs(size.x * scale.x), Mathf.Abs(size.y * scale.y),
                Mathf.Abs(size.z * scale.z));
            int category = Classify(target.name + " " + filter.sharedMesh.name);
            bool onRoute = routeMeshes.Contains(filter.sharedMesh);
            bool map = IsEnvironmentMesh(category, onRoute, dimensions);
            if (!map)
            {
                var renderer = target.GetComponent<MeshRenderer>();
                map = renderer && IsEnvironmentMesh(category, onRoute, renderer.bounds.size);
            }
            if (map)
                worldGeometry.Add(target);
            return map;
        }

        internal static bool IsEnvironmentMesh(int category, bool onRoute, Vector3 size)
        {
            float span = Mathf.Max(size.x, size.z);
            float width = Mathf.Min(size.x, size.z);
            if (category == 0)
                return span >= 3f && width >= 0.75f;
            if (category == 1)
                return span >= 4f && width >= 2f;
            if (category == 2)
                return span >= 3f && (width >= 1.5f || size.y >= 2.5f);
            // Route prefabs also contain props. Reserve only large structural surfaces.
            return onRoute && span >= 7f && (width >= 3f || size.y >= 3f);
        }

        private void RegisterGroup(GameObject source, string key, int category, HashSet<GameObject> seen)
        {
            if (source && (source.GetComponentInChildren<MeshFilter>(true) ||
                source.GetComponentInChildren<Terrain>(true)) && seen.Add(source))
                Add(new Entry(key, source.name, category, source, false));
        }

        private void Add(Entry entry)
        {
            if (byKey.ContainsKey(entry.Key))
                return;
            byKey.Add(entry.Key, entry);
            entries.Add(entry);
        }

        private static int Classify(string name)
        {
            string lower = name.ToLowerInvariant();
            if (lower.Contains("road") || lower.Contains("junction") || lower.Contains("roundabout") ||
                lower.Contains("asphalt") || lower.Contains("bridge"))
                return 0;
            if (lower.Contains("terrain") || lower.Contains("ground") || lower.Contains("hill") ||
                lower.Contains("cliff") || lower.Contains("landscape"))
                return 1;
            if (lower.Contains("building") || lower.Contains("house") || lower.Contains("garage") ||
                lower.Contains("motel") || lower.Contains("warehouse") || lower.Contains("factory") ||
                lower.Contains("shop") || lower.Contains("station"))
                return 2;
            return 3;
        }

        public void Show(int category)
        {
            visible.Clear();
            foreach (var entry in entries)
            {
                if (entry.Category == category)
                    visible.Add(entry);
            }
        }

        public MapTerrainPiece Spawn(Entry entry, Camera view)
        {
            var piece = Create(entry);
            if (!piece)
                return null;

            var bounds = piece.Bounds();
            Vector3 forward = Vector3.ProjectOnPlane(view.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.forward;
            float extent = Mathf.Abs(forward.x) * bounds.extents.x + Mathf.Abs(forward.z) * bounds.extents.z;
            Vector3 anchor = view.transform.position + forward * (extent + 5f);
            RaycastHit hit;
            if (Physics.Raycast(anchor + Vector3.up * 10f, Vector3.down, out hit, 100f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                anchor.y = hit.point.y;
            else
                anchor.y = view.transform.position.y - 1.6f;
            Vector3 offset = bounds.center - piece.transform.position;
            piece.transform.position = new Vector3(anchor.x - offset.x,
                anchor.y - offset.y + bounds.extents.y, anchor.z - offset.z);
            piece.gameObject.SetActive(true);
            Physics.SyncTransforms();
            pieces.Add(piece);
            return piece;
        }

        private static MapTerrainPiece Create(Entry entry)
        {
            if (!entry.Source)
                return null;

            var root = new GameObject(entry.Name + " [Map]");
            root.SetActive(false);
            root.transform.localScale = entry.Source.transform.lossyScale;
            var piece = root.AddComponent<MapTerrainPiece>();
            piece.Key = entry.Key;
            try
            {
                bool authoredColliders = false;
                if (!entry.SingleMesh)
                {
                    foreach (var collider in entry.Source.GetComponentsInChildren<Collider>(true))
                    {
                        if (collider.enabled && !collider.isTrigger && IsSupportedCollider(collider))
                        {
                            authoredColliders = true;
                            break;
                        }
                    }
                }
                if (CopyNode(entry.Source.transform, root.transform, entry.SingleMesh, authoredColliders))
                    return piece;
                Object.Destroy(root);
                return null;
            }
            catch
            {
                Object.Destroy(root);
                throw;
            }
        }

        private static bool CopyNode(Transform source, Transform target, bool single, bool authoredColliders)
        {
            target.gameObject.layer = source.gameObject.layer == 2 ? 0 : source.gameObject.layer;
            bool hasGeometry = false;
            if (!single)
            {
                foreach (var collider in source.GetComponents<Collider>())
                {
                    if (collider.enabled && !collider.isTrigger)
                        CopyCollider(collider, target.gameObject);
                }
            }
            var filter = source.GetComponent<MeshFilter>();
            var renderer = source.GetComponent<MeshRenderer>();
            if (filter && filter.sharedMesh && renderer && renderer.enabled && !renderer.isPartOfStaticBatch)
            {
                target.gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                target.gameObject.AddComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
                if (!authoredColliders)
                    target.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
                hasGeometry = true;
            }
            if (single)
                return hasGeometry;

            var terrain = source.GetComponent<Terrain>();
            if (terrain && terrain.terrainData)
            {
                var terrainObject = Terrain.CreateTerrainGameObject(terrain.terrainData);
                terrainObject.transform.SetParent(target, false);
                terrainObject.layer = target.gameObject.layer;
                terrainObject.GetComponent<Terrain>().materialTemplate = terrain.materialTemplate;
                hasGeometry = true;
            }
            for (int i = 0; i < source.childCount; i++)
            {
                var child = source.GetChild(i);
                if (!child.gameObject.activeSelf)
                    continue;

                var copy = new GameObject(child.name).transform;
                copy.SetParent(target, false);
                copy.localPosition = child.localPosition;
                copy.localRotation = child.localRotation;
                copy.localScale = child.localScale;
                bool geometry = CopyNode(child, copy, false, authoredColliders);
                if (!geometry && !copy.GetComponentInChildren<Collider>(true))
                    Object.Destroy(copy.gameObject);
                hasGeometry |= geometry;
            }
            return hasGeometry;
        }

        private static bool IsSupportedCollider(Collider collider)
        {
            var mesh = collider as MeshCollider;
            return (mesh && mesh.sharedMesh) || collider is BoxCollider ||
                collider is SphereCollider || collider is CapsuleCollider;
        }

        private static void CopyCollider(Collider source, GameObject target)
        {
            var mesh = source as MeshCollider;
            var box = source as BoxCollider;
            var sphere = source as SphereCollider;
            var capsule = source as CapsuleCollider;
            Collider copy = null;
            if (mesh && mesh.sharedMesh)
            {
                var clone = target.AddComponent<MeshCollider>();
                clone.convex = mesh.convex;
                clone.sharedMesh = mesh.sharedMesh;
                copy = clone;
            }
            else if (box)
            {
                var clone = target.AddComponent<BoxCollider>();
                clone.center = box.center;
                clone.size = box.size;
                copy = clone;
            }
            else if (sphere)
            {
                var clone = target.AddComponent<SphereCollider>();
                clone.center = sphere.center;
                clone.radius = sphere.radius;
                copy = clone;
            }
            else if (capsule)
            {
                var clone = target.AddComponent<CapsuleCollider>();
                clone.center = capsule.center;
                clone.radius = capsule.radius;
                clone.height = capsule.height;
                clone.direction = capsule.direction;
                copy = clone;
            }
            if (copy)
                copy.sharedMaterial = source.sharedMaterial;
        }

        public string Save()
        {
            var layout = new Layout();
            pieces.RemoveAll(piece => !piece);
            foreach (var piece in pieces)
            {
                if (piece.gameObject.activeSelf)
                    layout.Pieces.Add(new Placement(piece));
            }
            if (layout.Pieces.Count == 0)
                return "Place at least one map piece first.";
            string path = SavePath;
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(layout, true));
            if (File.Exists(path))
                File.Replace(temporary, path, null);
            else
                File.Move(temporary, path);
            return "Saved " + layout.Pieces.Count + " map pieces.";
        }

        public string Load()
        {
            if (!File.Exists(SavePath))
                return "No custom map has been saved.";
            var layout = JsonUtility.FromJson<Layout>(File.ReadAllText(SavePath));
            if (layout == null || layout.Pieces == null)
                return "Invalid map file.";

            var restored = new List<MapTerrainPiece>();
            int missing = 0;
            try
            {
                foreach (var placement in layout.Pieces)
                {
                    Entry entry;
                    var piece = placement != null && placement.Key != null &&
                        byKey.TryGetValue(placement.Key, out entry) ? Create(entry) : null;
                    if (!piece)
                    {
                        missing++;
                        continue;
                    }
                    restored.Add(piece);
                    piece.transform.SetPositionAndRotation(placement.Position, placement.Rotation);
                    piece.transform.localScale = placement.Scale;
                }
            }
            catch
            {
                foreach (var piece in restored)
                    Object.Destroy(piece.gameObject);
                throw;
            }
            if (restored.Count == 0)
                return "No saved assets are available in this scene.";
            foreach (var piece in pieces)
            {
                if (piece)
                {
                    piece.gameObject.SetActive(false);
                    Object.Destroy(piece.gameObject);
                }
            }
            pieces.Clear();
            foreach (var piece in restored)
            {
                piece.gameObject.SetActive(true);
                pieces.Add(piece);
            }
            Physics.SyncTransforms();
            return "Loaded " + restored.Count + " map pieces" +
                (missing == 0 ? "." : " (" + missing + " unavailable here).");
        }

        internal sealed class Entry
        {
            public readonly string Key;
            public readonly string Name;
            public readonly int Category;
            public readonly GameObject Source;
            public readonly bool SingleMesh;

            public Entry(string key, string name, int category, GameObject source, bool singleMesh)
            {
                Key = key;
                Name = name;
                Category = category;
                Source = source;
                SingleMesh = singleMesh;
            }
        }

        [Serializable]
        private sealed class Layout
        {
            public List<Placement> Pieces = new List<Placement>();
        }

        [Serializable]
        private sealed class Placement
        {
            public string Key;
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Scale;

            public Placement(MapTerrainPiece piece)
            {
                Key = piece.Key;
                Position = piece.transform.position;
                Rotation = piece.transform.rotation;
                Scale = piece.transform.localScale;
            }
        }
    }

    internal sealed class MapTerrainPiece : MonoBehaviour
    {
        public string Key;

        public Bounds Bounds()
        {
            var bounds = new Bounds(transform.position, Vector3.zero);
            bool hasBounds = false;
            foreach (var filter in GetComponentsInChildren<MeshFilter>(true))
            {
                var meshBounds = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = new Vector3((i & 1) == 0 ? -1f : 1f,
                        (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f);
                    Vector3 point = filter.transform.TransformPoint(meshBounds.center +
                        Vector3.Scale(meshBounds.extents, corner));
                    if (!hasBounds)
                        bounds.center = point;
                    bounds.Encapsulate(point);
                    hasBounds = true;
                }
            }
            foreach (var terrain in GetComponentsInChildren<Terrain>(true))
            {
                var terrainBounds = new Bounds(terrain.transform.position + terrain.terrainData.size * 0.5f,
                    terrain.terrainData.size);
                if (!hasBounds)
                    bounds = terrainBounds;
                else
                    bounds.Encapsulate(terrainBounds);
                hasBounds = true;
            }
            return bounds;
        }
    }
}

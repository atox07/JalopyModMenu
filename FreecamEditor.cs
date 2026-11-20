using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace JalopyModMenu
{
    internal sealed class FreecamEditor
    {
        private static readonly Vector3[] Axes = { Vector3.right, Vector3.up, Vector3.forward };
        private static readonly Color[] AxisColors = {
            new Color(1f, 0.27f, 0.27f), new Color(0.33f, 1f, 0.38f), new Color(0.23f, 0.58f, 1f)
        };
        private static readonly string[] AxisNames = { "X", "Y", "Z" };
        private static readonly int[] BoxEdges = { 0,1, 0,2, 0,4, 1,3, 1,5, 2,3, 2,6, 3,7, 4,5, 4,6, 5,7, 6,7 };
        private static readonly Rect Toolbar = new Rect(20f, 18f, 520f, 90f);
        private readonly GameBindings game;
        private readonly GameTools tools;
        private readonly MapBuilder mapBuilder;
        private readonly bool mapMode;
        private readonly List<Edit> history = new List<Edit>();
        private readonly RaycastHit[] hits = new RaycastHit[64];
        private readonly Vector2[] box = new Vector2[8];
        private GameObject rig;
        private Camera view;
        private bool originalCursorVisible;
        private CursorLockMode originalLock;
        private Behaviour cursorMenu;
        private bool originalGameCursorLock;
        private Rigidbody playerBody;
        private RigidbodyConstraints playerConstraints;
        private Behaviour[] disabledInput;
        private Transform player;
        private Renderer[] sceneMeshes;
        private Transform selected;
        private Transform hovered;
        private Transform highlighted;
        private bool highlightedSelection;
        private Material tint;
        private CommandBuffer overlay;
        private GizmoTransform drag;
        private Vector3 dragPivot;
        private int activeAxis;
        private Vector2 pressPoint;
        private Vector2 dragTangent;
        private float dragScale;
        private float yaw;
        private float pitch;
        private bool rotate;
        private bool scaling;
        private bool uniformScale;
        private bool opened;
        private float nextPick;
        private string status;

        public Behaviour Walker { get; private set; }
        public bool Active { get { return opened; } }
        public bool HasView { get { return view; } }

        public FreecamEditor(GameBindings game, GameTools tools, MapBuilder mapBuilder, bool mapMode)
        {
            this.game = game;
            this.tools = tools;
            this.mapBuilder = mapBuilder;
            this.mapMode = mapMode;
        }

        public void Open(Camera source, Behaviour walker)
        {
            disabledInput = tools.FreecamInput(walker);
            player = walker.transform;
            Walker = walker;
            rotate = false;
            scaling = false;
            status = null;
            originalLock = Cursor.lockState;
            originalCursorVisible = Cursor.visible;
            cursorMenu = game.Menu;
            originalGameCursorLock = game.LocksCursor(cursorMenu);
            game.SetCursorLock(cursorMenu, false);
            opened = true;
            var rotation = source.transform.eulerAngles;
            yaw = rotation.y;
            pitch = Mathf.DeltaAngle(0f, rotation.x);
            rig = new GameObject(mapMode ? "Jalopy Map Editor Camera" : "Jalopy World Editor Camera");
            view = rig.AddComponent<Camera>();
            view.CopyFrom(source);
            if (mapMode)
                view.farClipPlane = Mathf.Max(view.farClipPlane, 1200f);
            view.targetTexture = null;
            if (view.clearFlags == CameraClearFlags.Depth || view.clearFlags == CameraClearFlags.Nothing)
                view.clearFlags = CameraClearFlags.Skybox;
            view.depth = source.depth + 10f;
            rig.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            sceneMeshes = VisualPickMeshes();
            playerBody = walker.GetComponent<Rigidbody>();
            if (playerBody)
            {
                playerConstraints = playerBody.constraints;
                if (!playerBody.isKinematic)
                {
                    playerBody.velocity = Vector3.zero;
                    playerBody.angularVelocity = Vector3.zero;
                }
                playerBody.constraints = RigidbodyConstraints.FreezeAll;
            }
            foreach (var input in disabledInput)
                input.enabled = false;

            var shader = Shader.Find("Hidden/Internal-Colored");
            if (shader && shader.isSupported)
            {
                tint = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                tint.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                tint.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                tint.SetInt("_Cull", (int)CullMode.Back);
                tint.SetInt("_ZWrite", 0);
                tint.SetInt("_ZTest", (int)CompareFunction.LessEqual);
                overlay = new CommandBuffer { name = "Jalopy object highlight" };
                view.AddCommandBuffer(CameraEvent.BeforeImageEffects, overlay);
            }
            SetCursor(false);
        }

        public void SelectNewMapPiece(MapTerrainPiece piece)
        {
            selected = piece.transform;
            PushEdit(new Edit(piece));
            Vector3 direction = piece.Bounds().center - view.transform.position;
            if (direction.sqrMagnitude > 0.01f)
            {
                view.transform.rotation = Quaternion.LookRotation(direction);
                Vector3 angles = view.transform.eulerAngles;
                yaw = angles.y;
                pitch = Mathf.DeltaAngle(0f, angles.x);
            }
            Highlight(selected, true);
            status = "Map piece added. W / R / S to edit.";
        }

        private Renderer[] VisualPickMeshes()
        {
            var meshes = new List<Renderer>();
            var car = tools.Car;
            foreach (var mesh in Object.FindObjectsOfType<MeshRenderer>())
            {
                if (mesh.isPartOfStaticBatch || mesh.GetComponent<Collider>() ||
                    mesh.GetComponentInParent<Rigidbody>() ||
                    (car && mesh.transform.IsChildOf(car.transform)) ||
                    (player && mesh.transform.IsChildOf(player)) ||
                    mapBuilder.IsMapGeometry(mesh.transform) != mapMode)
                    continue;

                meshes.Add(mesh);
            }
            return meshes.ToArray();
        }

        public void Close()
        {
            if (drag != null)
            {
                drag.Finish(true);
                drag = null;
            }
            if (overlay != null)
            {
                if (view)
                    view.RemoveCommandBuffer(CameraEvent.BeforeImageEffects, overlay);
                overlay.Release();
                overlay = null;
            }
            if (tint)
                Object.Destroy(tint);
            if (view)
                view.enabled = false;
            if (rig)
                Object.Destroy(rig);
            if (playerBody)
                playerBody.constraints = playerConstraints;
            if (disabledInput != null)
            {
                foreach (var input in disabledInput)
                {
                    if (input)
                        input.enabled = true;
                }
            }
            if (cursorMenu == game.Menu && game.IsWalkingAvailable())
            {
                game.SetCursorLock(cursorMenu, originalGameCursorLock);
                Cursor.lockState = originalLock;
                Cursor.visible = originalCursorVisible;
            }
            cursorMenu = null;
            view = null;
            rig = null;
            selected = hovered = highlighted = player = null;
            sceneMeshes = null;
            disabledInput = null;
            playerBody = null;
            Walker = null;
            opened = false;
        }

        public void Update()
        {
            bool looking = Input.GetMouseButton(1) && drag == null;
            if (game.LocksCursor(cursorMenu))
                game.SetCursorLock(cursorMenu, false);
            SetCursor(looking);
            Navigate(looking);
            Vector2 mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            bool control = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (control && Input.GetKeyDown(KeyCode.Z))
            {
                Undo();
                return;
            }
            if (drag != null)
            {
                UpdateDrag(mouse);
                return;
            }

            if (Input.GetKeyDown(KeyCode.W) && !looking)
                scaling = rotate = false;
            if (Input.GetKeyDown(KeyCode.R) && !looking)
            {
                scaling = false;
                rotate = true;
            }
            if (Input.GetKeyDown(KeyCode.S) && !looking)
            {
                rotate = false;
                scaling = true;
            }
            if (!mapMode && Input.GetKeyDown(KeyCode.P) && !looking)
                MakeSelectedPickup();
            if (mapMode && Input.GetKeyDown(KeyCode.Delete) && !looking && selected)
            {
                var piece = selected.GetComponent<MapTerrainPiece>();
                if (piece)
                {
                    PushEdit(new Edit(piece, true));
                    piece.gameObject.SetActive(false);
                    selected = hovered = null;
                    Highlight(null, false);
                    status = "Map piece removed.";
                    return;
                }
            }

            bool clicked = Input.GetMouseButtonDown(0) && !looking && !Toolbar.Contains(mouse);
            if (clicked && selected && PickHandle(mouse))
            {
                if (scaling && IsCar(selected))
                {
                    status = "The Laika supports move and rotate.";
                    return;
                }
                if (mapMode && (rotate || scaling) && selected.GetComponentInChildren<Terrain>())
                {
                    status = "Use W to move heightmap terrain.";
                    return;
                }
                uniformScale = scaling && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
                drag = new GizmoTransform(selected, player, IsCar(selected));
                status = null;
                return;
            }
            if (looking)
                hovered = null;
            else if (clicked || Time.unscaledTime >= nextPick)
            {
                hovered = Pick();
                nextPick = Time.unscaledTime + 0.05f;
            }
            if (clicked)
            {
                selected = hovered;
                status = null;
            }
            Highlight(selected ? selected : hovered, selected);
        }

        private void MakeSelectedPickup()
        {
            if (!selected || (!IsCar(selected) && tools.IsNativePickup(selected)))
            {
                status = "Select a loose scenery mesh.";
                return;
            }
            if (selected.GetComponent<WorldMeshPickup>())
            {
                status = "This mesh is already pickable.";
                return;
            }

            var pickup = WorldMeshPickup.Make(selected, IsCar(selected));
            if (!pickup)
            {
                status = "Use a standalone mesh with separate geometry, or select the Laika.";
                return;
            }
            PushEdit(new Edit(selected, pickup));
            status = "Mesh is dynamic. Use the normal pickup key outside the editor.";
        }

        private void Navigate(bool looking)
        {
            if (!looking)
                return;

            yaw += Input.GetAxisRaw("Mouse X") * 3f;
            pitch = Mathf.Clamp(pitch - Input.GetAxisRaw("Mouse Y") * 3f, -89f, 89f);
            view.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 direction = view.transform.forward * KeyPair(KeyCode.W, KeyCode.S) +
                view.transform.right * KeyPair(KeyCode.D, KeyCode.A) +
                Vector3.up * KeyPair(KeyCode.E, KeyCode.Q);
            float speed = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? 24f : 8f;
            view.transform.position += Vector3.ClampMagnitude(direction, 1f) * speed * Time.unscaledDeltaTime;
        }

        private static float KeyPair(KeyCode positive, KeyCode negative)
        {
            return (Input.GetKey(positive) ? 1f : 0f) - (Input.GetKey(negative) ? 1f : 0f);
        }

        private static void SetCursor(bool looking)
        {
            CursorLockMode desired = looking ? CursorLockMode.Locked : CursorLockMode.None;
            if (Cursor.lockState != desired)
                Cursor.lockState = desired;
            if (Cursor.visible == looking)
                Cursor.visible = !looking;
        }

        private bool IsCar(Transform candidate)
        {
            var car = tools.Car;
            return car && candidate == car.transform;
        }

        private Transform Pick()
        {
            Ray ray = view.ScreenPointToRay(Input.mousePosition);
            float nearest = mapMode ? Mathf.Min(view.farClipPlane, 1200f) : 160f;
            int count = Physics.RaycastNonAlloc(ray, hits, nearest, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            Transform result = null;
            Transform hitTransform = null;
            var car = tools.Car;
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                if (!hit.collider || (player && hit.collider.transform.IsChildOf(player)) || hit.distance >= nearest)
                    continue;

                var candidate = car && hit.collider.transform.IsChildOf(car.transform)
                    ? car.transform : ColliderTarget(hit.collider);
                if (!Editable(candidate))
                    continue;

                nearest = hit.distance;
                hitTransform = hit.collider.transform;
                result = candidate;
            }

            // A separately rendered part can be selectable even when its parent owns the collider.
            float meshDistance = nearest + (hitTransform ? 0.12f : 0f);
            foreach (var renderer in sceneMeshes)
            {
                float distance;
                if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                    (view.cullingMask & (1 << renderer.gameObject.layer)) == 0 ||
                    !renderer.bounds.IntersectRay(ray, out distance) || distance >= meshDistance ||
                    (distance >= nearest && (!hitTransform ||
                        !renderer.transform.IsChildOf(hitTransform))) ||
                    renderer.GetComponentInParent<Rigidbody>())
                    continue;
                var piece = mapMode ? renderer.GetComponentInParent<MapTerrainPiece>() : null;
                var candidate = piece ? piece.transform : renderer.transform;
                if (!Editable(candidate))
                    continue;
                meshDistance = distance;
                result = candidate;
            }
            return result;
        }

        private Transform ColliderTarget(Collider collider)
        {
            var piece = collider.GetComponentInParent<MapTerrainPiece>();
            if (piece)
                return mapMode ? piece.transform : null;
            var terrain = collider.GetComponentInParent<Terrain>();
            if (terrain)
                return mapMode ? terrain.transform : null;
            return tools.EditorTarget(collider);
        }

        private bool Editable(Transform candidate)
        {
            if (!candidate || !candidate.gameObject.activeInHierarchy ||
                (player && (candidate.IsChildOf(player) || player.IsChildOf(candidate))) ||
                candidate.GetComponentInChildren<Camera>())
                return false;
            if (IsCar(candidate))
                return !mapMode;
            if (mapMode && (tools.IsNativePickup(candidate) || candidate.GetComponentInParent<WorldMeshPickup>()))
                return false;
            if (mapBuilder.IsMapGeometry(candidate) != mapMode)
                return false;

            var renderers = candidate.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0 && !candidate.GetComponent<MapTerrainPiece>() && !candidate.GetComponent<Terrain>())
                return false;
            foreach (var renderer in renderers)
            {
                if (renderer.isPartOfStaticBatch)
                    return false;
            }
            return true;
        }

        private void Highlight(Transform target, bool committed)
        {
            if (overlay == null || (target == highlighted && committed == highlightedSelection))
                return;

            overlay.Clear();
            highlighted = target;
            highlightedSelection = committed;
            if (!target)
                return;

            tint.SetColor("_Color", committed ? new Color(0.12f, 0.86f, 1f, 0.38f) :
                new Color(1f, 0.79f, 0.18f, 0.30f));
            foreach (var renderer in target.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled || !(renderer is MeshRenderer || renderer is SkinnedMeshRenderer))
                    continue;
                var filter = renderer.GetComponent<MeshFilter>();
                var skinned = renderer as SkinnedMeshRenderer;
                var mesh = filter ? filter.sharedMesh : (skinned ? skinned.sharedMesh : null);
                if (!mesh)
                    continue;
                for (int i = 0; i < mesh.subMeshCount; i++)
                    overlay.DrawRenderer(renderer, tint, i, 0);
            }
        }

        private float HandleLength(Vector3 point)
        {
            float depth = Vector3.Dot(point - view.transform.position, view.transform.forward);
            return Mathf.Clamp(depth, 0.25f, 400f) * 0.17f;
        }

        private bool PickHandle(Vector2 mouse)
        {
            float best = 11f;
            activeAxis = -1;
            Vector3 pivot = GizmoPivot();
            dragPivot = pivot;
            float length = HandleLength(pivot);
            for (int axis = 0; axis < 3; axis++)
            {
                int segments = rotate ? 48 : 1;
                for (int part = 0; part < segments; part++)
                {
                    Vector3 first;
                    Vector3 last;
                    Segment(pivot, length, axis, part, segments, out first, out last);
                    Vector2 a;
                    Vector2 b;
                    if (!Project(first, out a) || !Project(last, out b))
                        continue;
                    Vector2 delta = b - a;
                    if (delta.sqrMagnitude < 1.5f)
                        continue;
                    float t = Mathf.Clamp01(Vector2.Dot(mouse - a, delta) / delta.sqrMagnitude);
                    float distance = (a + delta * t - mouse).magnitude;
                    if (distance >= best)
                        continue;
                    best = distance;
                    activeAxis = axis;
                    dragTangent = delta.normalized;
                    dragScale = rotate ? 1.25f : (scaling ? 1f : length * 0.82f) / delta.magnitude;
                }
            }
            pressPoint = mouse;
            return activeAxis >= 0;
        }

        private Vector3 GizmoPivot()
        {
            Bounds bounds;
            return TryBounds(selected, out bounds) ? bounds.center : selected.position;
        }

        private void Segment(Vector3 pivot, float length, int axis, int part, int count,
            out Vector3 from, out Vector3 to)
        {
            if (!rotate)
            {
                Vector3 direction = scaling ? selected.rotation * Axes[axis] : Axes[axis];
                from = pivot + direction * (length * 0.18f);
                to = pivot + direction * length;
                return;
            }
            from = RingPoint(pivot, length, axis, part * Mathf.PI * 2f / count);
            to = RingPoint(pivot, length, axis, (part + 1) * Mathf.PI * 2f / count);
        }

        private static Vector3 RingPoint(Vector3 pivot, float length, int axis, float radians)
        {
            return pivot + length * (Axes[(axis + 1) % 3] * Mathf.Cos(radians) +
                Axes[(axis + 2) % 3] * Mathf.Sin(radians));
        }

        private void UpdateDrag(Vector2 mouse)
        {
            if (!drag.Target || Input.GetKeyDown(KeyCode.Backspace))
            {
                drag.Finish(true);
                drag = null;
                return;
            }
            float amount = Vector2.Dot(mouse - pressPoint, dragTangent) * dragScale;
            if (rotate)
            {
                Quaternion rotation = Quaternion.AngleAxis(amount, Axes[activeAxis]);
                drag.Move(dragPivot + rotation * (drag.Position - dragPivot), rotation * drag.Rotation);
            }
            else if (scaling)
            {
                float factor = Mathf.Clamp(1f + amount, 0.1f, 10f);
                Vector3 next = drag.Scale;
                if (uniformScale)
                    next *= factor;
                else
                    next[activeAxis] *= factor;
                drag.Move(drag.Position, drag.Rotation, next);
            }
            else
                drag.Move(drag.Position + Axes[activeAxis] * amount, drag.Rotation);

            if (Input.GetMouseButton(0))
                return;

            if (Mathf.Abs(amount) < 0.001f)
                drag.Finish(true);
            else if (drag.IsCar && !drag.PlaceCar())
            {
                drag.Finish(true);
                status = "Place the Laika upright on clear, solid ground.";
            }
            else
            {
                PushEdit(new Edit(drag));
                drag.Finish(false);
                status = "Changed " + drag.Target.name + ".";
            }
            drag = null;
        }

        private void PushEdit(Edit edit)
        {
            if (history.Count == 100)
            {
                var oldest = history[0];
                if (oldest.Deleted && oldest.Target && !oldest.Target.gameObject.activeSelf)
                    Object.Destroy(oldest.Target.gameObject);
                history.RemoveAt(0);
            }
            history.Add(edit);
        }

        private void Undo()
        {
            if (drag != null)
            {
                drag.Finish(true);
                drag = null;
                status = "Drag cancelled.";
                return;
            }

            while (history.Count > 0)
            {
                var edit = history[history.Count - 1];
                history.RemoveAt(history.Count - 1);
                if (!edit.Target || edit.Target.parent != edit.Parent)
                    continue;
                if (edit.Deleted)
                {
                    edit.Target.gameObject.SetActive(true);
                    selected = edit.Target;
                    status = "Map piece restored.";
                    return;
                }
                if (!edit.Target.gameObject.activeInHierarchy)
                    continue;
                if (edit.Spawned)
                {
                    edit.Target.gameObject.SetActive(false);
                    Object.Destroy(edit.Target.gameObject);
                    if (selected == edit.Target)
                        selected = null;
                    status = "Map piece spawn undone.";
                    return;
                }
                if (edit.Pickup)
                {
                    edit.Pickup.RemovePhysics();
                    selected = edit.Target;
                    status = "Dynamic pickup undone.";
                    return;
                }

                var insertion = new GizmoTransform(edit.Target, player, IsCar(edit.Target));
                insertion.Move(edit.Position, edit.Rotation, edit.Scale);
                if (insertion.IsCar && !insertion.PlaceCar(true))
                {
                    insertion.Finish(true);
                    history.Add(edit);
                    status = "Undo needs clear ground at the original position.";
                    return;
                }
                insertion.Finish(false);
                selected = edit.Target;
                status = "Undid " + selected.name + ".";
                return;
            }
            status = "Nothing to undo.";
        }

        public void Draw()
        {
            if (Event.current.type == EventType.Repaint)
            {
                if (hovered && hovered != selected)
                    DrawBounds(hovered, new Color(1f, 0.79f, 0.18f));
                if (selected)
                {
                    DrawBounds(selected, new Color(0.12f, 0.86f, 1f));
                    DrawHandles();
                }
            }
            GUILayout.BeginArea(Toolbar, GUI.skin.box);
            GUILayout.Label((mapMode ? "Map Builder" : "World Editor") + "   |   " +
                (rotate ? "Rotate" : scaling ? "Scale" : "Move") +
                "   |   " + (selected ? selected.name : (hovered ? hovered.name : "Select an object")));
            GUILayout.Label("Hold RMB + WASD / Q E to fly    W Move    R Rotate    S Scale" +
                (mapMode ? "    Delete placed piece" : "    P Make pickup"));
            GUILayout.Label(status ?? (mapMode
                ? "Ctrl+Z undo    Backspace cancel drag    F7 map catalog"
                : "Ctrl+Z undo    Backspace cancel drag    F7 exit"));
            GUILayout.EndArea();
        }

        private bool Project(Vector3 world, out Vector2 screen)
        {
            Vector3 point = view.WorldToScreenPoint(world);
            screen = new Vector2(point.x, Screen.height - point.y);
            return point.z > view.nearClipPlane;
        }

        private void DrawHandles()
        {
            Vector3 pivot = GizmoPivot();
            float length = HandleLength(pivot);
            for (int axis = 0; axis < 3; axis++)
            {
                Color color = drag != null && axis == activeAxis ? Color.white : AxisColors[axis];
                int segments = rotate ? 48 : 1;
                for (int i = 0; i < segments; i++)
                {
                    Vector3 from;
                    Vector3 to;
                    Segment(pivot, length, axis, i, segments, out from, out to);
                    Vector2 a;
                    Vector2 b;
                    if (!Project(from, out a) || !Project(to, out b))
                        continue;
                    DrawLine(a, b, color, 3f);
                    if (!rotate)
                    {
                        Vector2 along = (b - a).normalized;
                        Vector2 side = new Vector2(-along.y, along.x);
                        if (scaling)
                        {
                            Color original = GUI.color;
                            GUI.color = color;
                            GUI.DrawTexture(new Rect(b.x - 4f, b.y - 4f, 8f, 8f), Texture2D.whiteTexture);
                            GUI.color = original;
                        }
                        else
                        {
                            DrawLine(b, b - along * 13f + side * 6f, color, 3f);
                            DrawLine(b, b - along * 13f - side * 6f, color, 3f);
                        }
                        GUI.Label(new Rect(b.x + 5f, b.y - 12f, 20f, 22f), AxisNames[axis]);
                    }
                }
            }
        }

        private static bool TryBounds(Transform target, out Bounds bounds)
        {
            var mapPiece = target.GetComponent<MapTerrainPiece>();
            if (mapPiece)
            {
                bounds = mapPiece.Bounds();
                return true;
            }
            var terrain = target.GetComponent<Terrain>();
            if (terrain && terrain.terrainData)
            {
                Vector3 size = terrain.terrainData.size;
                bounds = new Bounds(terrain.transform.position + size * 0.5f, size);
                return true;
            }
            var renderers = target.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                bounds = default(Bounds);
                return false;
            }
            bounds = renderers[0].bounds;
            foreach (var renderer in renderers)
                bounds.Encapsulate(renderer.bounds);
            return true;
        }

        private void DrawBounds(Transform target, Color color)
        {
            Bounds bounds;
            if (!TryBounds(target, out bounds))
                return;
            for (int i = 0; i < box.Length; i++)
            {
                Vector3 vertex = bounds.center + new Vector3(
                    ((i & 1) == 0 ? -1f : 1f) * bounds.extents.x,
                    ((i & 2) == 0 ? -1f : 1f) * bounds.extents.y,
                    ((i & 4) == 0 ? -1f : 1f) * bounds.extents.z);
                if (!Project(vertex, out box[i]))
                    return;
            }
            for (int edge = 0; edge < BoxEdges.Length; edge += 2)
                DrawLine(box[BoxEdges[edge]], box[BoxEdges[edge + 1]], color, 1.4f);
        }

        private static void DrawLine(Vector2 from, Vector2 to, Color color, float width)
        {
            Matrix4x4 oldMatrix = GUI.matrix;
            Color oldColor = GUI.color;
            Vector2 delta = to - from;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, from);
            GUI.color = color;
            GUI.DrawTexture(new Rect(from.x, from.y - width * 0.5f, delta.magnitude, width), Texture2D.whiteTexture);
            GUI.color = oldColor;
            GUI.matrix = oldMatrix;
        }

        private sealed class Edit
        {
            public readonly Transform Target;
            public readonly Transform Parent;
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;
            public readonly Vector3 Scale;
            public readonly WorldMeshPickup Pickup;
            public readonly bool Spawned;
            public readonly bool Deleted;

            public Edit(MapTerrainPiece piece, bool deleted = false)
            {
                Target = piece.transform;
                Parent = Target.parent;
                Spawned = !deleted;
                Deleted = deleted;
            }

            public Edit(GizmoTransform drag)
            {
                Target = drag.Target;
                Parent = drag.Parent;
                Position = drag.Position;
                Rotation = drag.Rotation;
                Scale = drag.Scale;
            }

            public Edit(Transform target, WorldMeshPickup pickup)
            {
                Target = target;
                Parent = target.parent;
                Position = target.position;
                Rotation = target.rotation;
                Scale = target.localScale;
                Pickup = pickup;
            }
        }
    }
}

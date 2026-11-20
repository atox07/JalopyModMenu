using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JalopyModMenu
{
    internal sealed class WorldPlacement
    {
        private readonly Transform target;
        private readonly Vector3 originalPosition;
        private readonly Quaternion originalRotation;
        private readonly Rigidbody body;
        private readonly bool originalKinematic;
        private readonly bool originalCollisions;
        private readonly Vector3 originalVelocity;
        private readonly Vector3 originalAngularVelocity;
        private readonly bool created;
        private readonly List<Collider> suspendedColliders = new List<Collider>();
        private readonly Vector3 gripOffset;
        private float distance;
        private Quaternion rotation;

        public string Name { get { return target ? target.name : "Object"; } }
        public bool IsAlive { get { return target && target.gameObject.activeInHierarchy; } }

        private WorldPlacement(Transform target, Camera camera, Vector3 grip, bool created)
        {
            this.target = target;
            this.created = created;
            originalPosition = target.position;
            originalRotation = rotation = target.rotation;
            gripOffset = Quaternion.Inverse(rotation) * (originalPosition - grip);
            distance = Mathf.Clamp(Vector3.Dot(grip - camera.transform.position, camera.transform.forward), 0.8f, 40f);
            body = target.GetComponent<Rigidbody>();
            if (body)
            {
                originalKinematic = body.isKinematic;
                originalCollisions = body.detectCollisions;
                originalVelocity = body.velocity;
                originalAngularVelocity = body.angularVelocity;
                if (!body.isKinematic)
                {
                    body.velocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
                body.isKinematic = true;
                body.detectCollisions = false;
            }
            else
            {
                foreach (var collider in target.GetComponentsInChildren<Collider>())
                {
                    if (!collider.enabled || collider.isTrigger)
                        continue;
                    suspendedColliders.Add(collider);
                    collider.enabled = false;
                }
            }
        }

        public static WorldPlacement FromAim(GameTools tools, MapBuilder mapBuilder, Camera camera,
            bool duplicate, out string status)
        {
            status = "Stand on foot and put down any held items first.";
            if (!camera || !tools.EditorAvailable(camera))
                return null;

            Vector3 origin = camera.transform.position;
            RaycastHit hit;
            if (!Physics.Raycast(origin, camera.transform.forward, out hit, 35f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                status = "Aim at an item or a standalone prop.";
                return null;
            }

            Transform selected = tools.EditorTarget(hit.collider);
            if (mapBuilder.IsMapGeometry(hit.collider.transform) || mapBuilder.IsMapGeometry(selected) ||
                !IsEditable(selected, tools))
            {
                status = "Aim at a loose item or a standalone, movable mesh.";
                return null;
            }

            if (duplicate)
            {
                selected = CopyVisualProp(selected);
                if (!selected)
                {
                    status = "This prop has no supported mesh to duplicate.";
                    return null;
                }
            }

            status = null;
            return new WorldPlacement(selected, camera, hit.point, duplicate);
        }

        public static WorldPlacement FromSpawn(GameObject spawned, Camera camera)
        {
            Vector3 grip = camera.transform.position + camera.transform.forward * 3f;
            spawned.transform.position = grip;
            return new WorldPlacement(spawned.transform, camera, grip, true);
        }

        private static bool IsEditable(Transform candidate, GameTools tools)
        {
            if (!candidate || !candidate.gameObject.activeInHierarchy || candidate.GetComponent<WheelCollider>() ||
                candidate.GetComponentInChildren<Camera>())
                return false;

            var player = tools.Player;
            var car = tools.Car;
            var bodies = candidate.GetComponentsInChildren<Rigidbody>();
            if (IsRelated(candidate, player) || IsRelated(candidate, car) ||
                bodies.Length > (candidate.GetComponent<Rigidbody>() ? 1 : 0))
                return false;

            var renderers = candidate.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0 || renderers.Length > 24)
                return false;

            Bounds bounds = renderers[0].bounds;
            foreach (var renderer in renderers)
            {
                if (renderer.isPartOfStaticBatch)
                    return false;
                bounds.Encapsulate(renderer.bounds);
            }
            return bounds.size.magnitude < 25f;
        }

        private static bool IsRelated(Transform target, GameObject root)
        {
            return root && (target.IsChildOf(root.transform) || root.transform.IsChildOf(target));
        }

        private static Transform CopyVisualProp(Transform source)
        {
            var filters = source.GetComponentsInChildren<MeshFilter>();
            Transform clone = null;
            foreach (var filter in filters)
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (!filter.sharedMesh || !renderer || !renderer.enabled)
                    continue;

                if (!clone)
                {
                    clone = new GameObject(source.name + " (copy)").transform;
                    clone.position = source.position;
                    clone.rotation = source.rotation;
                    clone.gameObject.AddComponent<EditorProp>();
                }
                var meshObject = new GameObject(filter.name);
                meshObject.layer = filter.gameObject.layer;
                var child = meshObject.transform;
                child.position = filter.transform.position;
                child.rotation = filter.transform.rotation;
                child.localScale = filter.transform.lossyScale;
                child.SetParent(clone, true);
                meshObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                meshObject.AddComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
                var collider = meshObject.AddComponent<BoxCollider>();
                collider.center = filter.sharedMesh.bounds.center;
                collider.size = Vector3.Max(filter.sharedMesh.bounds.size, Vector3.one * 0.02f);
            }
            return clone;
        }

        public void Preview(Camera camera, float scroll, float yaw, float pitch)
        {
            distance = Mathf.Clamp(distance + scroll * 3f, 0.8f, 40f);
            rotation = Quaternion.AngleAxis(yaw, Vector3.up) *
                Quaternion.AngleAxis(pitch, camera.transform.right) * rotation;
            Vector3 point = camera.transform.position + camera.transform.forward * distance;
            target.SetPositionAndRotation(point + rotation * gripOffset, rotation);
        }

        public void Finish(bool cancel, bool freeze)
        {
            if (!target)
                return;

            if (cancel && created)
            {
                Object.Destroy(target.gameObject);
                return;
            }

            if (cancel)
                target.SetPositionAndRotation(originalPosition, originalRotation);
            if (body)
            {
                // Transfer the preview transform into physics before restoring the dynamic body.
                body.position = target.position;
                body.rotation = target.rotation;
                body.detectCollisions = originalCollisions;
                var marker = target.GetComponent<EditorProp>();
                bool frozenByEditor = marker && marker.FrozenByEditor;
                bool normallyKinematic = originalKinematic && !frozenByEditor;
                body.isKinematic = cancel ? originalKinematic : normallyKinematic || freeze;
                if (!cancel && !normallyKinematic && (marker || freeze))
                {
                    if (!marker)
                        marker = target.gameObject.AddComponent<EditorProp>();
                    marker.FrozenByEditor = freeze;
                }
                if (!body.isKinematic)
                {
                    body.velocity = cancel ? originalVelocity : Vector3.zero;
                    body.angularVelocity = cancel ? originalAngularVelocity : Vector3.zero;
                    body.WakeUp();
                }
            }
            foreach (var collider in suspendedColliders)
            {
                if (collider)
                    collider.enabled = true;
            }
        }
    }

    internal sealed class EditorProp : MonoBehaviour
    {
        public bool FrozenByEditor;
    }
}

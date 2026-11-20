using System.Collections.Generic;
using UnityEngine;

namespace JalopyModMenu
{
    internal sealed class WorldMeshPickup : MonoBehaviour
    {
        private readonly List<Collider> originalColliders = new List<Collider>();
        private BoxCollider pickupCollider;
        private Rigidbody body;
        private Quaternion relativeRotation;
        private bool originallyStatic;
        private bool vehicle;
        private GizmoTransform vehicleCarry;
        private Vector3 vehicleCenter;
        private Vector3 vehicleSize;
        private Vector3 startPosition;
        private Quaternion startRotation;
        private readonly Collider[] nearby = new Collider[64];

        public bool IsVehicle { get { return vehicle; } }

        public static WorldMeshPickup Make(Transform target, bool vehicle)
        {
            if (!target)
                return null;
            if (vehicle)
            {
                var vehicleBody = target.GetComponent<Rigidbody>();
                if (!vehicleBody)
                    return null;
                var pickup = target.gameObject.AddComponent<WorldMeshPickup>();
                pickup.vehicle = true;
                pickup.body = vehicleBody;
                return pickup;
            }

            if (target.GetComponentInParent<Rigidbody>() ||
                target.GetComponentInChildren<Rigidbody>() || target.GetComponentInParent<Joint>() ||
                target.GetComponentInChildren<Joint>() || target.GetComponentInChildren<Terrain>())
                return null;

            var renderers = target.GetComponentsInChildren<MeshRenderer>();
            if (renderers.Length == 0)
                return null;

            var worldBounds = renderers[0].bounds;
            var localBounds = new Bounds(target.InverseTransformPoint(worldBounds.center), Vector3.zero);
            foreach (var renderer in renderers)
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (!filter || !filter.sharedMesh || renderer.isPartOfStaticBatch)
                    return null;

                worldBounds.Encapsulate(renderer.bounds);
                var meshBounds = filter.sharedMesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = meshBounds.center + Vector3.Scale(meshBounds.extents,
                        new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f,
                            (corner & 4) == 0 ? -1f : 1f));
                    localBounds.Encapsulate(target.InverseTransformPoint(renderer.transform.TransformPoint(point)));
                }
            }
            var item = target.gameObject.AddComponent<WorldMeshPickup>();
            item.originallyStatic = target.gameObject.isStatic;
            target.gameObject.isStatic = false;
            foreach (var collider in target.GetComponentsInChildren<Collider>())
            {
                if (collider.enabled)
                {
                    item.originalColliders.Add(collider);
                    collider.enabled = false;
                }
            }
            item.pickupCollider = target.gameObject.AddComponent<BoxCollider>();
            item.pickupCollider.center = localBounds.center;
            item.pickupCollider.size = Vector3.Max(localBounds.size, Vector3.one * 0.02f);
            item.body = target.gameObject.AddComponent<Rigidbody>();
            item.body.mass = Mathf.Clamp(worldBounds.size.x * worldBounds.size.y * worldBounds.size.z * 8f, 0.2f, 12f);
            item.body.interpolation = RigidbodyInterpolation.Interpolate;
            return item;
        }

        public void Take(Camera camera, Transform player)
        {
            if (vehicle)
            {
                var renderers = GetComponentsInChildren<Renderer>();
                var bounds = new Bounds(transform.position, Vector3.zero);
                foreach (var renderer in renderers)
                    bounds.Encapsulate(renderer.bounds);
                vehicleCenter = transform.InverseTransformPoint(bounds.center);
                vehicleSize = bounds.size;
                relativeRotation = Quaternion.Inverse(CameraHeading(camera)) *
                    Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
                vehicleCarry = new GizmoTransform(transform, player, true);
                return;
            }
            startPosition = body.position;
            startRotation = body.rotation;
            relativeRotation = Quaternion.Inverse(camera.transform.rotation) * transform.rotation;
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
            body.detectCollisions = false;
            body.interpolation = RigidbodyInterpolation.None;
            Follow(camera);
        }

        public void Follow(Camera camera)
        {
            var view = camera.transform;
            Quaternion rotation = (vehicle ? CameraHeading(camera) : view.rotation) * relativeRotation;
            Vector3 center = vehicle ? vehicleCenter : pickupCollider.center;
            Vector3 visualOffset = rotation * Vector3.Scale(center, transform.lossyScale);
            Vector3 halfSize = (vehicle ? vehicleSize : Vector3.Scale(pickupCollider.size, transform.lossyScale)) * 0.5f;
            Vector3 forward = vehicle ? CameraHeading(camera) * Vector3.forward : view.forward;
            Vector3 localForward = Quaternion.Inverse(rotation) * forward;
            float radius = Mathf.Abs(localForward.x * halfSize.x) +
                Mathf.Abs(localForward.y * halfSize.y) + Mathf.Abs(localForward.z * halfSize.z);
            float distance = Mathf.Max(1.15f, radius + 0.6f);
            RaycastHit hit;
            if (Physics.Raycast(view.position, forward, out hit, distance + radius,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.distance < radius + 0.6f)
                    return;
                distance = Mathf.Min(distance, hit.distance - radius);
            }
            Vector3 position = view.position + forward * distance - visualOffset;
            if (vehicleCarry != null)
                vehicleCarry.Move(position, rotation);
            else
            {
                body.position = position;
                body.rotation = rotation;
            }
        }

        private static Quaternion CameraHeading(Camera camera)
        {
            return Quaternion.Euler(0f, camera.transform.eulerAngles.y, 0f);
        }

        public bool Drop(bool cancel = false)
        {
            if (vehicleCarry != null)
            {
                if (!cancel && !vehicleCarry.PlaceCar())
                    return false;
                vehicleCarry.Finish(cancel);
                vehicleCarry = null;
                return true;
            }
            if (vehicle)
                return true;
            if (!body || !body.isKinematic)
                return true;
            if (!CanDropProp())
            {
                if (!cancel)
                    return false;
                body.position = startPosition;
                body.rotation = startRotation;
            }
            body.detectCollisions = true;
            body.isKinematic = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.WakeUp();
            return true;
        }

        private bool CanDropProp()
        {
            Vector3 scale = transform.lossyScale;
            Vector3 extents = Vector3.Scale(pickupCollider.size * 0.5f,
                new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            Quaternion rotation = body.rotation;
            Vector3 position = body.position;
            Vector3 center = position + rotation * Vector3.Scale(pickupCollider.center, scale);
            int count = Physics.OverlapBoxNonAlloc(center, extents, nearby, rotation,
                Physics.AllLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var other = nearby[i];
                if (!other || other.attachedRigidbody == body ||
                    Physics.GetIgnoreLayerCollision(gameObject.layer, other.gameObject.layer))
                    continue;
                Vector3 separation;
                float depth;
                if (Physics.ComputePenetration(pickupCollider, position, rotation, other,
                    other.transform.position, other.transform.rotation, out separation, out depth) && depth > 0.03f)
                    return false;
            }
            return true;
        }

        public void RemovePhysics()
        {
            if (vehicle)
            {
                Drop(true);
                Destroy(this);
                return;
            }
            pickupCollider.enabled = false;
            body.isKinematic = true;
            Destroy(pickupCollider);
            Destroy(body);
            gameObject.isStatic = originallyStatic;
            foreach (var collider in originalColliders)
            {
                if (collider)
                    collider.enabled = true;
            }
            Destroy(this);
        }
    }
}

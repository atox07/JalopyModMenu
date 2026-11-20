using System.Collections.Generic;
using UnityEngine;

namespace JalopyModMenu
{
    internal sealed class MovementSession
    {
        private readonly Rigidbody body;
        private readonly bool originalGravity;
        private readonly bool originalCollisions;
        private readonly CollisionDetectionMode originalDetection;
        private readonly Collider[] nearby = new Collider[48];
        private List<PlayerShape> shapes;
        private Vector3 noclipStart;
        private Vector3 lastClearPosition;

        public readonly Behaviour Walker;
        public bool Fly { get; private set; }
        public bool Noclip { get; private set; }
        public bool IsUsable { get { return body && !body.isKinematic && Walker && Walker.isActiveAndEnabled; } }

        public MovementSession(Behaviour walker, Rigidbody body)
        {
            Walker = walker;
            this.body = body;
            originalGravity = body.useGravity;
            originalCollisions = body.detectCollisions;
            originalDetection = body.collisionDetectionMode;
        }

        public void SetFly(bool enabled)
        {
            Fly = enabled;
            ApplyPhysics();
        }

        public bool TrySetNoclip(bool enabled, out string message)
        {
            message = null;
            if (enabled)
            {
                if (!CaptureShapes())
                {
                    message = "Noclip unavailable: unsupported player collider.";
                    return false;
                }

                noclipStart = body.position;
                lastClearPosition = noclipStart;
            }
            else if (!TryReturnToClearPosition(out message))
            {
                return false;
            }

            Noclip = enabled;
            ApplyPhysics();
            return true;
        }

        public void Move(Vector3 velocity)
        {
            if (Noclip && IsClear(body.position))
                lastClearPosition = body.position;

            body.velocity = velocity;
        }

        public void Release(bool restorePosition)
        {
            if (!body)
                return;

            if (Noclip && restorePosition && !body.isKinematic)
            {
                string ignored;
                if (!TryReturnToClearPosition(out ignored))
                    body.position = noclipStart;
            }

            if (restorePosition && !body.isKinematic)
                body.velocity = Vector3.zero;

            body.detectCollisions = originalCollisions;
            body.useGravity = originalGravity;
            body.collisionDetectionMode = originalDetection;
        }

        private void ApplyPhysics()
        {
            body.velocity = Vector3.zero;
            body.useGravity = !Fly && !Noclip && originalGravity;
            body.detectCollisions = !Noclip && originalCollisions;
            body.collisionDetectionMode = Fly && !Noclip && originalCollisions &&
                originalDetection == CollisionDetectionMode.Discrete
                ? CollisionDetectionMode.ContinuousDynamic
                : originalDetection;
        }

        private bool TryReturnToClearPosition(out string message)
        {
            message = null;
            if (IsClear(body.position))
                return true;

            Vector3 clearPosition = lastClearPosition;
            if (!IsClear(clearPosition))
                clearPosition = noclipStart;

            if (!IsClear(clearPosition))
            {
                message = "Move clear of the obstacle before disabling Noclip.";
                return false;
            }

            body.position = clearPosition;
            message = "Returned to a clear position.";
            return true;
        }

        private bool CaptureShapes()
        {
            shapes = new List<PlayerShape>();
            foreach (var collider in body.GetComponentsInChildren<Collider>())
            {
                if (collider.attachedRigidbody != body || !collider.enabled || collider.isTrigger)
                    continue;

                var mesh = collider as MeshCollider;
                if (!(collider is BoxCollider || collider is CapsuleCollider ||
                    collider is SphereCollider || (mesh && mesh.convex)))
                    return false;

                Bounds bounds = collider.bounds;
                shapes.Add(new PlayerShape(collider, bounds.center - collider.transform.position,
                    bounds.extents.magnitude + 0.1f));
            }

            return shapes.Count != 0;
        }

        private bool IsClear(Vector3 position)
        {
            foreach (var shape in shapes)
            {
                var own = shape.Collider;
                if (!own || !own.enabled || !own.gameObject.activeInHierarchy)
                    continue;

                Vector3 shapePosition = position + (own.transform.position - body.transform.position);
                Quaternion rotation = own.transform.rotation;
                Vector3 center = shapePosition + rotation * shape.LocalCenterOffset;
                int count = Physics.OverlapSphereNonAlloc(center, shape.Radius, nearby,
                    Physics.AllLayers, QueryTriggerInteraction.Ignore);
                if (count == nearby.Length)
                {
                    var all = Physics.OverlapSphere(center, shape.Radius,
                        Physics.AllLayers, QueryTriggerInteraction.Ignore);
                    if (HasPenetration(own, shapePosition, rotation, all, all.Length))
                        return false;
                }
                else if (HasPenetration(own, shapePosition, rotation, nearby, count))
                {
                    return false;
                }
            }

            return true;
        }

        private bool HasPenetration(Collider own, Vector3 position, Quaternion rotation,
            Collider[] candidates, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var other = candidates[i];
                if (!other || other.attachedRigidbody == body ||
                    Physics.GetIgnoreLayerCollision(own.gameObject.layer, other.gameObject.layer))
                    continue;

                Vector3 direction;
                float distance;
                if (Physics.ComputePenetration(own, position, rotation, other,
                    other.transform.position, other.transform.rotation, out direction, out distance) &&
                    distance > 0.015f)
                    return true;
            }

            return false;
        }

        private sealed class PlayerShape
        {
            public readonly Collider Collider;
            public readonly Vector3 LocalCenterOffset;
            public readonly float Radius;

            public PlayerShape(Collider collider, Vector3 centerOffset, float radius)
            {
                Collider = collider;
                LocalCenterOffset = Quaternion.Inverse(collider.transform.rotation) * centerOffset;
                Radius = radius;
            }
        }
    }
}

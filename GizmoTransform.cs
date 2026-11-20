using System.Collections.Generic;
using UnityEngine;

namespace JalopyModMenu
{
    internal sealed class GizmoTransform
    {
        private readonly BodyState[] bodies;
        private readonly WheelCollider[] wheels;
        private readonly float[] wheelClearances;
        private readonly List<Collider> staticColliders = new List<Collider>();
        private readonly Transform player;

        public readonly Transform Target;
        public readonly Transform Parent;
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;
        public readonly Vector3 Scale;
        public bool IsCar { get { return wheels != null; } }

        public GizmoTransform(Transform target, Transform player, bool car)
        {
            Target = target;
            this.player = player;
            Parent = target.parent;
            Position = target.position;
            Rotation = target.rotation;
            Scale = target.localScale;
            if (car)
            {
                var enabledWheels = new List<WheelCollider>();
                var clearances = new List<float>();
                foreach (var wheel in target.GetComponentsInChildren<WheelCollider>())
                {
                    if (!wheel.enabled)
                        continue;

                    WheelHit hit;
                    float clearance = wheel.GetGroundHit(out hit)
                        ? wheel.transform.TransformPoint(wheel.center).y - hit.point.y
                        : wheel.radius + wheel.suspensionDistance * 0.5f;
                    clearances.Add(Mathf.Clamp(clearance, 0.1f, 1.8f));
                    enabledWheels.Add(wheel);
                }
                wheels = enabledWheels.ToArray();
                wheelClearances = clearances.ToArray();
            }

            var rigidbodies = target.GetComponentsInChildren<Rigidbody>();
            bodies = new BodyState[rigidbodies.Length];
            for (int i = 0; i < rigidbodies.Length; i++)
                bodies[i] = new BodyState(rigidbodies[i], Position, Rotation);

            if (bodies.Length == 0)
            {
                foreach (var collider in target.GetComponentsInChildren<Collider>())
                {
                    if (!collider.enabled || collider.attachedRigidbody)
                        continue;
                    staticColliders.Add(collider);
                    collider.enabled = false;
                }
            }

            if (wheels != null)
            {
                foreach (var wheel in wheels)
                    wheel.enabled = false;
            }
        }

        public void Move(Vector3 position, Quaternion rotation)
        {
            if (Target)
                Move(position, rotation, Target.localScale);
        }

        public void Move(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            if (!Target)
                return;

            Vector3 ratio = new Vector3(ScaleRatio(scale.x, Scale.x),
                ScaleRatio(scale.y, Scale.y), ScaleRatio(scale.z, Scale.z));
            Target.localScale = scale;
            Target.SetPositionAndRotation(position, rotation);
            foreach (var state in bodies)
                state.Move(position, rotation, ratio);
            Physics.SyncTransforms();
        }

        private static float ScaleRatio(float next, float initial)
        {
            return Mathf.Abs(initial) > 0.00001f ? next / initial : 1f;
        }

        public bool PlaceCar(bool preservePose = false)
        {
            if (!IsCar || wheels.Length < 2 || Vector3.Dot(Target.up, Vector3.up) < 0.65f)
                return false;

            int supported = 0;
            float lift = float.NegativeInfinity;
            for (int i = 0; i < wheels.Length; i++)
            {
                var wheel = wheels[i];
                if (!wheel)
                    continue;

                Vector3 center = wheel.transform.TransformPoint(wheel.center);
                float best = float.PositiveInfinity;
                float ground = 0f;
                foreach (var hit in Physics.RaycastAll(center + Vector3.up * 2f, Vector3.down, 32f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    var collider = hit.collider;
                    if (!collider || collider.transform.IsChildOf(Target) ||
                        (player && collider.transform.IsChildOf(player)) ||
                        (collider.attachedRigidbody && !collider.attachedRigidbody.isKinematic) ||
                        hit.normal.y < 0.65f || hit.distance >= best)
                        continue;

                    best = hit.distance;
                    ground = hit.point.y;
                }
                if (float.IsInfinity(best))
                    continue;

                supported++;
                lift = Mathf.Max(lift, ground + wheelClearances[i] - center.y);
            }
            if (supported < 2)
                return false;

            if (preservePose)
                return Mathf.Abs(lift) < 0.35f && !ChassisOverlaps();

            Move(Target.position + Vector3.up * (lift + 0.025f), Target.rotation);
            return !ChassisOverlaps();
        }

        private bool ChassisOverlaps()
        {
            foreach (var own in Target.GetComponentsInChildren<Collider>())
            {
                if (!own.enabled || own.isTrigger || own is WheelCollider)
                    continue;

                var mesh = own as MeshCollider;
                if (!(own is BoxCollider || own is CapsuleCollider || own is SphereCollider || (mesh && mesh.convex)))
                    continue;

                // The body's collision flag is off, so query nearby geometry independently of its bounds.
                foreach (var other in Physics.OverlapSphere(own.transform.position, 5f,
                    Physics.AllLayers, QueryTriggerInteraction.Ignore))
                {
                    if (!other || other.transform.IsChildOf(Target) ||
                        (player && other.transform.IsChildOf(player)) ||
                        Physics.GetIgnoreLayerCollision(own.gameObject.layer, other.gameObject.layer))
                        continue;

                    Vector3 direction;
                    float distance;
                    if (Physics.ComputePenetration(own, own.transform.position, own.transform.rotation, other,
                        other.transform.position, other.transform.rotation, out direction, out distance) &&
                        distance > 0.06f)
                        return true;
                }
            }
            return false;
        }

        public void Finish(bool cancel)
        {
            if (cancel)
                Move(Position, Rotation, Scale);

            foreach (var state in bodies)
                state.Restore(cancel);
            foreach (var collider in staticColliders)
            {
                if (collider)
                    collider.enabled = true;
            }
            if (wheels != null)
            {
                foreach (var wheel in wheels)
                {
                    if (wheel)
                        wheel.enabled = true;
                }
            }
        }

        private sealed class BodyState
        {
            public readonly Rigidbody Body;
            private readonly bool kinematic;
            private readonly bool collisions;
            private readonly Vector3 velocity;
            private readonly Vector3 angularVelocity;
            private readonly RigidbodyInterpolation interpolation;
            private readonly Vector3 offset;
            private readonly Quaternion relativeRotation;

            public BodyState(Rigidbody body, Vector3 rootPosition, Quaternion rootRotation)
            {
                Body = body;
                Quaternion inverse = Quaternion.Inverse(rootRotation);
                offset = inverse * (body.transform.position - rootPosition);
                relativeRotation = inverse * body.transform.rotation;
                kinematic = body.isKinematic;
                collisions = body.detectCollisions;
                interpolation = body.interpolation;
                velocity = body.velocity;
                angularVelocity = body.angularVelocity;
                if (!kinematic)
                {
                    body.velocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
                body.isKinematic = true;
                body.detectCollisions = false;
                body.interpolation = RigidbodyInterpolation.None;
            }

            public void Move(Vector3 rootPosition, Quaternion rootRotation, Vector3 ratio)
            {
                if (!Body)
                    return;

                Body.position = rootPosition + rootRotation * Vector3.Scale(offset, ratio);
                Body.rotation = rootRotation * relativeRotation;
            }

            public void Restore(bool cancel)
            {
                if (!Body)
                    return;

                Body.detectCollisions = collisions;
                Body.isKinematic = kinematic;
                Body.interpolation = interpolation;
                if (!kinematic)
                {
                    Body.velocity = cancel ? velocity : Vector3.zero;
                    Body.angularVelocity = cancel ? angularVelocity : Vector3.zero;
                    Body.WakeUp();
                }
            }
        }
    }
}

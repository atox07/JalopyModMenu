using System.Collections.Generic;
using UnityEngine;

namespace JalopyModMenu
{
    internal sealed class VehicleNoclip
    {
        private readonly List<Shape> shapes;
        private readonly List<WheelCollider> wheels;
        private readonly bool originalCollisions;
        private readonly Transform driver;
        private readonly Collider[] nearby = new Collider[64];
        private readonly RaycastHit[] groundHits = new RaycastHit[32];
        private readonly float rideHeight;
        private readonly Vector3 startPosition;
        private readonly Quaternion startRotation;
        private Vector3 lastClearPosition;
        private Quaternion lastClearRotation;
        private float groundHeight;
        private float yaw;
        private float speed;
        private Vector3 lastSupportedPosition;
        private bool overGround = true;

        public readonly Rigidbody Body;

        private VehicleNoclip(Rigidbody body, Transform driver, List<Shape> shapes,
            List<WheelCollider> wheels, float groundHeight)
        {
            Body = body;
            this.driver = driver;
            this.shapes = shapes;
            this.wheels = wheels;
            this.groundHeight = groundHeight;
            rideHeight = body.position.y - groundHeight;
            yaw = body.rotation.eulerAngles.y;
            startPosition = lastClearPosition = body.position;
            lastSupportedPosition = body.position;
            startRotation = lastClearRotation = body.rotation;
            originalCollisions = body.detectCollisions;
            speed = Mathf.Clamp(Vector3.Dot(body.velocity, Quaternion.Euler(0f, yaw, 0f) *
                Vector3.forward), -6f, 22f);
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
            body.detectCollisions = false;
            foreach (var wheel in wheels)
                wheel.enabled = false;
        }

        public static VehicleNoclip TryStart(Rigidbody body, Transform driver, out string status)
        {
            status = "Get in the driver's seat to use car noclip.";
            if (!body || body.isKinematic)
                return null;

            var wheels = new List<WheelCollider>();
            int supportingWheels = 0;
            float groundY = 0f;
            foreach (var wheel in body.GetComponentsInChildren<WheelCollider>())
            {
                if (!wheel.enabled || wheel.attachedRigidbody != body)
                    continue;
                wheels.Add(wheel);
                WheelHit hit;
                if (wheel.GetGroundHit(out hit) && hit.normal.y >= 0.65f)
                {
                    groundY += hit.point.y;
                    supportingWheels++;
                }
            }
            if (supportingWheels < 2)
            {
                status = "Put the Laika on solid ground before enabling car noclip.";
                return null;
            }

            var shapes = new List<Shape>();
            foreach (var collider in body.GetComponentsInChildren<Collider>())
            {
                if (!collider.enabled || collider.isTrigger || collider.attachedRigidbody != body || collider is WheelCollider)
                    continue;

                var mesh = collider as MeshCollider;
                if (!(collider is BoxCollider || collider is SphereCollider || collider is CapsuleCollider ||
                    (mesh && mesh.convex)))
                {
                    status = "Unsupported chassis collider. Car noclip stayed off.";
                    return null;
                }
                shapes.Add(new Shape(collider, body.transform));
            }
            if (shapes.Count == 0)
            {
                status = "No chassis colliders found.";
                return null;
            }

            status = "Car noclip enabled. Drive with the normal keyboard controls.";
            return new VehicleNoclip(body, driver, shapes, wheels, groundY / supportingWheels);
        }

        public void Step(Vector2 controls, bool brake, float dt)
        {
            if (!Body)
                return;

            if (overGround && PoseIsClear(Body.position, Body.rotation))
            {
                lastClearPosition = Body.position;
                lastClearRotation = Body.rotation;
            }

            float desiredSpeed = brake ? 0f : controls.y * (controls.y < 0f ? 6f : 22f);
            float acceleration = brake ? 28f : (controls.y == 0f ? 7f : 10f);
            speed = Mathf.MoveTowards(speed, desiredSpeed, acceleration * dt);
            if (Mathf.Abs(speed) < 0.01f)
                return;

            float turn = Mathf.Lerp(60f, 32f, Mathf.Abs(speed) / 22f);
            float nextYaw = yaw + controls.x * turn * Mathf.Sign(speed) * dt;
            Quaternion heading = Quaternion.Euler(0f, nextYaw, 0f);
            float distance = Mathf.Abs(speed) * dt;
            Vector3 position = Body.position + heading * Vector3.forward * (speed * dt);
            float nextGround;
            Vector3 normal;
            Quaternion facing = Body.rotation;
            if (FindGround(position, distance, out nextGround, out normal))
            {
                groundHeight = nextGround;
                overGround = true;
                lastSupportedPosition = position;
                Vector3 tangent = Vector3.ProjectOnPlane(heading * Vector3.forward, normal);
                facing = Quaternion.RotateTowards(facing, Quaternion.LookRotation(tangent, normal), 85f * dt);
            }
            else
            {
                Vector3 gap = Vector3.ProjectOnPlane(position - lastSupportedPosition, Vector3.up);
                if (gap.sqrMagnitude > 1.8f * 1.8f)
                {
                    speed = 0f;
                    yaw = nextYaw;
                    Body.MoveRotation(Quaternion.RotateTowards(facing, heading, 85f * dt));
                    return;
                }
                overGround = false;
                facing = Quaternion.RotateTowards(facing, heading, 85f * dt);
            }

            yaw = nextYaw;
            position.y = Mathf.MoveTowards(Body.position.y, groundHeight + rideHeight, 2.8f * dt);
            Body.MovePosition(position);
            Body.MoveRotation(facing);
        }

        private bool FindGround(Vector3 position, float distance, out float height, out Vector3 normal)
        {
            float upper = groundHeight + 0.09f + distance * 0.7f;
            float lower = groundHeight - 0.24f - distance * 0.9f;
            Vector3 origin = new Vector3(position.x, upper, position.z);
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, groundHits, upper - lower,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            height = groundHeight;
            normal = Vector3.up;
            float closest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                var hit = groundHits[i];
                if (!hit.collider || hit.normal.y < 0.65f || BelongsToCarOrDriver(hit.collider))
                    continue;
                var supportBody = hit.collider.attachedRigidbody;
                if (supportBody && !supportBody.isKinematic)
                    continue;
                float difference = Mathf.Abs(hit.point.y - groundHeight);
                if (difference < closest)
                {
                    height = hit.point.y;
                    normal = hit.normal;
                    closest = difference;
                }
            }
            return closest < float.PositiveInfinity;
        }

        public bool TryStop(bool force, out string status)
        {
            status = "Car noclip disabled.";
            if (Body && Body.gameObject.activeInHierarchy &&
                (!overGround || !PoseIsClear(Body.position, Body.rotation)))
            {
                Vector3 position = lastClearPosition;
                Quaternion rotation = lastClearRotation;
                if (!PoseIsClear(position, rotation))
                {
                    position = startPosition;
                    rotation = startRotation;
                    if (!force && !PoseIsClear(position, rotation))
                    {
                        status = "Drive clear of the obstacle before disabling car noclip.";
                        return false;
                    }
                }
                Body.position = position;
                Body.rotation = rotation;
                status = "Car noclip disabled. Returned to a clear position.";
            }

            if (Body)
            {
                Body.isKinematic = false;
                Body.detectCollisions = originalCollisions;
                Body.velocity = Vector3.zero;
                Body.angularVelocity = Vector3.zero;
            }
            foreach (var wheel in wheels)
            {
                if (wheel)
                    wheel.enabled = true;
            }
            return true;
        }

        private bool BelongsToCarOrDriver(Collider collider)
        {
            return collider.attachedRigidbody == Body || collider.transform.IsChildOf(Body.transform) ||
                (driver && collider.transform.IsChildOf(driver));
        }

        private bool PoseIsClear(Vector3 position, Quaternion rotation)
        {
            foreach (var shape in shapes)
            {
                var own = shape.Collider;
                if (!own || !own.gameObject.activeInHierarchy)
                    continue;

                Vector3 center = position + rotation * shape.Center;
                int count = Physics.OverlapSphereNonAlloc(center, shape.Radius, nearby,
                    Physics.AllLayers, QueryTriggerInteraction.Ignore);
                Collider[] candidates = nearby;
                if (count == nearby.Length)
                {
                    candidates = Physics.OverlapSphere(center, shape.Radius,
                        Physics.AllLayers, QueryTriggerInteraction.Ignore);
                    count = candidates.Length;
                }

                for (int i = 0; i < count; i++)
                {
                    var other = candidates[i];
                    if (!other || BelongsToCarOrDriver(other) ||
                        Physics.GetIgnoreLayerCollision(own.gameObject.layer, other.gameObject.layer))
                        continue;

                    Vector3 direction;
                    float distance;
                    if (Physics.ComputePenetration(own, position + rotation * shape.Position,
                        rotation * shape.Rotation, other, other.transform.position,
                        other.transform.rotation, out direction, out distance) && distance > 0.04f)
                        return false;
                }
            }
            return true;
        }

        private sealed class Shape
        {
            public readonly Collider Collider;
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;
            public readonly Vector3 Center;
            public readonly float Radius;

            public Shape(Collider collider, Transform root)
            {
                Collider = collider;
                Quaternion inverse = Quaternion.Inverse(root.rotation);
                Position = inverse * (collider.transform.position - root.position);
                Rotation = inverse * collider.transform.rotation;
                Bounds bounds = collider.bounds;
                Center = inverse * (bounds.center - root.position);
                Radius = bounds.extents.magnitude + 0.1f;
            }
        }
    }
}

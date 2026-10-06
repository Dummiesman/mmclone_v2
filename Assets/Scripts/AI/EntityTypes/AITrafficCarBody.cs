using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace MM2.AI
{
    /// Physics side of a traffic car, on the vehicle model's GameObject.
    public class AITrafficCarBody : MonoBehaviour
    {
        // Total impulse a kinematic (on-rail) car may push back with in one step. Just over the knock-off
        // threshold: enough to register the hit, not enough to stop the other body dead.
        private const float ContactImpulseCap = AITrafficCar.KnockOffImpulse * 1.2f;

        // depenetration
        private const float MaxDepenetrationStep = 1f;      // meters; a deeper overlap means something tunnelled
        private const float DepenetrationSkin = 0.01f;      // leave a sliver of gap so we don't re-contact immediately
        private const int DepenetrationPasses = 6;          // flattened pushes clear less per pass, so give it a few more

        // Collider ids of kinematic traffic cars. Written on the main thread only, read from physics worker
        // threads during the step (while the main thread is blocked in the simulation), so no lock needed.
        private static readonly HashSet<int> cappedColliders = new HashSet<int>();
        private static bool contactHookInstalled;

        public AITrafficCar Car { get; private set; }
        public Rigidbody Rb { get; private set; }
        public BoxCollider Box { get; private set; }
        public bool IsDynamic => !Rb.isKinematic;

        private VehWheelCheap[] wheels;
        private int boxId;

        public void Init(AITrafficCar car, AIVehicleData data, float mass)
        {
            Car = car;

            Rb = gameObject.AddComponent<Rigidbody>();
            Rb.mass = mass;
            Rb.isKinematic = true;
            Rb.interpolation = RigidbodyInterpolation.None;
            Rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            // Same box the bumper/side distances describe: Size centred on CG.
            // If the car's origin is at ground level and CG.y is low, use Size.y * 0.5f for the Y instead.
            Box = gameObject.AddComponent<BoxCollider>();
            Box.size = data.Size;
            Box.center = data.CG;
            Box.hasModifiableContacts = true; // lets CapKnockOffContacts see pairs involving us

            int vehicleLayerIndex = LayerMask.NameToLayer("VehicleBody");
            Box.gameObject.layer = vehicleLayerIndex;

            boxId = Box.GetInstanceID();
            InstallContactHook();

            // Suspension and tyre grip for when we're loose; off while the rail drives us.
            int count = Mathf.Min(4, data.WheelPositions.Length);
            wheels = new VehWheelCheap[count];
            for (int i = 0; i < count; i++)
            {
                var wheel = gameObject.AddComponent<VehWheelCheap>();
                float radius = data.WheelPositions[i].y; // same radius AITrafficCar.UpdateWheels rolls with
                if (radius > 0.01f)
                    wheel.Radius = radius;
                wheel.Init(Rb, data.WheelPositions[i]); // tuning comes from the fields, so set them first
                wheel.enabled = false;
                wheels[i] = wheel;
            }
        }

        private void SetWheelsEnabled(bool enabled)
        {
            for (int i = 0; i < wheels.Length; i++)
            {
                wheels[i].enabled = enabled;
            }
            if(enabled)
            {
                foreach (var wheel in wheels)
                {
                    wheel.ResetState();
                }
            }
        }

        /// Pushes us clear of 'theirCollider'. The solver won't: we're kinematic (so it never depenetrates us) and
        /// the contact modifier caps the impulse, so by the time the hit registers we're already inside them.
        public Vector3 DepenetrateFrom(Collider theirCollider)
        {
            if (theirCollider == null || !Box.enabled)
                return Vector3.zero;

            Vector3 away = Box.bounds.center - theirCollider.bounds.center;
            away.y = 0f;
            if (away.sqrMagnitude < 1e-6f)
                away = transform.right; // dead centre on them: any way out will do
            away.Normalize();

            Vector3 theirPosition = theirCollider.transform.position;
            Quaternion theirRotation = theirCollider.transform.rotation;
            Vector3 offset = Vector3.zero;

            // 'distance' is the depth along the shortest way out, so it's never more than we need along
            // 'away': step by it and re-measure until we're clear.
            for (int pass = 0; pass < DepenetrationPasses; pass++)
            {
                if (!Physics.ComputePenetration(
                        Box, Box.transform.position + offset, Box.transform.rotation,
                        theirCollider, theirPosition, theirRotation,
                        out _, out float distance))
                    break;

                offset += away * (distance + DepenetrationSkin);

                if (offset.magnitude > MaxDepenetrationStep)
                {
                    offset = away * MaxDepenetrationStep;
                    break;
                }
            }

            if (offset.sqrMagnitude < 1e-8f)
                return Vector3.zero;

            transform.position += offset;
            Rb.position = transform.position;
            return offset;
        }

        public void MakeDynamic()
        {
            Rb.isKinematic = false;
            Rb.interpolation = RigidbodyInterpolation.Interpolate;
            Rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            SetWheelsEnabled(true);
            UpdateContactCap();
            Rb.angularVelocity = Vector3.zero;
            Rb.WakeUp();
        }


        public void MakeKinematic()
        {
            if (!Rb.isKinematic)
            {
                Rb.velocity = Vector3.zero;
                Rb.angularVelocity = Vector3.zero;
            }
            Rb.isKinematic = true;
            Rb.interpolation = RigidbodyInterpolation.None;
            Rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative; // only continuous mode kinematic bodies support
            SetWheelsEnabled(false);
            UpdateContactCap();
        }

        public void SetCollisionEnabled(bool enabled)
        {
            Box.enabled = enabled;
            Rb.detectCollisions = enabled;
            UpdateContactCap();
        }

        // ---------------------------------------------------------------------------------------------
        // Contact capping: a kinematic body has infinite mass, so without this a knock-off stops the player
        // like a wall. Kinematic-vs-kinematic and kinematic-vs-static make no contacts, so every pair that
        // involves a capped (kinematic) car is against a dynamic body and gets capped.
        // ---------------------------------------------------------------------------------------------

        private void UpdateContactCap()
        {
            if (Rb.isKinematic && Box.enabled)
                cappedColliders.Add(boxId);
            else
                cappedColliders.Remove(boxId);
        }

        private static void InstallContactHook()
        {
            if (contactHookInstalled)
                return;

            Physics.ContactModifyEvent += CapKnockOffContacts;
            Physics.ContactModifyEventCCD += CapKnockOffContacts;
            contactHookInstalled = true;
        }

        // Statics survive play mode when domain reload is off: start clean each time.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            if (contactHookInstalled)
            {
                Physics.ContactModifyEvent -= CapKnockOffContacts;
                Physics.ContactModifyEventCCD -= CapKnockOffContacts;
            }
            contactHookInstalled = false;
            cappedColliders.Clear();
        }

        // Runs on physics worker threads mid-step: no Unity API calls in here.
        private static void CapKnockOffContacts(PhysicsScene scene, NativeArray<ModifiableContactPair> pairs)
        {
            for (int i = 0; i < pairs.Length; i++)
            {
                var pair = pairs[i]; // wraps pointers into the solver's buffers, edits go straight through
                if (!cappedColliders.Contains(pair.colliderInstanceID) &&
                    !cappedColliders.Contains(pair.otherColliderInstanceID))
                    continue;

                int count = pair.contactCount;
                if (count == 0)
                    continue;

                float perContact = ContactImpulseCap / count;
                for (int j = 0; j < count; j++)
                    pair.SetMaxImpulse(j, perContact);
            }
        }

        private void OnDestroy()
        {
            cappedColliders.Remove(boxId);
        }

        public void ResetForPool()
        {
            MakeKinematic();
            SetCollisionEnabled(false);
        }

        private void FixedUpdate()
        {
            if(Rb.isKinematic && Rb.detectCollisions)
            {
                SyncToRail();
            }
        }

        public void SyncToRail()
        {
            if (Car == null || !Rb.isKinematic || !Rb.detectCollisions)
                return;

            transform.SetPositionAndRotation(Car.Position, Car.Rotation);
        }

        private void OnCollisionEnter(Collision collision) => Car?.OnBodyCollision(collision);
        private void OnCollisionStay(Collision collision) => Car?.OnBodyCollision(collision);
    }
}

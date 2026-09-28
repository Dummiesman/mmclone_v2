using UnityEngine;

namespace MM2.Camera
{
    /// <summary>
    /// Port of camTrackCS — the chase camera.
    ///
    /// Drives three animated channels through a Spline: x = dolly/zoom, y = swing (yaw about
    /// world up), z = pitch trim. Front()/Rear() set the goal to (0, PI, 0) / (0, 0, 0),
    /// which is what pins the swing channel to index 1.
    /// </summary>
    public class camTrackCS : camCarCS
    {
        // ---- Settings (FileIO) ----------------------------------------------------------
        public Vector3 Offset = new Vector3(0.0f, 1.9f, 7.7f);
        public int CollideType = 0;
        public bool MinMaxOn = true;
        public int TrackBreak = 0;

        public float MinAppXZPos = 1.8f;
        public float MaxAppXZPos = 12.0f;
        public float MinSpeed = 5.0f;
        public float MaxSpeed = 35.0f;
        public float AppInc = 15.0f;
        public float AppDec = 10.0f;

        public float MinHardSteer = 0.8f;
        public float DriftDelay = 0.3f;
        public float VertOffset = 0.6f;
        public float FrontRate = 0.55f;
        public float RearRate = 0.5f;
        public float FlipDelay = 0.5f;

        public bool SteerOn = false;
        public float SteerMin = 0.5f;
        public float SteerAmt = 3.5f;

        public float HillMin = -0.56f;
        public float HillMax = 0.56f;
        public float HillLerp = 0.05f;

        public float RevDelay = 2.0f;
        public float RevOnApp = 2.0f;
        public float RevOffApp = 4.0f;

        // ---- Runtime state --------------------------------------------------------------
        [SerializeField] protected Spline spline;

        // Replaces RoomId + CollideFlags1/2 + dgPhysManager.
        public LayerMask CollisionMask = ~0;

        // dword_180 — collision pull-in slack. Not in FileIO; set by whatever configures the view.
        public float CollideOffset;

        // dword_110 — set externally to snap to the goal at the end of the next Update.
        public bool SnapAfterUpdate;

        public float CarSteer;
        public float CarSpeed;

        protected Vector3 UpNormal = Vector3.up;
        protected float hillPitch;          // dword_1b4

        protected Vector3 prevTrackToLocal; // dword_280..288
        protected Vector3 prevEye;          // dword_274

        protected float airTimer;           // dword_184.y
        protected float groundTimer;        // dword_184.z
        protected bool grounded;            // dword_190
        protected int trackBreakState;      // dword_194 (0 normal, 1 break, 2 forced)
        protected bool trackBreaking;       // dword_198

        protected bool reverseLook;         // dword_248
        protected float reverseTimer;       // dword_24c
        protected float reverseSign = 1.0f; // dword_250

        protected bool collideActive;       // dword_258
        protected float collideGoalDistSq;  // dword_260
        protected float collideCurDistSq;   // dword_264

        private static readonly RaycastHit[] castBuffer = new RaycastHit[16];

        // ---- Frame ----------------------------------------------------------------------
        protected override void Update()
        {
            base.Update();

            if (internalCamera == null || Car == null || Target == null)
                return;

            if (ForceSnap)
                spline.Reset();

            Vector3 previousPosition = Position;

            UpdateCar();
            UpdateHill();
            UpdateTrack();
            UpdateSwing();
            PreApproach();
            ApproachIt();

            if (MinMaxOn)
                MinMax(previousPosition);

            if (CollideType != 0)
                Collide(previousPosition);

            if (ForceSnap)
                ForceSnap = false;

            spline.Update();

            if (SnapAfterUpdate)
            {
                SnapAfterUpdate = false;
                Position = DesiredPosition;
                Rotation = DesiredRotation;
            }
        }

        // ---- Tracking -------------------------------------------------------------------
        protected virtual void UpdateTrack()
        {
            if (Target == null)
                return;

            TrackToLocal = Target.TransformPoint(TrackTo);

            Vector3 trackDelta = TrackToLocal - prevTrackToLocal;
            prevTrackToLocal = TrackToLocal;

            // Simplifies (state != 0 && !breaking) || breaking.
            bool breaking = (!grounded && (trackBreakState != 0 || trackBreaking)) || trackBreakState == 2;

            Vector3 eye;
            if (breaking)
            {
                // Hold the framing and just carry it along with the target.
                trackBreakState = 1;
                trackBreaking = true;
                eye = prevEye + trackDelta;
            }
            else
            {
                trackBreaking = false;

                Vector3 forward = TargetTrackAxis();
                Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;

                eye = TrackToLocal + right * Offset.x + forward * Mathf.Max(Offset.z, 0.01f);
                eye.y += Offset.y;
            }

            TrackToLocal.y += 0.4f;
            prevEye = eye;

            LookAbove = (Offset.y - 0.8f) * VertOffset;

            DesiredPosition = eye;
            DesiredRotation = LookRotationSafe(TrackToLocal - eye, DesiredRotation);

            float dt = Time.deltaTime;
            float zoom = spline.Value.x;
            float swing = spline.Value.y;
            float pitch;

            if (ReverseMode == 1)
            {
                float swingGoal = reverseLook ? reverseSign * Mathf.PI : 0.0f;
                float rate = reverseLook ? RevOnApp : RevOffApp;

                swing = Mathf.MoveTowards(swing, swingGoal, rate * dt);

                pitch = HillPitchForSwing(swing) + spline.Value.z;
                SetSwing(swing);
            }
            else if (ReverseMode == -1)
            {
                swing = reverseLook ? Mathf.PI : 0.0f;

                pitch = HillPitchForSwing(swing) + spline.Value.z;
                SetSwing(swing);
            }
            else
            {
                pitch = spline.Value.z + hillPitch;
            }

            if (zoom != 0.0f || swing != 0.0f || pitch != 0.0f)
            {
                Vector3 relative = DesiredPosition - TrackToLocal;

                if (pitch != 0.0f)
                {
                    // Axis is the goal matrix's own right vector, taken before either rotation.
                    Vector3 axis = Vector3.Cross(Vector3.up, DesiredRotation * Vector3.forward);
                    Quaternion rotation = Quaternion.AngleAxis(-pitch * Mathf.Rad2Deg, axis);

                    relative = rotation * relative;
                    DesiredRotation = rotation * DesiredRotation;
                }

                if (swing != 0.0f)
                {
                    Quaternion rotation = Quaternion.AngleAxis(swing * Mathf.Rad2Deg, Vector3.up);

                    relative = rotation * relative;
                    DesiredRotation = rotation * DesiredRotation;
                }

                DesiredPosition = TrackToLocal + relative;

                if (zoom != 0.0f)
                    DesiredPosition += (DesiredPosition - TrackToLocal) * zoom;
            }

            Vector3 lookTarget = TrackToLocal;
            lookTarget.y += LookAbove;

            DesiredRotation = LookRotationSafe(lookTarget - DesiredPosition, DesiredRotation);
        }

        /// <summary>Fades the hill pitch out and back in inverted as the camera swings to the front.</summary>
        protected float HillPitchForSwing(float swing)
        {
            float amount = Mathf.Abs(swing);
            float fraction;

            if (amount > 0.0f)
                fraction = (amount < Mathf.PI) ? (amount / Mathf.PI) : 1.0f;
            else
                fraction = 0.0f;

            return hillPitch * (1.0f - 2.0f * fraction);
        }

        /// <summary>Stubbed out in the original too.</summary>
        protected virtual void UpdateSwing()
        {
        }

        // ---- Hill pitch -----------------------------------------------------------------
        protected virtual void UpdateHill()
        {
            Vector3 frontNormal = AverageWheelNormal(0, 1);
            Vector3 rearNormal = AverageWheelNormal(2, 3);

            bool rearValid = rearNormal.sqrMagnitude > 0.01f;
            bool frontValid = frontNormal.sqrMagnitude > 0.01f;

            Vector3 up;
            if (!frontValid)
                up = rearValid ? rearNormal : Vector3.up;
            else if (!rearValid)
                up = frontNormal;
            else
                up = (frontNormal + rearNormal) * 0.5f;

            if (ForceSnap)
                UpNormal = up;
            else
                UpNormal += (up - UpNormal) * HillLerp;

            UpNormal = UpNormal.normalized;

            float angle = Vector3.Angle(Vector3.up, UpNormal) * Mathf.Deg2Rad;
            Vector3 carRight = Target.right.normalized;

            float amount;
            if (angle >= 0.01f)
            {
                Vector3 axis = Vector3.Cross(UpNormal, Vector3.up).normalized;
                float scale = (angle < HalfPi * 0.5f) ? (angle * 4.0f / Mathf.PI) : 1.0f;

                amount = scale * -Vector3.Dot(carRight, axis);
            }
            else
            {
                // Too flat to have a meaningful slope direction; always well under PI/4 here.
                amount = (angle > 0.0f) ? (angle * 4.0f / Mathf.PI) : 0.0f;
            }

            float blend;
            if (amount > 0.0f)
            {
                float t = (amount < 0.5f) ? (amount + amount) : 1.0f;
                blend = Mathf.Cos(t * HalfPi + Mathf.PI) + 2.0f;
            }
            else
            {
                float negative = -amount;
                float t = (negative > 0.0f) ? ((negative < 0.5f) ? (negative + negative) : 1.0f) : 0.0f;
                blend = Mathf.Sin((1.0f - t) * HalfPi);
            }

            blend *= 0.5f;

            if (blend >= 0.5f)
                hillPitch = -(HillMax * (blend - 0.5f) * 2.0f);
            else
                hillPitch = -(HillMin * (1.0f - 2.0f * blend));
        }

        // ---- Car state ------------------------------------------------------------------
        protected virtual void UpdateCar()
        {
            float dt = Time.deltaTime;

            CarSteer = SteeringInput;
            CarSpeed = Speed;

            if (ReverseMode == 1)
            {
                if (CurrentGear != 0)
                {
                    if (reverseLook)
                    {
                        reverseSign = (CarSteer <= 0.1f) ? -1.0f : 1.0f;

                        // Already swung all the way round — land it on the limit exactly.
                        if (Mathf.Abs(spline.Value.y) > 3.1405928f)
                            SetSwing(reverseSign * Mathf.PI);

                        reverseLook = false;
                    }

                    reverseTimer = 0.0f;
                }
                else
                {
                    if (HandBrakeInput > 0.5f)
                        AppXZPos = 0.0f;

                    if (!reverseLook && ThrottleInput >= 0.05f)
                    {
                        reverseTimer += dt;

                        // Hard 2s in the original even though RevDelay exists.
                        if (reverseTimer >= 2.0f)
                        {
                            reverseLook = true;
                            reverseSign = (CarSteer <= 0.1f) ? 1.0f : -1.0f;
                        }
                    }
                }
            }
            else if (ReverseMode == -1)
            {
                reverseLook = true;
            }

            if ((Target.up.y < 0.35f && TrackBreak == 1) || TrackBreak == 2)
            {
                trackBreakState = 1;
                grounded = false;
                return;
            }

            float angularSpeedSq = AngularVelocitySqrMagnitude;

            if (OnGroundCount <= 2)
            {
                airTimer += dt;
                if (airTimer > 0.1f)
                {
                    grounded = false;
                    groundTimer = 0.0f;
                }
            }
            else
            {
                groundTimer += dt;
                if (groundTimer > 0.1f)
                {
                    grounded = true;
                    airTimer = 0.0f;
                }
            }

            trackBreakState = (angularSpeedSq > 2250000.0f && !grounded) ? 1 : 0;
        }

        /// <summary>Ramps AppXZPos toward a speed-derived target so the camera lags harder when quick.</summary>
        protected virtual void PreApproach()
        {
            if (Car == null)
                return;

            float current = AppXZPos;
            float target;

            if (MinAppXZPos == 0.0f)
            {
                target = MaxAppXZPos;
            }
            else
            {
                float t;
                if (MinSpeed == MaxSpeed || CarSpeed <= MinSpeed)
                    t = 0.0f;
                else if (CarSpeed < MaxSpeed)
                    t = (CarSpeed - MinSpeed) / (MaxSpeed - MinSpeed);
                else
                    t = 1.0f;

                target = MaxAppXZPos + (MinAppXZPos - MaxAppXZPos) * t;
            }

            // Snap tight to the car when rolling in neutral with reverse-look off.
            if (ReverseMode == 0 && CurrentGear == 0)
                target = 20.0f;

            float rate = (target > current) ? AppInc : AppDec;
            AppXZPos = Mathf.MoveTowards(current, target, rate * Time.deltaTime);
        }

        // ---- Vertical clamp -------------------------------------------------------------
        protected virtual void MinMax(Vector3 previousPosition)
        {
            Vector3 position = Position;
            float baseY = previousPosition.y;

            Vector3 upFrom = new Vector3(position.x, baseY, position.z);
            Vector3 upTo = new Vector3(position.x, baseY + 5.0f, position.z);

            bool hasCeiling = false;
            float ceilingY = 0.0f;
            Vector3 downFrom = upFrom;

            RaycastHit hit;
            if (CameraCast(upFrom, upTo, out hit) && hit.normal.y < 0.7f)
            {
                hasCeiling = true;
                ceilingY = hit.point.y - 0.5f;
                downFrom = new Vector3(position.x, ceilingY, position.z);
            }

            Vector3 downTo = new Vector3(position.x, baseY - 5.0f, position.z);

            bool hasFloor = false;
            float floorY = 0.0f;

            if (CameraCast(downFrom, downTo, out hit) && hit.normal.y > 0.7f)
            {
                hasFloor = true;
                floorY = hit.point.y + 0.5f;
            }

            // Ceiling below the floor is degenerate — the original leaves the camera alone.
            if (hasCeiling && hasFloor && ceilingY < floorY)
                return;

            if (hasCeiling && position.y > ceilingY)
                position.y = ceilingY;

            if (hasFloor && position.y < floorY)
                position.y = floorY;

            Position = position;
        }

        // ---- Collision ------------------------------------------------------------------
        protected virtual void Collide(Vector3 previousPosition)
        {
            if (CollideType == 1)
                CollideFrustum();
            else if (CollideType == 2)
                CollideSegment(previousPosition);
        }

        /// <summary>Casts the four near-plane corners back from the car and pulls the camera in.</summary>
        protected virtual void CollideFrustum()
        {
            Vector3 position = Position;
            Vector3 direction = (position - TrackToLocal).normalized;

            float near = internalCamera.nearClipPlane;
            float tan = Mathf.Tan(internalCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);

            float halfHeight = tan * near + 0.33f;
            float halfWidth = tan * internalCamera.aspect * near + 0.33f;

            Transform camTransform = internalCamera.transform;
            Vector3 right = camTransform.right * halfWidth;
            Vector3 up = camTransform.up * halfHeight;

            float nearest = 10000000.0f;
            nearest = CastCorner(-right - up, direction, nearest);
            nearest = CastCorner(right - up, direction, nearest);
            nearest = CastCorner(-right + up, direction, nearest);
            nearest = CastCorner(right + up, direction, nearest);

            float limit = nearest * MaxDist + near - CollideOffset;

            if (limit < (TrackToLocal - position).magnitude)
                Position = TrackToLocal + direction * limit;
        }

        private float CastCorner(Vector3 corner, Vector3 direction, float nearest)
        {
            Vector3 from = TrackToLocal + corner;
            Vector3 to = from + direction * MaxDist;

            RaycastHit hit;
            if (!CameraCast(from, to, out hit))
                return nearest;

            float t = (MaxDist > 0.0f) ? (hit.distance / MaxDist) : 0.0f;

            // Ignore back faces — only surfaces the camera is travelling into count.
            if (t < nearest && Vector3.Dot(hit.normal, direction) < -0.00001f)
                nearest = t;

            return nearest;
        }

        /// <summary>Line of sight test from the car to the camera, easing in and back out.</summary>
        protected virtual void CollideSegment(Vector3 previousPosition)
        {
            Vector3 position = Position;
            Vector3 direction = (position - TrackToLocal).normalized;
            float dt = Time.deltaTime;

            RaycastHit hit;
            if (CameraCast(TrackToLocal, position, out hit) && hit.distance > 2.0f)
            {
                collideActive = true;
                collideGoalDistSq = (TrackToLocal - position).sqrMagnitude;

                Vector3 pulled = hit.point - direction;
                float pulledDistSq = (TrackToLocal - pulled).sqrMagnitude;

                Vector3 goal;
                float band;

                if (pulledDistSq >= 50.0f)
                {
                    goal = pulled;
                    band = MinDist;
                }
                else
                {
                    float scale = pulledDistSq * 0.02f;
                    goal = hit.point - direction * scale;
                    band = MaxDist;
                }

                Vector3 next = ForceSnap ? goal : VectorApproach(previousPosition, goal, band, dt * 15.0f);
                Position = next;

                collideCurDistSq = (TrackToLocal - next).sqrMagnitude;

                if (collideGoalDistSq > 0.0f)
                    LookAbove = collideCurDistSq / collideGoalDistSq * LookAbove;

                return;
            }

            if (!collideActive)
                return;

            if (collideCurDistSq >= collideGoalDistSq || ForceSnap)
            {
                collideActive = false;
                return;
            }

            collideCurDistSq = Mathf.MoveTowards(collideCurDistSq, collideGoalDistSq, dt * 30.0f);
            Position = TrackToLocal + direction * Mathf.Sqrt(collideCurDistSq);
        }

        // ---- Swing commands -------------------------------------------------------------
        public void Front(float time)
        {
            SetSwingGoal(new Vector3(0.0f, Mathf.PI, 0.0f), time);
        }

        public void Rear(float time)
        {
            SetSwingGoal(Vector3.zero, time);
        }

        public void SwingToRear()
        {
            float time = Mathf.Max(Mathf.Abs(spline.Value.y), Mathf.Abs(spline.Value.z) + 0.2f) * 0.6f;

            Rear(time);

            // The original also zeroed two floats either side of the channel array here.
            // Neither lands inside the three channels, so nothing to do.
        }

        private void SetSwingGoal(Vector3 goal, float time)
        {
            if (time >= 0.000001f)
                spline.SetGoal(goal, time);
            else
                spline.SetValue(goal);
        }

        private void SetSwing(float swing)
        {
            Vector3 value = spline.Value;
            value.y = swing;
            spline.Value = value;
        }

        protected override void Awake()
        {
            base.Awake();
            spline = new Spline(3);
        }

        /// <summary>Named ResetCamera because Reset is a MonoBehaviour editor message.</summary>
        public virtual void ResetCamera()
        {
            Position = DesiredPosition;
            Rotation = DesiredRotation;

            ForceSnap = true;
            trackBreaking = false;
            SnapAfterUpdate = false;

            reverseTimer = 0.0f;
            reverseLook = false;
            reverseSign = 1.0f;
        }

        public override void ReadSettings(TokenFileParser parser)
        {
            base.ReadSettings(parser);

            Offset = parser.Read("Offset", Offset);
            CollideType = parser.Read("CollideType", CollideType);
            MinMaxOn = parser.Read("MinMaxOn", MinMaxOn);
            TrackBreak = parser.Read("TrackBreak", TrackBreak);
            MinAppXZPos = parser.Read("MinAppXZPos", MinAppXZPos);
            MaxAppXZPos = parser.Read("MaxAppXZPos", MaxAppXZPos);
            MinSpeed = parser.Read("MinSpeed", MinSpeed);
            MaxSpeed = parser.Read("MaxSpeed", MaxSpeed);
            AppInc = parser.Read("AppInc", AppInc);
            AppDec = parser.Read("AppDec", AppDec);
            MinHardSteer = parser.Read("MinHardSteer", MinHardSteer);
            DriftDelay = parser.Read("DriftDelay", DriftDelay);
            VertOffset = parser.Read("VertOffset", VertOffset);
            FrontRate = parser.Read("FrontRate", FrontRate);
            RearRate = parser.Read("RearRate", RearRate);
            FlipDelay = parser.Read("FlipDelay", FlipDelay);
            SteerOn = parser.Read("SteerOn", SteerOn);
            SteerMin = parser.Read("SteerMin", SteerMin);
            SteerAmt = parser.Read("SteerAmt", SteerAmt);
            HillMin = parser.Read("HillMin", HillMin);
            HillMax = parser.Read("HillMax", HillMax);
            HillLerp = parser.Read("HillLerp", HillLerp);
            ReverseMode = parser.Read("ReverseOn", ReverseMode);
            RevDelay = parser.Read("RevDelay", RevDelay);
            RevOnApp = parser.Read("RevOnApp", RevOnApp);
            RevOffApp = parser.Read("RevOffApp", RevOffApp);
        }

        // ---- Helpers --------------------------------------------------------------------
        protected virtual Vector3 TargetTrackAxis()
        {
            Vector3 forward = -Target.forward;
            return new Vector3(forward.x, 0.0f, forward.z).normalized;
        }

        protected static Quaternion LookRotationSafe(Vector3 direction, Quaternion fallback)
        {
            return (direction.sqrMagnitude > 0.0f) ? Quaternion.LookRotation(direction, Vector3.up) : fallback;
        }

        /// <summary>Vector3::Approach — DApproach per component, without the AppApp smoothing.</summary>
        protected static Vector3 VectorApproach(Vector3 value, Vector3 goal, float min, float rate)
        {
            value.x = ComponentApproach(value.x, goal.x, min, rate);
            value.y = ComponentApproach(value.y, goal.y, min, rate);
            value.z = ComponentApproach(value.z, goal.z, min, rate);
            return value;
        }

        private static float ComponentApproach(float value, float goal, float min, float rate)
        {
            float diff = Mathf.Abs(goal - value);
            float speed = diff;

            if (min != 0.0f && diff <= min)
                speed = (diff * diff) / min;

            return Mathf.MoveTowards(value, goal, speed * rate);
        }

        protected Vector3 AverageWheelNormal(int first, int second)
        {
            Vector3 a, b;
            bool hasA = TryGetWheelNormal(first, out a);
            bool hasB = TryGetWheelNormal(second, out b);

            if (hasA && hasB)
                return (a + b) * 0.5f;

            if (hasA)
                return a;

            if (hasB)
                return b;

            return Vector3.zero;
        }

        protected virtual bool CameraCast(Vector3 from, Vector3 to, out RaycastHit hit)
        {
            hit = default(RaycastHit);

            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance <= 0.0f)
                return false;

            CollisionMask = ~LayerMask.GetMask("PlayerVehicleBody", "Banger", "VehicleBody");
            int count = Physics.RaycastNonAlloc(from, delta / distance, castBuffer, distance,
                                                CollisionMask, QueryTriggerInteraction.Ignore);

            bool found = false;
            float nearest = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                if (IsOwnCar(castBuffer[i].collider))
                    continue;

                if (castBuffer[i].distance < nearest)
                {
                    nearest = castBuffer[i].distance;
                    hit = castBuffer[i];
                    found = true;
                }
            }

            return found;
        }

        protected virtual bool IsOwnCar(Collider collider)
        {
            return Car != null && collider != null && collider.transform.IsChildOf(Car.transform);
        }

        // ---- Vehicle seams --------------------------------------------------------------
        // Everything the camera needs from the car funnels through here, so retargeting to
        // whatever the vehicle port ends up exposing is a local change.

        protected virtual bool TryGetWheelNormal(int index, out Vector3 normal)
        {
            VehCarSim sim = Car.VehCarSim;
            VehWheel wheel;

            switch (index)
            {
                case 0: wheel = sim.Wheels[0]; break;
                case 1: wheel = sim.Wheels[1]; break;
                case 2: wheel = sim.Wheels[2]; break;
                case 3: wheel = sim.Wheels[3]; break;
                default: normal = Vector3.zero; return false;
            }

            normal = wheel.LastSurfaceNormal;
            return wheel.LastGroundedStatus;
        }

        protected virtual float SteeringInput { get { return Car.VehCarSim.SteeringInput; } }
        protected virtual float Speed { get { return Car.VehCarSim.Speed; } }
        protected virtual int CurrentGear { get { return Car.VehCarSim.Transmission.CurrentGear; } }
        protected virtual float ThrottleInput { get { return Car.VehCarSim.Engine.ThrottleInput; } }
        protected virtual float HandBrakeInput { get { return Car.VehCarSim.HandBrakeInput; } }
        protected virtual int OnGroundCount { get { return Car.VehCarSim.OnGround(); } }

        protected virtual float AngularVelocitySqrMagnitude
        {
            get { return Car.VehCarSim.Body.angularVelocity.sqrMagnitude; }
        }
    }
}
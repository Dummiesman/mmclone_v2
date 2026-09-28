using UnityEngine;

namespace MM2.Camera
{
    public class camAppCS : camBaseCS
    {
        protected const float HalfPi = Mathf.PI * 0.5f;
        protected const float TwoPi = Mathf.PI * 2.0f;

        // The second Matrix34 at 0x4C — the goal that derived cameras write into.
        public Vector3 DesiredPosition;
        public Quaternion DesiredRotation = Quaternion.identity;

        // Was a Matrix34* to the target's world matrix.
        public Transform Target;

        // Offset from the target that the camera tracks/looks at.
        public Vector3 TrackTo = new Vector3(0.0f, 0.8f, 0.0f);

        // TrackTo resolved into world space (dword_d8..dword_e0).
        protected Vector3 TrackToLocal;

        public bool ApproachOn = false;
        public bool AppAppOn = false;

        public float AppRot = 0.5f;
        public float AppXRot = 0.5f;
        public float AppRotMin = 0.5f;
        public float AppPosMin = 0.5f;
        public float AppXZPos = 0.0f;
        public float AppYPos = 0.5f;
        public float AppApp = 0.1f;

        public float MinDist = 0.0f;
        public float MaxDist = 0.0f;
        public float LookAt = 0.01f;
        public float LookAbove = 0.0f;

        // dword_c8 — when set, ApproachIt snaps to the goal instead of smoothing.
        public bool ForceSnap;

        // DApproach smoothing state (dword_f0..dword_104).
        private float appRotAccumX, appRotAccumY, appRotAccumZ;
        private float appPosAccumX, appPosAccumY, appPosAccumZ;

        /// <summary>Called by derived cameras once they have filled in the goal.</summary>
        public void ApproachIt()
        {
            if (internalCamera == null)
                return;

            if (ApproachOn && !ForceSnap)
            {
                UpdateApproach();
                return;
            }

            internalCamera.transform.SetPositionAndRotation(DesiredPosition, DesiredRotation);
        }

        protected virtual void UpdateApproach()
        {
            Transform cam = internalCamera.transform;
            float dt = Time.deltaTime;

            if (Target != null)
                TrackToLocal = Target.TransformPoint(TrackTo);

            Vector3 position = cam.position;

            DApproach(ref position.x, DesiredPosition.x, AppPosMin, 0.0f, ref appPosAccumX, AppXZPos * dt);
            DApproach(ref position.y, DesiredPosition.y, AppPosMin, 0.0f, ref appPosAccumY, AppYPos * dt);
            DApproach(ref position.z, DesiredPosition.z, AppPosMin, 0.0f, ref appPosAccumZ, AppXZPos * dt);

            if (MaxDist != 0.0f)
                UpdateMaxDist(ref position);

            Vector3 current = ToEulerRad(cam.rotation);
            Vector3 goal = ToEulerRad(DesiredRotation);

            goal.x = Unwrap(current.x, goal.x);
            goal.y = Unwrap(current.y, goal.y);
            goal.z = Unwrap(current.z, goal.z);

            if (LookAt != 0.0f)
            {
                Vector3 to = TrackToLocal;
                to.y += LookAbove;

                // Uses the position from this frame's approach, as the original did.
                Vector3 dir = to - position;
                if (dir.sqrMagnitude > 0.0f)
                {
                    Vector3 look = ToEulerRad(Quaternion.LookRotation(dir, Vector3.up));

                    look.x = Unwrap(current.x, look.x);
                    look.y = Unwrap(current.y, look.y);
                    look.z = Unwrap(current.z, look.z);

                    // Blended as raw eulers, not slerped — matches the original.
                    goal = goal * (1.0f - LookAt) + look * LookAt;
                }
            }

            float xRot = (AppXRot == 0.0f) ? AppRot : AppXRot;

            float curX = current.x;
            float curY = current.y;
            float curZ = current.z;

            DApproach(ref curZ, goal.z, AppRotMin, 0.0f, ref appRotAccumZ, AppRot * dt);
            DApproach(ref curX, goal.x, AppRotMin, 0.0f, ref appRotAccumX, xRot * dt);
            DApproach(ref curY, goal.y, AppRotMin, 0.0f, ref appRotAccumY, AppRot * dt);

            cam.SetPositionAndRotation(position, FromEulerRad(new Vector3(curX, curY, curZ)));
        }

        public void UpdateMaxDist()
        {
            if (internalCamera == null)
                return;

            Vector3 position = internalCamera.transform.position;
            UpdateMaxDist(ref position);
            internalCamera.transform.position = position;
        }

        protected void UpdateMaxDist(ref Vector3 position)
        {
            if (MinDist > MaxDist)
                return;

            if (position == TrackToLocal)
                return;

            Vector3 offset = position - TrackToLocal;
            if (offset.sqrMagnitude > MaxDist * MaxDist)
                position = TrackToLocal + offset.normalized * MaxDist;

            offset = position - TrackToLocal;
            if (offset.sqrMagnitude < MinDist * MinDist)
                position = TrackToLocal + offset.normalized * MinDist;
        }

        /// <summary>
        /// Damped approach. Returns true once the value has landed on the goal.
        /// </summary>
        protected bool DApproach(ref float value, float goal, float min, float max, ref float accum, float rate)
        {
            float diff = Mathf.Abs(goal - value);
            float speed = diff;

            // Ease out once inside the min band.
            if (min != 0.0f && diff <= min)
                speed = (diff * diff) / min;

            // Never passed anything but 0 by the shipped callers.
            if (max != 0.0f && diff > max)
                speed = max;

            if (AppAppOn)
            {
                accum += (speed - accum) * AppApp;

                if (accum < 0.0f && speed > 0.0f)
                {
                    speed = 0.0f;
                    accum = 0.0f;
                }
                else if (accum <= 0.0f || speed >= 0.0f)
                {
                    speed = accum;
                }
                else
                {
                    speed = 0.0f;
                    accum = 0.0f;
                }
            }

            if (value < goal)
            {
                value += speed * rate;
                if (value > goal)
                    value = goal;
            }
            else if (value > goal)
            {
                value -= speed * rate;
                if (value < goal)
                    value = goal;
            }

            return value == goal;
        }

        public override void ReadSettings(TokenFileParser parser)
        {
            base.ReadSettings(parser);

            ApproachOn = parser.Read("ApproachOn", ApproachOn);
            AppAppOn = parser.Read("AppAppOn", AppAppOn);
            AppRot = parser.Read("AppRot", AppRot);
            AppXRot = parser.Read("AppXRot", AppXRot);
            AppYPos = parser.Read("AppYPos", AppYPos);
            AppXZPos = parser.Read("AppXZPos", AppXZPos);
            AppApp = parser.Read("AppApp", AppApp);
            AppRotMin = parser.Read("AppRotMin", AppRotMin);
            AppPosMin = parser.Read("AppPosMin", AppPosMin);
            LookAbove = parser.Read("LookAbove", LookAbove);
            TrackTo = parser.Read("TrackTo", TrackTo);
            MaxDist = parser.Read("MaxDist", MaxDist);
            MinDist = parser.Read("MinDist", MinDist);
            LookAt = parser.Read("LookAt", LookAt);
        }

        /// <summary>
        /// Unity applies eulers in Z, X, Y order, which is the "zxy" the original asked for.
        /// Kept in radians so AppRotMin and the App* rates stay on their original scale.
        /// </summary>
        protected static Vector3 ToEulerRad(Quaternion rotation)
        {
            Vector3 e = rotation.eulerAngles * Mathf.Deg2Rad;
            return new Vector3(WrapPi(e.x), WrapPi(e.y), WrapPi(e.z));
        }

        protected static Quaternion FromEulerRad(Vector3 eulers)
        {
            return Quaternion.Euler(eulers * Mathf.Rad2Deg);
        }

        protected static float WrapPi(float angle)
        {
            return Mathf.Repeat(angle + Mathf.PI, TwoPi) - Mathf.PI;
        }

        /// <summary>Shifts the goal so it takes the short way round instead of unwinding.</summary>
        protected static float Unwrap(float current, float goal)
        {
            if (current > HalfPi && goal < -HalfPi)
                goal += TwoPi;

            if (current < -HalfPi && goal > HalfPi)
                goal -= TwoPi;

            return goal;
        }
    }
}
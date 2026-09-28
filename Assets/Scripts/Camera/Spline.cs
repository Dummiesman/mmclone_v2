using UnityEngine;

namespace MM2
{
    /// <summary>
    /// Port of Spline. Animates N float channels toward a goal with a cubic Hermite curve,
    /// estimating the incoming slope from the last two frames' values so a new goal set
    /// mid-move continues smoothly instead of snapping direction.
    ///
    /// The original derived from asNode and owned no storage — Init took a pointer to the
    /// caller's value array. This version owns it.
    /// </summary>
    [System.Serializable]
    public class Spline
    {
        // dword_58 — 1 solves pure cubic, 0 pure linear, between blends the two.
        public float Smoothness = 1.0f;

        // dword_5c — duration used when SetGoal is passed 0.
        public float DefaultTime = 1.0f;

        private int npoints;

        private float[] v;      // current values
        private float[] prev;   // dword_40 — value as of last Update
        private float[] prev2;  // dword_44 — value as of the Update before that

        private float[] a, b, c, d;
        private float[] cc0, cc1, cc2, cc3;

        private float t0, t1, t2, t3;
        private float timeStart, timeEnd;
        private bool wasInRange;

        private float[] scratch;

        public Spline()
        {
        }

        public Spline(int numPoints)
        {
            Init(numPoints);
        }

        public int Count { get { return npoints; } }

        public float this[int index]
        {
            get { return v[index]; }
            set { v[index] = value; }
        }

        /// <summary>Convenience for the 3-channel splines the cameras use.</summary>
        public Vector3 Value
        {
            get { return new Vector3(v[0], v[1], v[2]); }
            set { v[0] = value.x; v[1] = value.y; v[2] = value.z; }
        }

        public Vector3 Goal
        {
            get { return new Vector3(d[0], d[1], d[2]); }
        }

        public void Init(int numPoints)
        {
            npoints = numPoints;

            v = new float[numPoints];
            prev = new float[numPoints];
            prev2 = new float[numPoints];

            a = new float[numPoints];
            b = new float[numPoints];
            c = new float[numPoints];
            d = new float[numPoints];

            cc0 = new float[numPoints];
            cc1 = new float[numPoints];
            cc2 = new float[numPoints];
            cc3 = new float[numPoints];

            scratch = new float[numPoints];

            Smoothness = 1.0f;
            DefaultTime = 1.0f;

            t0 = 0.0f;
            t1 = 0.0f;
            t2 = 0.0f;
            t3 = 0.0f;

            timeStart = 0.0f;
            timeEnd = 0.0099999998f;

            SetValue(v);
        }

        public void Update()
        {
            FixTimeStop();

            timeStart = timeEnd;
            timeEnd = Time.time;

            for (int i = 0; i < npoints; i++)
            {
                prev2[i] = prev[i];
                prev[i] = v[i];
            }
        }

        public void Solve(float time)
        {
            if (time == 0.0f)
                time = Time.time;

            for (int i = 0; i < npoints; i++)
            {
                float cubic = 0.0f;
                bool haveCubic = false;

                if (Smoothness != 0.0f)
                {
                    float u = Mathf.Max(time - t1, 0.000099999997f);

                    // Holds the end value once the segment is over.
                    float span = t2 - t1;
                    if (span < u)
                        u = span;

                    cubic = ((cc3[i] * u + cc2[i]) * u + cc1[i]) * u + cc0[i];

                    if (Smoothness == 1.0f)
                    {
                        v[i] = cubic;
                        continue;
                    }

                    haveCubic = true;
                }

                float fraction = (t1 == t2) ? 1.0f : ((time - t1) / (t2 - t1));
                float linear = b[i] + (c[i] - b[i]) * fraction;

                v[i] = haveCubic ? (cubic + (linear - cubic) * (1.0f - Smoothness)) : linear;
            }
        }

        public void SetValue(Vector3 value)
        {
            scratch[0] = value.x;
            scratch[1] = value.y;
            scratch[2] = value.z;
            SetValue(scratch);
        }

        public void SetValue(float[] values)
        {
            if (npoints == 0)
            {
                Debug.LogError("Spline.SetValue() - Not Initialized");
                return;
            }

            for (int i = 0; i < npoints; i++)
            {
                v[i] = values[i];
                prev[i] = v[i];
                prev2[i] = prev[i];
            }

            t0 = timeStart;
            t1 = timeEnd;
            t2 = Time.time;
            t3 = Time.time;
        }

        public void SetGoal(Vector3 goal, float time)
        {
            scratch[0] = goal.x;
            scratch[1] = goal.y;
            scratch[2] = goal.z;
            SetGoal(scratch, time);
        }

        public void SetGoal(float[] goal, float time)
        {
            if (npoints == 0)
            {
                Debug.LogError("Spline.SetGoal() - Not Initialized");
                return;
            }

            FixTimeStop();

            for (int i = 0; i < npoints; i++)
            {
                a[i] = prev2[i];
                b[i] = prev[i];
                d[i] = goal[i];
                c[i] = d[i];
            }

            t0 = timeStart;
            t1 = timeEnd;

            float duration = (time == 0.0f) ? DefaultTime : time;

            t2 = duration + Time.time;
            t3 = timeEnd - timeStart + t2;

            if (timeEnd != t2)
            {
                CalcCoeff();
                return;
            }

            Debug.LogError("Spline.SetGoal() - DeltaTime==0");
            SetValue(goal);
        }

        /// <summary>Clears the curve and parks every channel at zero.</summary>
        public void Reset()
        {
            for (int i = 0; i < npoints; i++)
            {
                a[i] = 0.0f;
                b[i] = 0.0f;
                c[i] = 0.0f;
                d[i] = 0.0f;

                cc0[i] = 0.0f;
                cc1[i] = 0.0f;
                cc2[i] = 0.0f;
                cc3[i] = 0.0f;

                scratch[i] = 0.0f;
            }

            SetValue(scratch);
        }

        /// <summary>True while a move is running, plus one final true on the frame it lands.</summary>
        public bool InRange()
        {
            float now = Time.time;

            if (now <= t1 || now >= t2)
            {
                if (wasInRange)
                {
                    wasInRange = false;
                    return true;
                }

                return false;
            }

            wasInRange = true;
            return true;
        }

        /// <summary>Resyncs after a stall so a long hitch doesn't fling the curve.</summary>
        private void FixTimeStop()
        {
            if (Time.time - timeEnd <= 0.30000001f)
                return;

            float resync = Time.time - Time.deltaTime;

            timeEnd = resync;
            timeStart = resync - Time.deltaTime;
        }

        private void CalcCoeff()
        {
            float span = Mathf.Max(t2 - t1, 0.000099999997f);
            float spanSq = span * span;

            for (int i = 0; i < npoints; i++)
            {
                float start = b[i];
                float end = c[i];

                float inSpan = Mathf.Max(Mathf.Abs(t1 - t0), 0.000099999997f);
                float inSlope = (b[i] - a[i]) / inSpan;

                float outSpan = Mathf.Max(Mathf.Abs(t3 - t2), 0.000099999997f);
                float outSlope = (d[i] - c[i]) / outSpan;

                float delta = end - start;

                cc0[i] = start;
                cc1[i] = inSlope;
                cc2[i] = delta * 3.0f / spanSq - (inSlope + inSlope) / span - outSlope / span;
                cc3[i] = (outSlope + inSlope) / spanSq - (delta + delta) / (spanSq * span);
            }
        }
    }
}
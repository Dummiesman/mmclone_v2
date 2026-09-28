using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[System.Flags]
public enum ParticleGizmoFlags
{
    None = 0,
    Emitter = 1 << 0, // emitter origin + birth velocity + AdditionalVelocity
    EmitBox = 1 << 1, // the position-variation volume particles are born in
    Bounds = 1 << 2, // the sim's computed Bounds
    Particles = 1 << 3, // wire sphere per particle, tinted by Color/Alpha
    Velocities = 1 << 4, // per-particle velocity arrows
    CollisionProbes = 1 << 5, // the exact ray Collide() casts, red when it currently hits
    GroundPlane = 1 << 6, // the y=0 plane CollideCheap() tests against on mobile
    Labels = 1 << 7, // text overlay (editor only)
}

// NOTE: change your declaration in the main file to:
//     public partial class ParticleSim : MonoBehaviour
public partial class ParticleSim
{
    [Header("Gizmos")]
    public ParticleGizmoFlags GizmoFlags = ParticleGizmoFlags.Emitter | ParticleGizmoFlags.EmitBox | ParticleGizmoFlags.Bounds;

    [Tooltip("Draw gizmos even when this object isn't selected.")]
    public bool GizmosWhenDeselected = false;

    [Tooltip("Cap on how many particles get gizmos, so a 1000-particle sim doesn't kill the editor.")]
    [Range(1, 1023)]
    public int MaxGizmoParticles = 128;

    [Tooltip("World units drawn per unit of velocity.")]
    public float GizmoVelocityScale = 0.1f;

    private static readonly Color kColEmitter = new Color(1.00f, 0.85f, 0.20f);
    private static readonly Color kColEmitBox = new Color(1.00f, 0.85f, 0.20f, 0.45f);
    private static readonly Color kColBounds = new Color(0.20f, 0.90f, 1.00f, 0.75f);
    private static readonly Color kColVelocity = new Color(0.35f, 1.00f, 0.45f, 0.80f);
    private static readonly Color kColProbe = new Color(1.00f, 1.00f, 1.00f, 0.35f);
    private static readonly Color kColProbeHit = new Color(1.00f, 0.15f, 0.15f, 1.00f);
    private static readonly Color kColGround = new Color(0.60f, 0.60f, 1.00f, 0.50f);
    private static readonly Color kColWind = new Color(0.60f, 0.80f, 1.00f, 0.90f);

    private bool HasGizmo(ParticleGizmoFlags f) => (GizmoFlags & f) != 0;

    private void OnDrawGizmos()
    {
        bool selected = false;
#if UNITY_EDITOR
        selected = Selection.Contains(gameObject);
#endif
        if (!selected && !GizmosWhenDeselected)
            return;

        DrawSimGizmos();
    }

    private void DrawSimGizmos()
    {
        if (GizmoFlags == ParticleGizmoFlags.None)
            return;

        Gizmos.matrix = Matrix4x4.identity;

        Vector3 emitOrigin = transform.position;
        if (!IsLocal && BirthRule != null)
            emitOrigin = BirthRule.Position.Value;

        DrawEmitterGizmos(emitOrigin);
        DrawBoundsGizmos();
        DrawGroundPlaneGizmos(emitOrigin);
        DrawParticleGizmos();
        DrawLabelGizmos(emitOrigin);
    }

    //EMITTER
    private void DrawEmitterGizmos(Vector3 emitOrigin)
    {
        if (HasGizmo(ParticleGizmoFlags.Emitter))
        {
            Gizmos.color = kColEmitter;
            Gizmos.DrawWireSphere(emitOrigin, 0.05f);

            //axis cross so you can see orientation of a local emitter
            Gizmos.DrawLine(emitOrigin - transform.right * 0.1f, emitOrigin + transform.right * 0.1f);
            Gizmos.DrawLine(emitOrigin - transform.up * 0.1f, emitOrigin + transform.up * 0.1f);
            Gizmos.DrawLine(emitOrigin - transform.forward * 0.1f, emitOrigin + transform.forward * 0.1f);

            if (BirthRule != null)
            {
                //mean birth velocity
                DrawArrow(emitOrigin, BirthRule.Velocity.Value * GizmoVelocityScale, kColEmitter);

                //the variation envelope: min/max corner of the velocity spread
                Vector3 half = BirthRule.Velocity.Variation * 0.5f;
                Vector3 vMin = (BirthRule.Velocity.Value - half) * GizmoVelocityScale;
                Vector3 vMax = (BirthRule.Velocity.Value + half) * GizmoVelocityScale;
                Gizmos.color = kColEmitBox;
                Gizmos.DrawLine(emitOrigin, emitOrigin + vMin);
                Gizmos.DrawLine(emitOrigin, emitOrigin + vMax);
                Gizmos.DrawLine(emitOrigin + vMin, emitOrigin + vMax);
            }

            //AdditionalVelocity ("wind")
            if (AdditionalVelocity.sqrMagnitude > 0f)
                DrawArrow(emitOrigin, AdditionalVelocity * GizmoVelocityScale, kColWind);
        }

        if (HasGizmo(ParticleGizmoFlags.EmitBox) && BirthRule != null)
        {
            //Variation is the full width of the random range (Random.Range(-0.5, 0.5) * Variation)
            Vector3 size = BirthRule.Position.Variation;
            if (size.sqrMagnitude > 0f)
            {
                Gizmos.color = kColEmitBox;
                Gizmos.DrawWireCube(emitOrigin, size);
            }
        }
    }

    //BOUNDS
    private void DrawBoundsGizmos()
    {
        if (!HasGizmo(ParticleGizmoFlags.Bounds) || !CalculateBounds || !Application.isPlaying)
            return;

        Gizmos.color = kColBounds;
        Gizmos.DrawWireCube(bounds.center, bounds.size);
    }

    //CHEAP COLLISION PLANE
    private void DrawGroundPlaneGizmos(Vector3 emitOrigin)
    {
        if (!HasGizmo(ParticleGizmoFlags.GroundPlane))
            return;

        Gizmos.color = kColGround;
        Vector3 c = new Vector3(emitOrigin.x, GroundHeight, emitOrigin.z);
        const float s = 4f;
        const int divisions = 8;
        for (int i = 0; i <= divisions; i++)
        {
            float t = Mathf.Lerp(-s, s, i / (float)divisions);
            Gizmos.DrawLine(c + new Vector3(t, 0f, -s), c + new Vector3(t, 0f, s));
            Gizmos.DrawLine(c + new Vector3(-s, 0f, t), c + new Vector3(s, 0f, t));
        }
    }

    //PARTICLES
    private void DrawParticleGizmos()
    {
        if (particleInstances == null || numEmittedParticles == 0)
            return;

        bool drawParticles = HasGizmo(ParticleGizmoFlags.Particles);
        bool drawVelocities = HasGizmo(ParticleGizmoFlags.Velocities);
        bool drawProbes = HasGizmo(ParticleGizmoFlags.CollisionProbes);
        if (!drawParticles && !drawVelocities && !drawProbes)
            return;

        int count = Mathf.Min(numEmittedParticles, MaxGizmoParticles);
        for (int i = 0; i < count; i++)
        {
            var p = particleInstances[i];
            if (p == null)
                continue;

            if (drawParticles)
            {
                Color c = p.Color;
                c.a = Mathf.Clamp01(p.Alpha / 255f);
                Gizmos.color = c;
                Gizmos.DrawWireSphere(p.Position, Mathf.Max(p.Radius, 0.001f));

                //a short "up" tick rotated by Rotation so you can see DRotation working
                Quaternion rot = Quaternion.AngleAxis(p.Rotation, Vector3.forward);
                Gizmos.DrawRay(p.Position, rot * Vector3.up * p.Radius);
            }

            if (drawVelocities)
                DrawArrow(p.Position, p.Velocity * GizmoVelocityScale, kColVelocity);

            if (drawProbes && (p.BirthFlags & (ParticleBirthFlags.Collision | ParticleBirthFlags.KillOnCollision)) != 0)
                DrawCollisionProbe(p);
        }
    }

    private void DrawCollisionProbe(ParticleInstance p)
    {
        //mirrors ParticleInstance.Collide() exactly
        float probeLength = p.Radius * 2.01f;
        Vector3 dir = p.Velocity;
        if (dir.sqrMagnitude < 1e-10f)
            dir = Vector3.down;
        dir.Normalize();

        RaycastHit hit;
        bool didHit = Physics.Raycast(p.Position, dir, out hit, probeLength, CollisionLayerMask);

        Gizmos.color = didHit ? kColProbeHit : kColProbe;
        Gizmos.DrawRay(p.Position, dir * probeLength);

        if (didHit)
        {
            Gizmos.DrawWireSphere(hit.point, Mathf.Max(p.Radius * 0.25f, 0.005f));
            Gizmos.DrawRay(hit.point, hit.normal * Mathf.Max(p.Radius, 0.05f));

            //visualise the reflection the particle will take
            Gizmos.color = kColVelocity;
            Gizmos.DrawRay(hit.point, Vector3.Reflect(dir, hit.normal) * probeLength);
        }
    }

    //LABELS
    private void DrawLabelGizmos(Vector3 emitOrigin)
    {
#if UNITY_EDITOR
        if (!HasGizmo(ParticleGizmoFlags.Labels))
            return;

        int poolSize = particleInstances != null ? particleInstances.Length : 0;
        string header = $"{name}\n{numEmittedParticles} / {poolSize} particles";
        if (BirthRule == null)
            header += "\n<no BirthRule>";
        else if (EmitOverTime)
            header += $"\nspew {BirthRule.SpewRate}/s (t={emitOverTimeGlobalTimer:0.00})";
        if (Mathf.Abs(SimulationRate - 1f) > Mathf.Epsilon)
            header += $"\nsim rate {SimulationRate:0.00}x";

        Handles.color = Color.white;
        Handles.Label(emitOrigin + Vector3.up * 0.2f, header);

        //per-particle detail, but only when there are few enough to read
        if (particleInstances != null && numEmittedParticles > 0 && numEmittedParticles <= 16)
        {
            for (int i = 0; i < numEmittedParticles; i++)
            {
                var p = particleInstances[i];
                if (p == null)
                    continue;
                Handles.Label(p.Position + Vector3.up * (p.Radius + 0.02f),
                              $"[{i}] life {p.Life:0.00}  a {p.Alpha:0}  r {p.Radius:0.000}  f {p.CurrentTexFrame}");
            }
        }
#endif
    }

    //HELPERS
    private static void DrawArrow(Vector3 origin, Vector3 vec, Color color, float headScale = 0.22f)
    {
        if (vec.sqrMagnitude < 1e-10f)
            return;

        Gizmos.color = color;
        Vector3 tip = origin + vec;
        Gizmos.DrawLine(origin, tip);

        Vector3 dir = vec.normalized;
        Vector3 right = Vector3.Cross(dir, Vector3.up);
        if (right.sqrMagnitude < 1e-6f)
            right = Vector3.Cross(dir, Vector3.forward);
        right.Normalize();
        Vector3 up = Vector3.Cross(right, dir);

        float h = vec.magnitude * headScale;
        Vector3 baseP = tip - dir * h;
        Gizmos.DrawLine(tip, baseP + right * h * 0.5f);
        Gizmos.DrawLine(tip, baseP - right * h * 0.5f);
        Gizmos.DrawLine(tip, baseP + up * h * 0.5f);
        Gizmos.DrawLine(tip, baseP - up * h * 0.5f);
    }
}
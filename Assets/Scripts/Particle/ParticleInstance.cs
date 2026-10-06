using UnityEngine;

public class ParticleInstance
{
    public const float MaxAlpha = 255f;

    public Vector3 Position => position;
    private Vector3 position;

    public float Rotation { get; private set; }

    public Vector3 Velocity => velocity;
    private Vector3 velocity;

    public Color Color { get; private set; }
    public float Life { get; private set; }
    public float Mass { get; private set; }
    public float Radius { get; private set; }
    public float Drag { get; private set; }
    public float Damp { get; private set; }
    public float DRadius { get; private set; }
    public float DAlpha { get; private set; }
    public float DRotation { get; private set; }
    public float Gravity { get; private set; }

    public int TexFrameStart { get; private set; }
    public int TexFrameEnd { get; private set; }
    public int CurrentTexFrame { get; private set; }
    private float texFrameTimer;

    public ParticleBirthFlags BirthFlags { get; private set; }
    public float Alpha { get; private set; } = MaxAlpha;

    public bool CollideCheap(float height)
    {
        return (position.y - Radius) < height;
    }

    public void ProcessCollideCheap(float height)
    {
        // place the particle back on top of the plane, otherwise a slow particle
        // sits under it and flips its velocity every frame
        position.y = height + Radius;

        velocity *= Damp;
        velocity.y = Mathf.Abs(velocity.y);
        DRotation *= Damp;
    }

    public bool Collide(float timeStep, int layerMask, out RaycastHit hitInfo)
    {
        hitInfo = default(RaycastHit);

        float speed = velocity.magnitude;
        if (speed < Mathf.Epsilon)
            return false;

        // probe at least a diameter ahead, but far enough to catch this frame's travel too
        float distance = Mathf.Max(Radius * 2.01f, (speed * timeStep) + Radius);
        return Physics.Raycast(position, velocity / speed, out hitInfo, distance, layerMask);
    }

    public void ProcessCollide(RaycastHit hitInfo)
    {
        float speed = velocity.magnitude;
        if (speed < Mathf.Epsilon)
            return;

        Vector3 direction = velocity / speed;
        Vector3 reflection = Vector3.Reflect(direction, hitInfo.normal);

        velocity = reflection * (speed * Damp);

        var hitRigidbody = hitInfo.rigidbody;
        if (hitRigidbody != null)
        {
            // how head-on the impact was: 1 = straight into the surface, 0 = grazing
            float impact = Mathf.Clamp01(-Vector3.Dot(direction, hitInfo.normal));
            velocity += hitRigidbody.velocity * (impact * Damp);
        }

        DRotation *= Damp;

        // push out of the surface instead of stepping the sim, so we can't re-hit it next frame
        position = hitInfo.point + (hitInfo.normal * Radius);
    }

    public void Update(float timeStep, ParticleSim parent = null)
    {
        float dt60 = timeStep * 60f;

        Vector3 additionalVelocity = (parent != null) ? parent.AdditionalVelocity : Vector3.zero;
        Vector3 totalVelocity = velocity + additionalVelocity;

        // drag. clamped at -1 so a big timestep or a very fast particle can never
        // overshoot and invert the velocity
        float dragScale = Mathf.Max(-(totalVelocity.magnitude * Drag) * Mass * timeStep, -1f);
        velocity.x += totalVelocity.x * dragScale;
        velocity.z += totalVelocity.z * dragScale;
        velocity.y += (totalVelocity.y * dragScale) + (Gravity * timeStep);

        // AdditionalVelocity advects the particle as well as feeding drag
        position += (velocity + additionalVelocity) * timeStep;

        // alpha
        if (Mathf.Abs(DAlpha) > Mathf.Epsilon)
        {
            Alpha = Mathf.Clamp(Alpha + (DAlpha * dt60), 0f, MaxAlpha);
        }

        // scale
        Radius += (dt60 * DRadius);

        // life
        Life -= timeStep;

        // rotation
        Rotation += (dt60 * DRotation);

        // animate, if specified
        if ((BirthFlags & ParticleBirthFlags.Animated) != 0 && TexFrameEnd > TexFrameStart)
        {
            float frameRate = (parent != null) ? parent.TexFrameRate : 0f;
            if (frameRate > 0f)
            {
                texFrameTimer += timeStep * frameRate;
                while (texFrameTimer >= 1f)
                {
                    texFrameTimer -= 1f;
                    CurrentTexFrame++;
                    if (CurrentTexFrame > TexFrameEnd)
                        CurrentTexFrame = TexFrameStart;
                }
            }
        }
    }

    public void SetMatrix(ref Matrix4x4 matrix)
    {
        matrix.SetTRS(Position, Quaternion.AngleAxis(Rotation, Vector3.forward), Vector3.one * (Radius * 2f));
    }

    public void InitFromSim(ParticleSim sim)
    {
        if (sim.BirthRule == null)
        {
            Debug.LogError("An attempt was made to initialize a ParticleInstance with a NULL BirthRule, aborting!", sim);
            return;
        }

        // 
        var rule = sim.BirthRule;
        float particleRandom()
        {
            return Random.Range(-0.5f, 0.5f);
        }

        // position
        Vector3 positionVariance;
        positionVariance.x = rule.Position.Variation.x * particleRandom();
        positionVariance.y = rule.Position.Variation.y * particleRandom();
        positionVariance.z = rule.Position.Variation.z * particleRandom();

        if (sim.IsLocal)
        {
            position = sim.transform.position + positionVariance;
        }
        else
        {
            position = rule.Position.Value + positionVariance;
        }

        // velocity
        velocity.x = rule.Velocity.Value.x + (rule.Velocity.Variation.x * particleRandom());
        velocity.y = rule.Velocity.Value.y + (rule.Velocity.Variation.y * particleRandom());
        velocity.z = rule.Velocity.Value.z + (rule.Velocity.Variation.z * particleRandom());

        // life
        Life = rule.Life.Value + (rule.Life.Variation * particleRandom());

        // mass
        Mass = rule.Mass.Value + (rule.Mass.Variation * particleRandom());

        // radius
        Radius = rule.Radius.Value + (rule.Radius.Variation * particleRandom());

        // drag
        Drag = rule.Drag.Value + (rule.Drag.Variation * particleRandom());

        // damp
        Damp = rule.Damp.Value + (rule.Damp.Variation * particleRandom());

        // dradius dalpha drotation
        DRadius = rule.DRadius.Value + (rule.DRadius.Variation * particleRandom());
        DAlpha = rule.DAlpha.Value + (rule.DAlpha.Variation * particleRandom());
        DRotation = rule.DRotation.Value + (rule.DRotation.Variation * particleRandom());

        // flags
        BirthFlags = rule.BirthFlags;

        // gravity
        Gravity = rule.Gravity;

        // alpha + rotation reset (instances are pooled and reused)
        Alpha = MaxAlpha;
        Rotation = 0f;

        // color
        Color = rule.Color;

        // tile range + starting frame
        TexFrameStart = rule.TexFrameStart;
        TexFrameEnd = rule.TexFrameEnd;
        texFrameTimer = 0f;

        if ((rule.BirthFlags & ParticleBirthFlags.Animated) != 0)
        {
            CurrentTexFrame = TexFrameStart;
        }
        else
        {
            CurrentTexFrame = Random.Range(TexFrameStart, TexFrameEnd + 1);
        }
    }
}
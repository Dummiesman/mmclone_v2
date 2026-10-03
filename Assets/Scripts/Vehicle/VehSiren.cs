using System.Linq;
using UnityEngine;

public class VehSiren : VehSubsystem
{
    public bool HasLights => LightCount > 0;
    public int LightCount => lightCount;
    private int lightCount = 0;

    private LightGlow[] glows = new LightGlow[8];
    private float[] glowRotations = new float[8];
    private float rotationRate = 2.5f;

    public void Activate()
    {
        this.enabled = true;
    }

    public void Deactivate()
    {
        this.enabled = false;
    }

    public override void Init(VehCar car)
    {
        base.Init(car);

        var sirens = car.Model.Sirens;
        for(int i=0; i < glows.Length && i < Car.Model.Sirens.Count; i++)
        {
            var sirenObject = Car.Model.Sirens[i];
            if(sirenObject != null)
            {
                lightCount++;
                glows[i] = new LightGlow()
                {
                    SpotExponent = 3.0f
                };
            }
            glowRotations[i] = i * 1.5707964f;
        }
    }

    public override void Update()
    {
        for(int i=0; i < glowRotations.Length; i++)
        {
            glowRotations[i] = (glowRotations[i] + (Time.deltaTime * rotationRate * Mathf.PI)) % (Mathf.PI * 2.0f);
        }
    }

    private void LateUpdate()
    {
        var baseDir = Car.transform.forward;
        for (int i = 0; i < glows.Length; i++)
        {
            var sirenObject = Car.Model.Sirens[i];
            if (sirenObject != null)
            {
                Vector3 direction = Quaternion.AngleAxis(
                    glowRotations[i] * Mathf.Rad2Deg,
                    Vector3.up
                ) * baseDir;

                glows[i].Position = sirenObject.transform.position;
                glows[i].Direction = direction;
                glows[i].Color = Car.Model.SirenColors[i];

                if (ViewportManager.MainViewport != null && ViewportManager.MainViewport.ActiveCamera != null)
                {
                    var eyePos = ViewportManager.MainViewport.ActiveCamera.transform.position;
                    glows[i].DrawGlow(eyePos);
                }
            }
        }
    }
}

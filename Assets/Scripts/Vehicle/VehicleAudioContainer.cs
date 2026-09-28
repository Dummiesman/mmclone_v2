using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VehicleAudioContainer : VehSubsystem
{
    public static string SirenCSVName = string.Empty;

    public VehicleAudio Audio => audio;
    public SuspensionAudio SuspensionAudio => suspensionAudio;
    public SurfaceAudio SurfaceAudio => surfaceAudio;
    public ImpactAudio ImpactAudio => impactAudio;
    public SirenAudio SirenAudio => sirenAudio;

    private new VehicleAudio audio;
    private SuspensionAudio suspensionAudio;
    private SurfaceAudio surfaceAudio;
    private ImpactAudio impactAudio;
    private SirenAudio sirenAudio;

    public override void Init(VehCar car)
    {
        base.Init(car);

        var audioRoot = new GameObject("Car Audio");
        audioRoot.transform.parent = this.transform;

        var vehAudioRoot = new GameObject("Engine");
        vehAudioRoot.transform.parent = audioRoot.transform;
        audio = vehAudioRoot.AddComponent<VehicleAudio>();
        audio.Init(car.Basename, car);

        var susAudioRoot = new GameObject("Suspension");
        susAudioRoot.transform.parent = audioRoot.transform;
        suspensionAudio = susAudioRoot.AddComponent<SuspensionAudio>();
        suspensionAudio.Init(car);

        var surfAudioRoot = new GameObject("SurfaceAudio");
        surfAudioRoot.transform.parent = audioRoot.transform;
        surfaceAudio = surfAudioRoot.AddComponent<SurfaceAudio>();
        surfaceAudio.Init(car, GameState.SelectedWeather);

        var impactAudioGroup = new GameObject("ImpactAudio");
        impactAudioGroup.transform.parent = audioRoot.transform;
        impactAudio = impactAudioGroup.AddComponent<ImpactAudio>();
        impactAudio.Init((car.Type != vehCarType.Player));

        if(car.Siren != null && car.Siren.LightCount > 0)
        {
            var sirenAudioGroup = new GameObject("SirenAudio");
            sirenAudioGroup.transform.parent = audioRoot.transform;
            sirenAudio = sirenAudioGroup.AddComponent<SirenAudio>();
            SirenAudio.Init(SirenCSVName, (car.Type != vehCarType.Player));
        }
    }

    public void ActivateSiren()
    {
        if(sirenAudio != null)
        {
            sirenAudio.Activate();
        }
    }

    public void DeactivateSiren()
    {
        if(sirenAudio != null)
        {
            sirenAudio.Deactivate();
        }
    }

    public void PlayExplosion()
    {
        if (sirenAudio != null)
        {
            sirenAudio.PlayExplosion();
        }
    }

    public void PlayHorn()
    {
        audio.UpdateHorn(true);
    }

    public void StopHorn()
    {
        audio.UpdateHorn(false);
    }

    public void UpdateHorn(bool state)
    {
        audio.UpdateHorn(state);
    }

    public void PlaySplash()
    {
        impactAudio.Collision(22, 9999.0f); // Fake a collision with SWIMWITHFISHES
    }

    public override void Update()
    {
    }

    public void Reset()
    {
        StopHorn();
        DeactivateSiren();
    }
}

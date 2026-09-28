using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VehTransmission
{
    public enum TransmissionMode
    {
        Automatic,
        Manual
    }

    // The original's tables are fixed float[8] struct members.
    public const int MaxGears = 8;

    // Original vehTransmission ctor values. These matter: Read() uses the
    // current value as its fallback, so a token missing from the car file must
    // land on the game's default, not on zero.
    private int ManualNumGears = 7;
    private int AutoNumGears = 6;
    private float ReverseSpeed = 20f;
    private float LowSpeed = 20f;
    private float HighSpeed = 75f;
    private float GearBias = 0.5f;
    private float UpshiftBias = 0.05f;
    private float DownshiftBiasMin = 0.05f;
    private float DownshiftBiasMax = 0.3f;
    private float GearChangeTime = 0.8f;

    public TransmissionMode Mode { get; set; } = TransmissionMode.Automatic;

    private int currentGear = 2;

    private float gearChangeTimer = 0f;
    private VehCarSim carSim;

    public int CurrentGear => currentGear;
    public bool IsAutomatic => (Mode == TransmissionMode.Automatic);

    public IReadOnlyList<float> AutoGearRatios => autoGearRatios;
    public IReadOnlyList<float> ManualGearRatios => manualGearRatios;

    public IReadOnlyList<float> UpshiftRpms => upshiftRpms;
    public IReadOnlyList<float> DownshiftMinRpms => downshiftMinRpms;
    public IReadOnlyList<float> DownshiftMaxRpms => downshiftMaxRpms;

    private List<float> autoGearRatios = new List<float>(MaxGears);
    private List<float> manualGearRatios = new List<float>(MaxGears);

    private List<float> downshiftMinRpms = new List<float>(MaxGears);
    private List<float> downshiftMaxRpms = new List<float>(MaxGears);
    private List<float> upshiftRpms = new List<float>(MaxGears);

    public bool GearChanged = false;

    public VehTransmission()
    {
        // vehTransmission::vehTransmission fills all 8 slots before calling
        // Reset(), so the tables are queryable (and dumpable) even if
        // ComputeConstants is never reached.
        ResetTablesToDefaults();
        Reset();
    }

    private void ResetTablesToDefaults()
    {
        manualGearRatios.Clear();
        autoGearRatios.Clear();
        upshiftRpms.Clear();
        downshiftMinRpms.Clear();
        downshiftMaxRpms.Clear();

        manualGearRatios.Add(-10f);
        manualGearRatios.Add(0f);
        autoGearRatios.Add(-10f);
        autoGearRatios.Add(0f);

        upshiftRpms.Add(0f);
        upshiftRpms.Add(0f);
        downshiftMinRpms.Add(0f);
        downshiftMinRpms.Add(0f);
        downshiftMaxRpms.Add(0f);
        downshiftMaxRpms.Add(0f);

        for (int i = 2; i < MaxGears; i++)
        {
            float ratio = 30f / i;

            manualGearRatios.Add(ratio);
            autoGearRatios.Add(ratio);
            upshiftRpms.Add(6000f);
            downshiftMinRpms.Add(2000f);
            downshiftMaxRpms.Add(2000f);
        }
    }

    public void Read(TokenFileParser parser)
    {
        ManualNumGears = parser.Read("ManualNumGears", ManualNumGears);
        AutoNumGears = parser.Read("AutoNumGears", AutoNumGears);
        ReverseSpeed = parser.Read("Reverse", ReverseSpeed);
        LowSpeed = parser.Read("Low", LowSpeed);
        HighSpeed = parser.Read("High", HighSpeed);
        GearBias = parser.Read("GearBias", GearBias);
        UpshiftBias = parser.Read("UpshiftBias", UpshiftBias);
        DownshiftBiasMin = parser.Read("DownshiftBiasMin", DownshiftBiasMin);
        DownshiftBiasMax = parser.Read("DownshiftBiasMax", DownshiftBiasMax);
        GearChangeTime = parser.Read("GearChangeTime", GearChangeTime);
        ManualNumGears = Mathf.Clamp(ManualNumGears, 3, MaxGears);
        AutoNumGears = Mathf.Clamp(AutoNumGears, 3, MaxGears);
    }

    private float GearRatioFromMPH(float mph)
    {
        float optRpm = carSim.Engine.OptRPM;
        float wheelRad = carSim.PrimaryDrivetrain.Wheels[0].Radius;
        return optRpm / (26.822235479112f * mph / (wheelRad * 6.2831855f));
    }

    public void ComputeConstants()
    {
        ResetTablesToDefaults();

        var engine = this.carSim.Engine;

        // Gear 0 = reverse, gear 1 = neutral; both share the same ratio table.
        float reverseRatio = GearRatioFromMPH(ReverseSpeed);
        float firstRatio = GearRatioFromMPH(LowSpeed);
        float lastRatio = GearRatioFromMPH(HighSpeed);

        BuildRatios(autoGearRatios, AutoNumGears, reverseRatio, firstRatio, lastRatio);
        BuildRatios(manualGearRatios, ManualNumGears, reverseRatio, firstRatio, lastRatio);

        // Top gear never upshifts, so its threshold is the rev limit.
        upshiftRpms[AutoNumGears - 1] = engine.MaxRPM;

        if ((AutoNumGears - 1) > 2)
        {
            for (int i = 2; i < (AutoNumGears - 1); i++)
            {
                float maxRpm = engine.MaxRPM;
                float optRpm = engine.OptRPM;

                // Speed ratio across the i -> i+1 shift. Always < 1.
                float step = autoGearRatios[i + 1] / autoGearRatios[i];
                if ((step * optRpm) < maxRpm)
                    maxRpm = optRpm / step;

                // Bisect for the RPM where power in gear i equals power in i+1.
                float midpoint = 0f;
                while ((maxRpm - optRpm) > 1f)
                {
                    midpoint = (maxRpm + optRpm) * 0.5f;
                    float midOmega = midpoint * VehEngine.RpmToRadPerSec;

                    float midHP = engine.CalcHPAtFullThrottle(midOmega);
                    if (engine.CalcHPAtFullThrottle(midOmega * step) <= midHP)
                        optRpm = midpoint;
                    else
                        maxRpm = midpoint;
                }

                midpoint = (maxRpm + optRpm) * 0.5f;

                upshiftRpms[i] = (UpshiftBias + 1f) * midpoint;

                // NOTE THE i + 1. These describe gear i+1's downshift point,
                // expressed as the RPM you land on after shifting into it.
                downshiftMinRpms[i + 1] = (1f - DownshiftBiasMin) * midpoint * step;
                downshiftMaxRpms[i + 1] = (1f - DownshiftBiasMax) * midpoint * step;
            }
        }

        // zero out neutral
        downshiftMinRpms[2] = 0f;
        downshiftMaxRpms[2] = 0f;
    }

    /// <summary>
    /// Geometric ratio spread between first and last, bent by GearBias.
    /// Slot 0 = reverse, 1 = neutral, 2 = first, numGears-1 = last.
    /// The list arrives pre-sized to MaxGears and pre-filled with the ctor
    /// defaults, so this writes slots 0..numGears-1 and leaves the tail as-is.
    /// </summary>
    private void BuildRatios(List<float> ratios, int numGears,
                             float reverseRatio, float firstRatio, float lastRatio)
    {
        ratios[0] = -reverseRatio;
        ratios[1] = 0f;
        ratios[2] = firstRatio;
        ratios[numGears - 1] = lastRatio;

        int numForward = numGears - 2;   // gears other than reverse/neutral
        if (numForward > 2)
        {
            int numFiller = numGears - 3;  // gears strictly between first and last
            float spread = Mathf.Pow(lastRatio / firstRatio, 1f / numFiller);

            for (int i = 1; i < numFiller; i++)
            {
                float exponent = i - (i - numForward + 1) * i * GearBias / numFiller;
                ratios[i + 2] = Mathf.Pow(spread, exponent) * firstRatio;
            }
        }
    }

    public void Init(VehCarSim carSim)
    {
        this.carSim = carSim;
    }

    public void SetReverse() => SetCurrentGear(0);

    public void SetNeutral() => SetCurrentGear(1);

    public void SetForward()
    {
        if (currentGear <= 1)
            SetCurrentGear(2);
    }

    public void Upshift()
    {
        if (Mode == TransmissionMode.Automatic)
        {
            // In auto, the driver's "upshift" only walks R -> N -> D.
            if (currentGear < 2)
                SetCurrentGear(currentGear + 1);
        }
        else
        {
            if (currentGear < ManualNumGears - 1)
                SetCurrentGear(currentGear + 1);
        }
    }

    public void Downshift()
    {
        if (Mode == TransmissionMode.Automatic)
        {
            if (currentGear == 0)
                return;
            SetCurrentGear(currentGear >= 2 ? 1 : 0);
        }
        else
        {
            if (currentGear > 0)
                SetCurrentGear(currentGear - 1);
        }
    }

    public void SetCurrentGear(int gear)
    {
        if (gear == currentGear)
            return;

        if (Mode == TransmissionMode.Automatic && gear >= AutoNumGears)
            return;
        if (Mode == TransmissionMode.Manual && gear >= ManualNumGears)
            return;
        if (gear < 0)
            return;

        gearChangeTimer = 0f;
        GearChanged = true;
        currentGear = gear;
    }

    public float GetCurrentGearRatio()
    {
        return (Mode == TransmissionMode.Automatic)
            ? autoGearRatios[currentGear]
            : manualGearRatios[currentGear];
    }

    public float GetGearRatio(TransmissionMode type, int gear)
    {
        return (type == TransmissionMode.Automatic) ? autoGearRatios[gear] : manualGearRatios[gear];
    }

    public void Update() => UpdateInternal(Time.fixedDeltaTime);

    public void UpdateWithTimestep(float dt) => UpdateInternal(dt);

    void UpdateInternal(float dt)
    {
        if (carSim.OnGround() == 0)
            return;

        if (Mode == TransmissionMode.Automatic &&
            currentGear > 1 &&
            gearChangeTimer > GearChangeTime &&
            !GearChanged)
        {
            var engine = carSim.Engine;

            if (currentGear >= AutoNumGears - 1 ||
                engine.CurrentRPM <= upshiftRpms[currentGear])
            {
                float threshold =
                    (1f - engine.ThrottleInput) * downshiftMaxRpms[currentGear] +
                    engine.ThrottleInput * downshiftMinRpms[currentGear];

                if (currentGear > 2 && threshold > engine.CurrentRPM)
                    SetCurrentGear(currentGear - 1);
            }
            else
            {
                SetCurrentGear(currentGear + 1);
            }
        }

        // The original accumulates TimeInGear AFTER the shift decision, so the
        // gear has to be held for strictly longer than GearChangeTime.
        gearChangeTimer += dt;
    }

    public void Reset()
    {
        SetCurrentGear(2);
        gearChangeTimer = 0f;
        GearChanged = false;
    }
}
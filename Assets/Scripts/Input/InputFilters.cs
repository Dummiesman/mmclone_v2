using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MMMouseInputFilter : MMInputFilterBase
{
    public override void Update(float speed, float steerInput, float sensitivityMultiplier)
    {
        steerInput = (steerInput / CalcSensitivity(speed)) * sensitivityMultiplier;
        steerInput = Mathf.Clamp(steerInput, -1f, 1f);


        float steerFilter = CalcSteerFilter(speed);
        Value = Mathf.Pow(Mathf.Abs(steerInput), steerFilter) * Mathf.Sign(steerInput);
    }
}

public class MMDiscreteInputFilter : MMInputFilterBase
{
    public float DiscreteSteeringDeltaOutLo = 3.5f;
    public float DiscreteSteeringDeltaOutHi = 2.5f;
    public float DiscreteSteeringDeltaInLo = 2.5f;
    public float DiscreteSteeringDeltaInHi = 1.5f;
    public float DiscreteSteeringFilterLo = 2f;
    public float DiscreteSteeringFilterHi = 1f;

    private float valuePreFilter = 0f;

    public override void Reset()
    {
        valuePreFilter = 0f;
    }

    public override void Update(float speed, float steerInput, float sensitivityMultiplier)
    {
        steerInput = Mathf.Clamp(steerInput, -1f, 1f);
        float filter = CalcDiscreteFilter(speed);

        //get delta
        float delta;
        if (Mathf.Abs(steerInput) < Mathf.Abs(valuePreFilter) || Mathf.Sign(steerInput) != Mathf.Sign(valuePreFilter))
        {
            delta = CalcDiscreteDeltaIn(speed);
        }
        else
        {
            delta = CalcDiscreteDeltaOut(speed);
        }

        //update prefilter value
        if (valuePreFilter < steerInput)
        {
            valuePreFilter += Time.deltaTime * delta;
            if (valuePreFilter > steerInput)
                valuePreFilter = steerInput;
        }
        else
        {
            valuePreFilter -= Time.deltaTime * delta;
            if (valuePreFilter < steerInput)
                valuePreFilter = steerInput;
        }

        //filter value
        Value = Mathf.Pow(Mathf.Abs(valuePreFilter), filter) * Mathf.Sign(valuePreFilter);
    }

    private float CalcDiscreteDeltaOut(float speed)
    {
        return Lerp(DiscreteSteeringDeltaOutLo, DiscreteSteeringDeltaOutHi, CalcSpeedBlend(speed));
    }

    private float CalcDiscreteDeltaIn(float speed)
    {
        return Lerp(DiscreteSteeringDeltaInLo, DiscreteSteeringDeltaInHi, CalcSpeedBlend(speed));
    }

    private float CalcDiscreteFilter(float speed)
    {
        return Lerp(DiscreteSteeringFilterLo, DiscreteSteeringFilterHi, CalcSpeedBlend(speed));
    }
}

public class MMApproachInputFilter : MMInputFilterBase
{
    public bool UseApproachValues = false;
    public float ApproachOutLo = 2.5f;
    public float ApproachInLo = 1f;
    public float ApproachOutHi = 2.57f;
    public float ApproachInHi = 1f;
    public float AppApp = 0f;

    private float valuePreFilter = 0f;

    public override void Reset()
    {
        valuePreFilter = 0f;
    }

    public override void Update(float speed, float steerInput, float sensitivityMultiplier)
    {
        steerInput = Mathf.Clamp(steerInput, -1f, 1f);

        float sensitivity = CalcSensitivity(speed);
        float filter = CalcSteerFilter(speed);

        if (!UseApproachValues)
        {
            Value = Mathf.Pow(Mathf.Abs(steerInput) * sensitivity, filter) * (1f / sensitivity) * Mathf.Sign(steerInput);
            return;
        }
        else
        {
            //todo!!
            float approach = 0f;
            if (Mathf.Abs(steerInput) < valuePreFilter || Mathf.Sign(steerInput) != Mathf.Sign(valuePreFilter))
            {
                approach = CalcApproachIn(speed);
            }
            else
            {
                approach = CalcApproachOut(speed);
            }



        }
        Value = steerInput;
    }

    private float CalcApproachIn(float speed)
    {
        return Lerp(ApproachInLo, ApproachInHi, CalcSpeedBlend(speed));
    }

    private float CalcApproachOut(float speed)
    {
        return Lerp(ApproachOutLo, ApproachOutHi, CalcSpeedBlend(speed));
    }
}

public abstract class MMInputFilterBase 
{
    public enum SpeedMode
    {
        Low,
        High,
        Blend
    }

    public SpeedMode Mode = SpeedMode.Blend;

    public float SpeedBaseLow = 5f;
    public float SpeedBaseHi = 44.6f;

    public float SensitivityLow = 1f;
    public float SensitivityHigh = 1f;

    public float SteerFilterLow = 1f;
    public float SteerFilterHi = 1f;

    private float _value = 0f;
    public float Value
    {
        get
        {
            return _value;
        }
        protected set
        {
            _value = Mathf.Clamp(value, -1f, 1f);
        }
    }

    public abstract void Update(float speed, float steerInput, float sensitivityMultiplier);
    public virtual void Reset() { }

    public void CopySpeedVars(MMInputFilterBase copyFrom)
    {
        this.SpeedBaseLow = copyFrom.SpeedBaseLow;
        this.SpeedBaseHi = copyFrom.SpeedBaseHi;
        this.Mode = copyFrom.Mode;
    }

    protected float CalcSpeedBlend(float speed)
    {
        switch (Mode)
        {
            case SpeedMode.Low:
                return 0f;
            case SpeedMode.High:
                return 1f;
            case SpeedMode.Blend:
                if (speed < SpeedBaseLow)
                    return 0f;
                if (speed > SpeedBaseHi)
                    return 1f;
                return (speed - SpeedBaseLow) / (SpeedBaseHi - SpeedBaseLow);
        }
        return 0f;
    }

    protected float CalcSteerFilter(float speed)
    {
        return Lerp(SteerFilterLow, SteerFilterHi, CalcSpeedBlend(speed));
    }

    protected float CalcSensitivity(float speed)
    {
        return Lerp(SensitivityLow, SensitivityHigh, CalcSpeedBlend(speed));
    }

    //lerp with a couple of quick exits, optimized for when CalcSpeedBlend is 0 or 1
    protected float Lerp(float a, float b, float t)
    {
        if (t <= 0f)
            return a;
        if (t >= 1f)
            return b;
        return Mathf.LerpUnclamped(a, b, t);
    }
}
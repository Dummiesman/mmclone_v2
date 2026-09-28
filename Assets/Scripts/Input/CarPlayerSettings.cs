using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CarPlayerSettings
{
    public int SpeedSensitive = 2;
    public float SpeedBaseLow = 5f;
    public float SpeedBaseHi = 100f;
    public float MouseSensitivityLow = 0.6f;
    public float MouseSensitivityHi = 1.8f;
    public float MouseSteerFilterLow = 1.5f;
    public float MouseSteerFilterHi = 4f;
    public float JoySensitivityLow = 0.5f;
    public float JoySensitivityHi = 1.1f;
    public float JoySteerFilterLow = 1f;
    public float JoySteerFilterHi = 3f;
    public float DiscreteSteeringDeltaOutLo = 3.5f;
    public float DiscreteSteeringDeltaInLo = 2.5f;
    public float DiscreteSteeringFilterLo = 2f;
    public float DiscreteSteeringDeltaOutHi = 2.5f;
    public float DiscreteSteeringDeltaInHi = 1.5f;
    public float DiscreteSteeringFilterHi = 1f;
    public int JoyApp = 0;
    public float JoySteerApproachOutLo = 10f;
    public float JoySteerApproachInLo = 10f;
    public float JoySteerApproachOutHi = 2f;
    public float JoySteerApproachInHi = 4f;
    public float JoySteerAppApp = 0f;
    public float WheelSensitivityLow = 0.5f;
    public float WheelSteerFilterLow = 1f;
    public float WheelSensitivityHi = 1.1f;
    public float WheelSteerFilterHi = 3f;
    public int WheelApp = 0;
    public float WheelSteerApproachOutLo = 10f;
    public float WheelSteerApproachInLo = 10f;
    public float WheelSteerApproachOutHi = 2f;
    public float WheelSteerApproachInHi = 4f;
    public float WheelSteerAppApp = 0f;

    public CarPlayerSettings(string carBasename)
    {
        if (!AssetManager.Exists("tune", $"{carBasename}.asNode"))
            return;
        var parser = AssetManager.OpenNode("tune", $"{carBasename}.asNode");

        SpeedSensitive = parser.Read("SpeedSensitive", SpeedSensitive);
        SpeedBaseLow = parser.Read("SpeedBaseLow", SpeedBaseLow);
        MouseSensitivityLow = parser.Read("MouseSensitivityLow", MouseSensitivityLow);
        MouseSteerFilterLow = parser.Read("MouseSteerFilterLow", MouseSteerFilterLow);
        JoySensitivityLow = parser.Read("JoySensitivityLow", JoySensitivityLow);
        JoySteerFilterLow = parser.Read("JoySteerFilterLow", JoySteerFilterLow);
        SpeedBaseHi = parser.Read("SpeedBaseHi", SpeedBaseHi);
        MouseSensitivityHi = parser.Read("MouseSensitivityHi", MouseSensitivityHi);
        MouseSteerFilterHi = parser.Read("MouseSteerFilterHi", MouseSteerFilterHi);
        JoySensitivityHi = parser.Read("JoySensitivityHi", JoySensitivityHi);
        JoySteerFilterHi = parser.Read("JoySteerFilterHi", JoySteerFilterHi);
        DiscreteSteeringDeltaOutLo = parser.Read("DiscreteSteeringDeltaOutLo", DiscreteSteeringDeltaOutLo);
        DiscreteSteeringDeltaInLo = parser.Read("DiscreteSteeringDeltaInLo", DiscreteSteeringDeltaInLo);
        DiscreteSteeringFilterLo = parser.Read("DiscreteSteeringFilterLo", DiscreteSteeringFilterLo);
        DiscreteSteeringDeltaOutHi = parser.Read("DiscreteSteeringDeltaOutHi", DiscreteSteeringDeltaOutHi);
        DiscreteSteeringDeltaInHi = parser.Read("DiscreteSteeringDeltaInHi", DiscreteSteeringDeltaInHi);
        DiscreteSteeringFilterHi = parser.Read("DiscreteSteeringFilterHi", DiscreteSteeringFilterHi);
        JoyApp = parser.Read("JoyApp", JoyApp);
        JoySteerApproachOutLo = parser.Read("JoySteerApproachOutLo", JoySteerApproachOutLo);
        JoySteerApproachInLo = parser.Read("JoySteerApproachInLo", JoySteerApproachInLo);
        JoySteerApproachOutHi = parser.Read("JoySteerApproachOutHi", JoySteerApproachOutHi);
        JoySteerApproachInHi = parser.Read("JoySteerApproachInHi", JoySteerApproachInHi);
        JoySteerAppApp = parser.Read("JoySteerAppApp", JoySteerAppApp);
        WheelSensitivityLow = parser.Read("WheelSensitivityLow", WheelSensitivityLow);
        WheelSteerFilterLow = parser.Read("WheelSteerFilterLow", WheelSteerFilterLow);
        WheelSensitivityHi = parser.Read("WheelSensitivityHi", WheelSensitivityHi);
        WheelSteerFilterHi = parser.Read("WheelSteerFilterHi", WheelSteerFilterHi);
        WheelApp = parser.Read("WheelApp", WheelApp);
        WheelSteerApproachOutLo = parser.Read("WheelSteerApproachOutLo", WheelSteerApproachOutLo);
        WheelSteerApproachInLo = parser.Read("WheelSteerApproachInLo", WheelSteerApproachInLo);
        WheelSteerApproachOutHi = parser.Read("WheelSteerApproachOutHi", WheelSteerApproachOutHi);
        WheelSteerApproachInHi = parser.Read("WheelSteerApproachInHi", WheelSteerApproachInHi);
        WheelSteerAppApp = parser.Read("WheelSteerAppApp", WheelSteerAppApp);
    }
}

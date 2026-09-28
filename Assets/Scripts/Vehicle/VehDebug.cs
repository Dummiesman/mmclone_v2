using UnityEngine;

public class VehDebug : VehSubsystem
{
    public bool DrawCarDebug = true;
    public bool DrawSlipCurve = true;
    public bool DrawSubsystemToggles = true;

    [Header("Slip Curve Graph")]
    public float SlipCurveMaxSlip = 1.5f;
    public int SlipCurveSamples = 96;
    public float SlipCurveWidth = 340.0f;
    public float SlipCurveHeight = 220.0f;
    public bool SlipCurveShowVisual = true;

    // Cached so the toggles survive being switched off and back on.
    private bool togglesInitialised;

    private float savedGyroDrift;
    private float savedGyroSpin180;
    private float savedGyroReverse180;
    private float savedGyroPitch;
    private float savedGyroRoll;

    private float savedAxleFrontTorque;
    private float savedAxleFrontDamp;
    private float savedAxleRearTorque;
    private float savedAxleRearDamp;

    private float savedEngineAngInertia;

    private bool aeroOn = true;
    private bool gyroOn = true;
    private bool axleFrontOn = true;
    private bool axleRearOn = true;
    private bool engineReactionOn = true;

    private static readonly int[] SlipCurveWheelIndices = { 0, 2 };

    private static readonly Color[] SlipCurveColors =
    {
        new Color(0.35f, 0.75f, 1.00f),
        new Color(1.00f, 0.60f, 0.25f),
    };

    public override void Init(VehCar car)
    {
        base.Init(car);

        DrawCarDebug = Application.isEditor;
        DrawSlipCurve = Application.isEditor;
        DrawSubsystemToggles = Application.isEditor;
    }

    public override void Update()
    {
        if (!Application.isEditor)
            return;

        if (Input.GetKeyDown(KeyCode.Alpha0))
            DrawSlipCurve = !DrawSlipCurve;

        if (Input.GetKeyDown(KeyCode.Alpha9))
            DrawCarDebug = !DrawCarDebug;
    }

    private void OnGUI()
    {
        if (!Application.isEditor || Car == null || Car.VehCarSim == null)
            return;

        VehCarSim sim = Car.VehCarSim;

        if (DrawSubsystemToggles)
            DrawSubsystemTogglePanel(sim);

        if (DrawCarDebug)
            DrawCarInfo(sim);

        if (DrawSlipCurve)
            DrawWheelSlipCurves(sim);
    }

    private void DrawCarInfo(VehCarSim sim)
    {
        Color color = GUI.color;
        GUI.color = Color.white;

        float speed = sim.Body.velocity.magnitude * 3.6f;

        GUI.Label(new Rect(10, 10, 700, 20),
            $"Vehicle: {Car.Basename} | Damage: {Car.Damage.DamagePercentage}");

        GUI.Label(new Rect(10, 30, 700, 20),
            $"Speed: {speed:F1} km/h  |  RPM: {sim.Engine.CurrentRPM:F0}");

        GUI.Label(new Rect(10, 50, 700, 20),
            $"Gear: {sim.Transmission.CurrentGear}  |  " +
            $"Throttle: {sim.Engine.ThrottleInput:F2}  |  " +
            $"Brake: {sim.BrakeInput:F2}  |  " +
            $"Steer: {sim.SteeringInput:F2}");

        GUI.Label(new Rect(10, 70, 700, 20),
            $"Handbrake: {sim.HandBrakeInput:F2}  |  " +
            $"Drivetrain: {sim.DrivetrainType}");

        GUI.Label(new Rect(10, 90, 700, 20),
            $"Position: {Car.transform.position:F1}");

        GUI.Label(new Rect(10, 110, 700, 20),
            $"Inertia Tensor: {sim.Body.inertiaTensor:F2}");

        GUI.Label(new Rect(10, 130, 700, 20),
            $"Inertia Tensor Rotation: " +
            $"{sim.Body.inertiaTensorRotation.eulerAngles:F1}");

        for (int i = 0; i < 4; i++)
        {
            VehWheel wheel = sim.Wheels[i];
            float y = 165 + i * 85;

            GUI.Label(new Rect(10, y, 700, 20),
                $"Wheel {i}: " +
                $"Grounded={wheel.IsGrounded}  " +
                $"Slip={wheel.LastSlippage:F2}  " +
                $"MajorSlip={wheel.MajorlySlipping}");

            GUI.Label(new Rect(10, y + 20, 700, 20),
                $"  Suspension={wheel.CurrentSuspensionForce:F1} / " +
                $"{wheel.SuspensionMaxForce:F1}  " +
                $"Travel={wheel.TargetSuspensionTravel:F3}");

            GUI.Label(new Rect(10, y + 40, 700, 20),
                $"  Long={wheel.LongForce:F1}  " +
                $"Lat={wheel.LatForce:F1}  " +
                $"Brake={wheel.InputBrakeAmount:F1}");

            GUI.Label(new Rect(10, y + 60, 700, 20),
                $"  Center={wheel.Center:F2}  " +
                $"Radius={wheel.Radius:F3}");
        }

        GUI.color = color;
    }

    private void CacheSubsystemDefaults(VehCarSim sim)
    {
        if (togglesInitialised)
            return;

        savedGyroDrift = Car.Gyro.Drift;
        savedGyroSpin180 = Car.Gyro.Spin180;
        savedGyroReverse180 = Car.Gyro.Reverse180;
        savedGyroPitch = Car.Gyro.Pitch;
        savedGyroRoll = Car.Gyro.Roll;

        savedAxleFrontTorque = sim.AxleFront.TorqueCoef;
        savedAxleFrontDamp = sim.AxleFront.DampCoef;

        savedAxleRearTorque = sim.AxleRear.TorqueCoef;
        savedAxleRearDamp = sim.AxleRear.DampCoef;

        savedEngineAngInertia = sim.Engine.AngInertia;

        togglesInitialised = true;
    }

    private void DrawSubsystemTogglePanel(VehCarSim sim)
    {
        CacheSubsystemDefaults(sim);

        const float ButtonWidth = 108.0f;
        const float ButtonHeight = 22.0f;
        const float Gap = 4.0f;
        const int Count = 5;

        float totalWidth =
            Count * ButtonWidth + (Count - 1) * Gap;

        float x = (Screen.width - totalWidth) * 0.5f;
        float y = 8.0f;

        Color prev = GUI.color;

        GUI.color = new Color(0, 0, 0, 0.55f);

        GUI.DrawTexture(
            new Rect(
                x - 8,
                y - 4,
                totalWidth + 16,
                ButtonHeight + 8),
            Texture2D.whiteTexture);

        bool next;

        GUI.color = aeroOn
            ? Color.white
            : new Color(1.0f, 0.45f, 0.45f);

        next = GUI.Toggle(
            new Rect(x, y, ButtonWidth, ButtonHeight),
            aeroOn,
            " Aero",
            "Button");

        if (next != aeroOn)
        {
            aeroOn = next;
            sim.VehAero.EnableAero = aeroOn;
        }

        x += ButtonWidth + Gap;

        GUI.color = gyroOn
            ? Color.white
            : new Color(1.0f, 0.45f, 0.45f);

        next = GUI.Toggle(
            new Rect(x, y, ButtonWidth, ButtonHeight),
            gyroOn,
            " Gyro",
            "Button");

        if (next != gyroOn)
        {
            gyroOn = next;

            Car.Gyro.Drift =
                gyroOn ? savedGyroDrift : 0.0f;

            Car.Gyro.Spin180 =
                gyroOn ? savedGyroSpin180 : 0.0f;

            Car.Gyro.Reverse180 =
                gyroOn ? savedGyroReverse180 : 0.0f;

            Car.Gyro.Pitch =
                gyroOn ? savedGyroPitch : 0.0f;

            Car.Gyro.Roll =
                gyroOn ? savedGyroRoll : 0.0f;
        }

        x += ButtonWidth + Gap;

        GUI.color = axleFrontOn
            ? Color.white
            : new Color(1.0f, 0.45f, 0.45f);

        next = GUI.Toggle(
            new Rect(x, y, ButtonWidth, ButtonHeight),
            axleFrontOn,
            " ARB Front",
            "Button");

        if (next != axleFrontOn)
        {
            axleFrontOn = next;

            sim.AxleFront.TorqueCoef =
                axleFrontOn ? savedAxleFrontTorque : 0.0f;

            sim.AxleFront.DampCoef =
                axleFrontOn ? savedAxleFrontDamp : 0.0f;

            sim.AxleFront.ComputeConstants();
        }

        x += ButtonWidth + Gap;

        GUI.color = axleRearOn
            ? Color.white
            : new Color(1.0f, 0.45f, 0.45f);

        next = GUI.Toggle(
            new Rect(x, y, ButtonWidth, ButtonHeight),
            axleRearOn,
            " ARB Rear",
            "Button");

        if (next != axleRearOn)
        {
            axleRearOn = next;

            sim.AxleRear.TorqueCoef =
                axleRearOn ? savedAxleRearTorque : 0.0f;

            sim.AxleRear.DampCoef =
                axleRearOn ? savedAxleRearDamp : 0.0f;

            sim.AxleRear.ComputeConstants();
        }

        x += ButtonWidth + Gap;

        GUI.color = engineReactionOn
            ? Color.white
            : new Color(1.0f, 0.45f, 0.45f);

        next = GUI.Toggle(
            new Rect(x, y, ButtonWidth, ButtonHeight),
            engineReactionOn,
            " Eng React",
            "Button");

        if (next != engineReactionOn)
        {
            engineReactionOn = next;

            sim.Engine.AngInertia =
                engineReactionOn
                    ? savedEngineAngInertia
                    : 0.0f;
        }

        GUI.color = prev;
    }

    private void DrawWheelSlipCurves(VehCarSim sim)
    {
        if (sim.Wheels == null || sim.Wheels.Length < 4)
            return;

        if (Event.current.type != EventType.Repaint)
            return;

        float maxSlip = Mathf.Max(0.01f, SlipCurveMaxSlip);
        int samples = Mathf.Clamp(SlipCurveSamples, 2, 512);

        Rect graph = new Rect(
            Screen.width - SlipCurveWidth - 20.0f,
            40.0f,
            SlipCurveWidth,
            SlipCurveHeight);

        Color prevColor = GUI.color;

        GUI.color = new Color(0.0f, 0.0f, 0.0f, 0.55f);

        GUI.DrawTexture(
            new Rect(
                graph.x - 10,
                graph.y - 26,
                graph.width + 20,
                graph.height + 78),
            Texture2D.whiteTexture);

        float maxForce = 0.0001f;

        for (int c = 0; c < SlipCurveWheelIndices.Length; c++)
        {
            VehWheel wheel =
                sim.Wheels[SlipCurveWheelIndices[c]];

            for (int s = 0; s < samples; s++)
            {
                float slip =
                    (s / (float)(samples - 1)) * maxSlip;

                float visual;

                float force =
                    Mathf.Abs(
                        wheel.ComputeFriction(
                            slip,
                            out visual));

                if (force > maxForce)
                    maxForce = force;
            }
        }

        GUI.color =
            new Color(1.0f, 1.0f, 1.0f, 0.12f);

        for (int i = 1; i < 4; i++)
        {
            float t = i / 4.0f;

            float x =
                Mathf.Lerp(graph.x, graph.xMax, t);

            float y =
                Mathf.Lerp(graph.yMax, graph.y, t);

            UIDrawing.DrawLine(
                new Vector2(x, graph.y),
                new Vector2(x, graph.yMax));

            UIDrawing.DrawLine(
                new Vector2(graph.x, y),
                new Vector2(graph.xMax, y));
        }

        GUI.color =
            new Color(1.0f, 1.0f, 1.0f, 0.6f);

        UIDrawing.DrawLine(
            new Vector2(graph.x, graph.yMax),
            new Vector2(graph.xMax, graph.yMax));

        UIDrawing.DrawLine(
            new Vector2(graph.x, graph.y),
            new Vector2(graph.x, graph.yMax));

        for (int c = 0; c < SlipCurveWheelIndices.Length; c++)
        {
            VehWheel wheel =
                sim.Wheels[SlipCurveWheelIndices[c]];

            Color curveColor =
                SlipCurveColors[c % SlipCurveColors.Length];

            GUI.color = curveColor;

            Vector2 prevPoint = Vector2.zero;

            for (int s = 0; s < samples; s++)
            {
                float slip =
                    (s / (float)(samples - 1)) * maxSlip;

                float visual;

                float force =
                    wheel.ComputeFriction(
                        slip,
                        out visual);

                Vector2 point =
                    PlotPoint(
                        graph,
                        slip / maxSlip,
                        force / maxForce);

                if (s > 0)
                    UIDrawing.DrawLine(
                        prevPoint,
                        point);

                prevPoint = point;
            }

            if (SlipCurveShowVisual)
            {
                GUI.color = new Color(
                    curveColor.r,
                    curveColor.g,
                    curveColor.b,
                    0.35f);

                prevPoint = Vector2.zero;

                for (int s = 0; s < samples; s++)
                {
                    float slip =
                        (s / (float)(samples - 1)) * maxSlip;

                    float visual;

                    wheel.ComputeFriction(
                        slip,
                        out visual);

                    Vector2 point =
                        PlotPoint(
                            graph,
                            slip / maxSlip,
                            visual);

                    if (s > 0)
                        UIDrawing.DrawLine(
                            prevPoint,
                            point);

                    prevPoint = point;
                }
            }
        }

        for (int i = 0; i < 4; i++)
        {
            VehWheel wheel = sim.Wheels[i];

            float slip =
                Mathf.Clamp(
                    Mathf.Abs(wheel.LastSlippage),
                    0.0f,
                    maxSlip);

            float visual;

            float force =
                wheel.ComputeFriction(
                    slip,
                    out visual);

            Vector2 point =
                PlotPoint(
                    graph,
                    slip / maxSlip,
                    force / maxForce);

            GUI.color =
                !wheel.IsGrounded
                    ? new Color(0.5f, 0.5f, 0.5f, 0.7f)
                    : wheel.MajorlySlipping
                        ? new Color(1.0f, 0.3f, 0.3f, 0.95f)
                        : new Color(0.4f, 1.0f, 0.4f, 0.95f);

            UIDrawing.DrawLine(
                new Vector2(point.x, graph.yMax),
                point);

            UIDrawing.DrawLine(
                point + new Vector2(-5, -5),
                point + new Vector2(5, 5));

            UIDrawing.DrawLine(
                point + new Vector2(-5, 5),
                point + new Vector2(5, -5));

            GUI.Label(
                new Rect(
                    point.x + 6,
                    point.y - 18,
                    40,
                    18),
                $"{i}");
        }

        GUI.color = Color.white;

        GUI.Label(
            new Rect(
                graph.x,
                graph.y - 22,
                graph.width,
                20),
            "Wheel slip curve");

        GUI.Label(
            new Rect(
                graph.x + 4,
                graph.y + 2,
                200,
                20),
            $"{maxForce:F0}");

        GUI.Label(
            new Rect(
                graph.xMax - 60,
                graph.yMax + 2,
                60,
                20),
            $"{maxSlip:F2}");

        GUI.Label(
            new Rect(
                graph.x + 4,
                graph.yMax + 2,
                60,
                20),
            "0");

        for (int c = 0; c < SlipCurveWheelIndices.Length; c++)
        {
            int wi = SlipCurveWheelIndices[c];

            GUI.color =
                SlipCurveColors[c % SlipCurveColors.Length];

            GUI.Label(
                new Rect(
                    graph.x + 4 + c * 110,
                    graph.yMax + 22,
                    110,
                    20),
                $"Wheel {wi} curve");
        }

        GUI.color = Color.white;

        GUI.Label(
            new Rect(
                graph.x + 4,
                graph.yMax + 40,
                graph.width,
                20),
            $"Slip now: " +
            $"{sim.Wheels[0].LastSlippage:F2} / " +
            $"{sim.Wheels[1].LastSlippage:F2} / " +
            $"{sim.Wheels[2].LastSlippage:F2} / " +
            $"{sim.Wheels[3].LastSlippage:F2}");

        GUI.color = prevColor;
    }

    private static Vector2 PlotPoint(
        Rect graph,
        float tx,
        float ty)
    {
        return new Vector2(
            graph.x + Mathf.Clamp01(tx) * graph.width,
            graph.yMax -
                Mathf.Clamp01(ty) * graph.height);
    }
}

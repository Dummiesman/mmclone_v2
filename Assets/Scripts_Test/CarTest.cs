using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CarTest : MonoBehaviour
{
    public string VehName = "vpmustang99";
    private VehCar car;
    private new VehicleAudio audio;

    void Start()
    {
        FileSystem.Init();

        car = this.gameObject.AddComponent<VehCar>();
        car.Init(null, VehName, 0, vehCarType.Player);

        audio = this.gameObject.AddComponent<VehicleAudio>();
        audio.Init(VehName, car);

        for (int i = 0; i < 4; i++)
        {
            var wheel = car.VehCarSim.Wheels[i];

            Debug.Log(
                $"Wheel {i} " +
                $"Center=({wheel.Center.x:F3}, {wheel.Center.y:F3}, {wheel.Center.z:F3}) " +
                $"Radius={wheel.Radius:F3} " +
                $"NormalLoad={wheel.NormalLoad:F2} " +
                $"MaxForce={wheel.SuspensionMaxForce:F2} " +
                $"RestPos={wheel.SuspensionRestingPosition:F4}");
        }
    }

    private Camera camera;
    private void LateUpdate()
    {
        if(camera == null)
        {
            camera = FindObjectOfType<Camera>();

        }

        if (camera == null)
            return;

        Vector3 targetPosition =
            transform.position
            + transform.forward * 6.0f
            + Vector3.up * 3.0f;

        camera.transform.position = targetPosition;

        Vector3 lookTarget =
            transform.position
            - transform.forward * 1.0f
            + Vector3.up * 1.0f;

        camera.transform.LookAt(lookTarget);
    }

    private void OnGUI()
    {
        if (car == null || car.VehCarSim == null)
            return;

        var color = GUI.color;
        GUI.color = Color.white;
        VehCarSim sim = car.VehCarSim;

        float speed = sim.Body.velocity.magnitude * 3.6f;

        GUI.Label(new Rect(10, 10, 600, 20),
            $"Vehicle: {VehName}");

        GUI.Label(new Rect(10, 30, 600, 20),
            $"Speed: {speed:F1} km/h  |  RPM: {sim.Engine.CurrentRPM:F0}");

        GUI.Label(new Rect(10, 50, 600, 20),
            $"Gear: {sim.Transmission.CurrentGear}  |  " +
            $"Throttle: {sim.Engine.ThrottleInput:F2}  |  " +
            $"Brake: {sim.BrakeInput:F2}  |  " +
            $"Steer: {sim.SteeringInput:F2}");

        GUI.Label(new Rect(10, 70, 600, 20),
            $"Handbrake: {sim.HandBrakeInput:F2}  |  " +
            $"Drivetrain: {sim.DrivetrainType}");

        GUI.Label(new Rect(10, 90, 600, 20),
            $"Position: {transform.position:F1}");

        for (int i = 0; i < 4; i++)
        {
            VehWheel wheel = sim.Wheels[i];

            float y = 125 + i * 85;

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
                $"  Center={wheel.Center:F2}  Radius={wheel.Radius:F3}");
        }

        GUI.color = color;
    }

    private void Update()
    {
        float throttleInput = 0.0f;
        float brakeInput = 0.0f;
        if (Input.GetKey(KeyCode.UpArrow)) throttleInput = 1.0f;
        if (Input.GetKey(KeyCode.DownArrow)) brakeInput = 1.0f;

        float steer = 0.0f;
        if (Input.GetKey(KeyCode.LeftArrow)) steer = -1.0f;
        else if (Input.GetKey(KeyCode.RightArrow)) steer = 1.0f;

        float handbrakeInput = 0.0f;
        if (Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.B)) handbrakeInput = 1.0f;
        car.VehCarSim.SetInputs(-steer, throttleInput, brakeInput, handbrakeInput);

        if (Input.GetKeyDown(KeyCode.T))
        {
            var curMode = car.VehCarSim.Transmission.Mode;
            var nextMode = (curMode == VehTransmission.TransmissionMode.Manual) ? VehTransmission.TransmissionMode.Automatic :
                                                                                  VehTransmission.TransmissionMode.Manual;
            car.VehCarSim.Transmission.Mode = nextMode;
        }

        if (Input.GetKeyDown(KeyCode.A))
        {
            car.VehCarSim.Transmission.Upshift();
        }

        if (Input.GetKeyDown(KeyCode.Z))
        {
            car.VehCarSim.Transmission.Downshift();
        }
    }
}

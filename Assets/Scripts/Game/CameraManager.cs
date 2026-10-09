using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityStandardAssets.Utility;
using MM2.Camera;
using System;

public class VehicleCameraManager : MonoBehaviour
{
    public Camera CurrentCamera => cameras[selectedCamera];
    public OrbitCamera OrbitCamera => cameras[3].gameObject.GetComponent<OrbitCamera>();

    private ViewportManager.Viewport viewport;

    private int selectedCamera = 0;
    private bool dashActive = false;
    private bool orbitCamActive = false;

    private Camera dashCam;
    private Vector3 dashCamOffset;

    private Camera bumperCam;
    private Vector3 bumperCamOffset;

    /// <summary>
    /// 0 = near, 1 = far, 2 = bumper, 3 = orbit
    /// </summary>
    private Camera[] cameras = new Camera[4];

    /// <summary>
    /// Track cameras by camera index, so they can be enabled/disabled on switch.
    /// </summary>
    private camTrackCS[] trackCams = new camTrackCS[4];

    private VehCar vehicle;

    private Camera InitPovCam()
    {
        string cameraFile = $"{vehicle.Basename}.camPovCS";

        string filePath = AssetManager.CombinePath("tune", "camera", cameraFile);
        if (!AssetManager.Exists(filePath))
        {
            var cam1 = this.viewport.AddCamera();
            cam1.transform.parent = vehicle.transform;
            return cam1;
        }

        var camNode = AssetManager.OpenNode(filePath);

        //
        var cam = this.viewport.AddCamera();
        cam.transform.parent = this.transform;
        cam.gameObject.name = cameraFile;
        cam.transform.localEulerAngles = new Vector3(0, 180, 0);


        var offset = camNode.Read("Offset", Vector3.zero);
        float fov = camNode.Read("CameraFOV", 70f);
        cam.transform.position = offset;
        cam.fieldOfView = fov;
        bumperCamOffset = offset;
        bumperCamOffset.z *= -1.0f;
        bumperCamOffset.x *= -1.0f;

        //
        return cam;
    }

    private Camera InitTrackCam(string name)
    {
        return null;
    }

    private void InitDashCam(VehCar vehicle)
    {
        dashCam = ViewportManager.MainViewport.AddCamera();
        dashCam.gameObject.name = "DashCamera";
        dashCam.transform.parent = transform;
        dashCam.transform.localPosition = Vector3.zero;
        dashCam.transform.localEulerAngles = new Vector3(0, 180, 0);
        dashCam.fieldOfView = 70;

        string vehicleBasename = vehicle.Basename;
        string camFile = $"{vehicleBasename}_dash.camPovCS";

        if (!AssetManager.Exists("tune", "camera", camFile))
            return;

        var camReader = AssetManager.OpenNode("tune", $"camera/{camFile}");
        dashCamOffset = camReader.Read("Offset", Vector3.zero).ConvertCoordinateSpace();
        dashCamOffset.z *= -1.0f;
        dashCamOffset.x *= -1.0f;
    }

    private Camera InitCamera(int index, string name)
    {
        bool isTrackCam = !string.IsNullOrEmpty(name);

        string cameraFile = $"{vehicle.Basename}.camPovCS";
        if (isTrackCam)
            cameraFile = $"{vehicle.Basename}_{name}.camTrackCS";

        string filePath = AssetManager.CombinePath("tune", "camera", cameraFile);
        if (!AssetManager.Exists(filePath))
        {
            var cam1 = this.viewport.AddCamera();
            cam1.transform.parent = vehicle.transform;
            return cam1;
        }

        var camNode = AssetManager.OpenNode(filePath);

        //

        //
        var cam = this.viewport.AddCamera();
        cam.transform.parent = this.transform;
        cam.gameObject.name = cameraFile;
        cam.transform.localEulerAngles = new Vector3(0, 180, 0);

        if (isTrackCam)
        {
            var track = cam.gameObject.AddComponent<camTrackCS>();
            track.Init(cam);
            track.ReadSettings(camNode);
            track.SetCar(vehicle);
            track.ResetCamera();

            trackCams[index] = track;
        }
        else
        {
            var offset = camNode.Read("Offset", Vector3.zero);
            float fov = camNode.Read("CameraFOV", 70f);
            cam.transform.position = offset;
            cam.fieldOfView = fov;
        }

        //
        return cam;
    }

    public void Init(VehCar vehicle, ViewportManager.Viewport viewport)
    {
        this.vehicle = vehicle;
        this.viewport = viewport;

        cameras[2] = InitPovCam();
        cameras[1] = InitCamera(1, "NEAR");
        cameras[0] = InitCamera(0, "FAR");


        cameras[3] = viewport.AddCamera();
        cameras[3].gameObject.AddComponent<OrbitCamera>().Target = vehicle.transform;

        bumperCam = cameras[2];
        InitDashCam(vehicle);
    }

    public void SetCamIndex(int index)
    {
        int maxCam = cameras.Length - 1;
        index = index % maxCam;
        selectedCamera = index;
    }

    public void ActivateCurrentCamera()
    {
        viewport.SetActiveCamera(CurrentCamera);
    }

    public void TransitionToCurrentCamera()
    {
        viewport.TransitionToCamera(CurrentCamera, 1f);
    }

    public void DeactivateDash()
    {
        TransitionToCurrentCamera();
        dashActive = false;
    }

    public void ActivateDash()
    {
        dashActive = true;
        ViewportManager.MainViewport.SetActiveCamera(dashCam);
    }

    public void ToggleDashboard()
    {
        //orbit cam takes precedence
        if (orbitCamActive)
            return;

        if (!dashActive)
        {
            ActivateDash();
        }
        else
        {
            DeactivateDash();
        }
    }

    public void NextCamera()
    {
        // decativate special cams and only increment if not in one
        if (dashActive)
        {
            DeactivateDash();
        }
        else if (orbitCamActive)
        {
            orbitCamActive = false;
        }
        else
        {
            selectedCamera++;
            if (selectedCamera > 2)
                selectedCamera = 0;
        }

        TransitionToCurrentCamera();
    }

    public void ActivateOrbitCamera()
    {
        //deactivate dash if active
        if (dashActive) DeactivateDash();

        //
        orbitCamActive = !orbitCamActive;
        if (orbitCamActive)
        {
            viewport.TransitionToCamera(cameras[3], 1f);
        }
        else
        {
            if (dashActive)
                ActivateDash();
            else
                TransitionToCurrentCamera();
        }
    }

    private void LateUpdate()
    {
        // force fixed cameras into position
        dashCam.transform.SetPositionAndRotation(vehicle.transform.TransformPoint(dashCamOffset), vehicle.transform.rotation);
        bumperCam.transform.SetPositionAndRotation(vehicle.transform.TransformPoint(bumperCamOffset), vehicle.transform.rotation);
    }

    public void Destroy()
    {
        foreach (var camera in cameras)
            viewport.RemoveCamera(camera);
        Destroy(this);
    }
}
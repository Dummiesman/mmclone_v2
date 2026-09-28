using System;
using UnityEngine;

public class CameraTransition : MonoBehaviour
{
	public Action OnCompletedCb;
	public AnimationCurve LerpCurve;
	public float Progress => currentTime / endTime;

	public Camera FromCamera { get; private set; }
	public Camera ToCamera { get; private set; }
	public Camera Camera { get; private set; }

	private float currentTime = 0;
	private float endTime = 1f;

	private bool staticOriginalPosition = false;
	private Vector3 originalPosition;
	private Quaternion originalRotation;

	public void Init(Camera from, Camera to, float time)
	{
		FromCamera = from;
		ToCamera = to;
		Camera = GetComponent<Camera>();
		endTime = time;
		staticOriginalPosition = false;
	}

	public void Init(Camera settingsCamera, Camera to, Vector3 startPosition, Quaternion startRotation, float time)
	{
		FromCamera = settingsCamera;
		ToCamera = to;
		Camera = GetComponent<Camera>();
		endTime = time;

		staticOriginalPosition = true;
		originalPosition = startPosition;
		originalRotation = startRotation;
	}

	void LateUpdate()
	{
		float progress = currentTime / endTime;

		if (progress > float.Epsilon)
		{
			float lerpAmount = LerpCurve?.Evaluate(progress) ?? progress;

			//lerp properties
			Camera.backgroundColor = Color.Lerp(FromCamera.backgroundColor, ToCamera.backgroundColor, lerpAmount);
			Camera.fieldOfView = Mathf.Lerp(FromCamera.fieldOfView, ToCamera.fieldOfView, lerpAmount);

			if (staticOriginalPosition)
			{
				Camera.transform.position = Vector3.Lerp(originalPosition, ToCamera.transform.position, lerpAmount);
				Camera.transform.rotation = Quaternion.Lerp(originalRotation, ToCamera.transform.rotation, lerpAmount);
			}
			else
			{
				Camera.transform.position = Vector3.Lerp(FromCamera.transform.position, ToCamera.transform.position, lerpAmount);
				Camera.transform.rotation = Quaternion.Lerp(FromCamera.transform.rotation, ToCamera.transform.rotation, lerpAmount);
			}
		}

		//done?
		currentTime += Time.unscaledDeltaTime;
		if (currentTime >= endTime)
		{
			OnCompletedCb?.Invoke();
			this.enabled = false;
		}
	}
}

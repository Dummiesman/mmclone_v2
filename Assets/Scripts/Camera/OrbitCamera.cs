using UnityEngine;

public class OrbitCamera : MonoBehaviour
{
    public Transform Target;
    public float MinDistance = 2f;
    public float MaxDistance = 50f;
    public float MinAngle = -5.0f;
    public float MaxAngle = 89.9f;
    public float InitialDistance = 5f;
    public Vector3 Offset = new Vector3(0f, 1.5f, 0f);
    public float FieldOfView = 60f;

    public bool EnableRaycasting = true;
    public bool EnableMouseControl = true;

    private float currentDistance = 0f;
    private float currentRotation = 0f;
    private float currentHeightRotation = 0f;

    private void Start()
    {
        GetComponent<Camera>().fieldOfView = FieldOfView;
        currentDistance = InitialDistance;
    }

    private void UpdateMouseInput()
    {
        // Mouse wheel
        float scrollDegrees = Input.mouseScrollDelta.y;
        currentDistance += -scrollDegrees / 360f;

        // Right button triggers mouse movement
        if (!Input.GetMouseButton(1))
            return;

        float mouseDeltaX = Input.GetAxisRaw("Mouse X");
        float mouseDeltaY = Input.GetAxisRaw("Mouse Y");

        currentRotation += (mouseDeltaX / Screen.width) * 180f;
        currentHeightRotation += (-mouseDeltaY / Screen.height) * 180f;
    }

    private void UpdateKeyboardInput()
    {
        float inputSpeed = 2f;

        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            inputSpeed = 10f;

        if (Input.GetKey(KeyCode.Home))
        {
            currentHeightRotation += Time.unscaledDeltaTime * inputSpeed * 10f;
        }

        if (Input.GetKey(KeyCode.End))
        {
            currentHeightRotation -= Time.unscaledDeltaTime * inputSpeed * 10f;
        }

        if (Input.GetKey(KeyCode.Delete))
        {
            currentRotation += Time.unscaledDeltaTime * inputSpeed * 10f;
        }

        if (Input.GetKey(KeyCode.PageDown))
        {
            currentRotation -= Time.unscaledDeltaTime * inputSpeed * 10f;
        }

        if (Input.GetKey(KeyCode.Insert))
        {
            currentDistance += Time.unscaledDeltaTime * inputSpeed;
        }

        if (Input.GetKey(KeyCode.PageUp))
        {
            currentDistance -= Time.unscaledDeltaTime * inputSpeed;
        }

        // Debug
        if (Input.GetKey(KeyCode.LeftShift) &&
            Input.GetKeyUp(KeyCode.V))
        {
            EnableRaycasting = !EnableRaycasting;
        }
    }

    private void Update()
    {
        if (Target == null)
            return;

        // Update input
        if (EnableMouseControl)
            UpdateMouseInput();

        UpdateKeyboardInput();

        // Clamp
        currentDistance = Mathf.Clamp(
            currentDistance,
            MinDistance,
            MaxDistance
        );

        currentHeightRotation = Mathf.Clamp(
            currentHeightRotation,
            MinAngle,
            MaxAngle
        );

        // Set position and rotation
        transform.position = new Vector3(
            Target.transform.position.x + Offset.x,
            Target.transform.position.y + Offset.y,
            Target.transform.position.z + Offset.z - currentDistance
        );

        transform.localRotation = Quaternion.identity;

        transform.RotateAround(
            Target.transform.position,
            Vector3.right,
            currentHeightRotation
        );

        transform.RotateAround(
            Target.transform.position,
            Vector3.up,
            currentRotation
        );

        // Raycast if requested
        if (!EnableRaycasting)
            return;

        bool blocked = Physics.Linecast(
            Target.transform.position,
            transform.position,
            out RaycastHit hit,
            LayerMask.GetMask("Default")
        );

        if (blocked && hit.distance >= MinDistance)
        {
            Vector3 linecastDirection =
                (transform.position - Target.transform.position).normalized;

            transform.position =
                hit.point - (linecastDirection * 0.1f);
        }
    }
}
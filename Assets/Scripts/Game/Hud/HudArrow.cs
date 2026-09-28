using UnityEngine;

public class HudArrow : MonoBehaviour
{
    public Vector3 Target;
    public bool AllowColorChanges = true;
    public Vector3 TargetOffset = -Vector3.up;
    private MeshRenderer Renderer;

    public float Angle { get; private set; }

    public void Init(string arrowModel = "hudarrow01")
    {
        var form = SimpleForm.Create(arrowModel, Shader.Find("Unlit/Color Overlay"), this.transform);
        Renderer = this.gameObject.GetComponentInChildren<MeshRenderer>();
    }

    int lastColorId = -1;
    private void SetColor(int id)
    {
        if (id == lastColorId)
            return;

        Color color = (id == 1) ? Color.yellow : Color.green;
        foreach (var mat in Renderer.sharedMaterials)
            mat.color = color;
        lastColorId = id;
    }

	
	void LateUpdate ()
	{
	    var curCam = ViewportManager.MainViewport.ActiveCamera;
	    this.transform.position = curCam.ScreenToWorldPoint(new Vector3(curCam.pixelWidth * 0.5f, curCam.pixelHeight * 0.85f, 6f));

        //do stuff with the target
	    if (Target == null)
	        return;

        float forwardTargetDot = 1f - Vector3.Dot((Target - this.transform.position).Flatten().normalized, this.transform.forward.Flatten());
        var upBlend = Vector3.Lerp(Vector3.up, (this.transform.position - curCam.transform.position).normalized, forwardTargetDot * 0.2f);
        var direction = (this.transform.position - (Target + TargetOffset)).normalized;
        transform.rotation = Quaternion.LookRotation(direction, upBlend);

        Angle = Vector3.SignedAngle(-transform.forward.Flatten(), curCam.transform.forward.Flatten(), Vector3.up);
        if (AllowColorChanges)
        {
            SetColor(Mathf.Abs(Angle) >= 45f ? 1 : 0);
        }
	}

    private void OnEnable()
    {
        if (Renderer != null) Renderer.forceRenderingOff = false;
    }
    private void OnDisable()
    {
        if (Renderer != null) Renderer.forceRenderingOff = true;
    }
}

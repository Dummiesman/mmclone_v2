using UnityEngine;

[DefaultExecutionOrder(100)]
public class Skydome : MonoBehaviour
{
    private GameObject skyObject;

    public Transform FollowTransform;
    public float RotationSpeed = 0.005f;
    public float YOffset = 0;
    public float YMultiplier = 1.0f;

    public void Init(string city, MMTimeOfDay tod, MMWeather weather)
    {
        //check if sky settings exists
        if (!AssetManager.Exists("city", $"{city}.sky"))
        {
            Debug.LogWarning("Cannot init skydome because the .sky file is missing for this city.");
            return;
        }

        //parse sky settings
        string[] skySettings = AssetManager.ReadAllText("city", $"{city}.sky").Clean().Split(' ');
        if (skySettings.Length < 4)
        {
            Debug.LogWarning("Cannot parse .sky file, it is malformed.");
            return;
        }

        string skyModelName = skySettings[0];
        float yOffset = FastFloatParser.Parse(skySettings[1]);
        float yMultiplier = FastFloatParser.Parse(skySettings[2]);
        float multiplier = FastFloatParser.Parse(skySettings[3]);

        //init model
        int shaderID = (((int)tod * 4) + (int)weather);
        skyObject = SimpleForm.Create(skyModelName, Shader.Find("Custom/RenderBehindUnlit"), this.transform, shaderID).gameObject;
        if (skyObject == null)
        {
            Debug.LogWarning("Sky model wasn't found or failed ot load.");
            return;
        }
        skyObject.SetLayer(LayerMask.NameToLayer("Sky"), true);

        //set params
        this.RotationSpeed = multiplier * Mathf.Rad2Deg;
        this.YMultiplier = yMultiplier;
        this.YOffset = yOffset;
    }

    public void Reset()
    {
        //reset rotation
        this.gameObject.transform.rotation = Quaternion.identity;
    }

    // Update is called once per frame
    void LateUpdate () 
    {
        if (ViewportManager.MainViewport == null || ViewportManager.MainViewport.ActiveCamera == null)
            return;

        FollowTransform = ViewportManager.MainViewport.ActiveCamera.transform;
        if (FollowTransform == null)
            return;

        transform.position = new Vector3(FollowTransform.position.x, YOffset + (FollowTransform.position.y * YMultiplier), FollowTransform.position.z);
        transform.Rotate(0, -RotationSpeed * Time.deltaTime, 0);
	}
}
    
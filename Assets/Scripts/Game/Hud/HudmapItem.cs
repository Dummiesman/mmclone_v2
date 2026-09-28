using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HudmapItem : MonoBehaviour
{
    public enum ComponentType
    {
        Square,
        Triangle
    }

    /// <summary>
    /// Automatically follow this.Transform.position
    /// </summary>
    public bool FollowTransformPosition = true;

    /// <summary>
    /// Automatically follow this.Transform.rotation
    /// </summary>
    public bool FollowTransformRotation = true;

    /// <summary>
    /// Whether we only want to follow 2D rotations (only Y axis)
    /// </summary>
    public bool TwoDimensionalRotation = false;

    /// <summary>
    /// Automatically follow this.Transform.scale
    /// </summary>
    public bool FollowTransformScale = false;

    /// <summary>
    /// Destroy this object if the target becomes null
    /// </summary>
    public bool DestroyIfTargetBecomesNull = true;

    /// <summary>
    /// Offset
    /// </summary>
    public Vector3 Offset;

    /// <summary>
    /// The object to follow if any of the follow parameters are set
    /// </summary>
    public Transform Transform;

    //scaling
    private Vector3 _baseScale = Vector3.one;
    private float _lastScaleFactor = 1f;

    /// <summary>
    /// The base scale of this hudmap item. This is used when calling Scale(factor).
    /// </summary>
    public Vector3 BaseScale
    {
        get
        {
            return _baseScale;
        }
        set
        {
            _baseScale = value;
            this.Scale(_lastScaleFactor);
        }
    }


    //
    private Material componentMaterial;
    private Material componentBorderMaterial;

    private Renderer[] componentRenderers;
    private Renderer[] componentBorderRenderers;

    private GameObject componentObject;
    private GameObject componentBorderObject;

    private void SetupMaterials()
    {
        var componentShader = Shader.Find("Unlit/Texture Transparent Colored"); 
        componentRenderers = componentObject.GetComponentsInChildren<MeshRenderer>();
        foreach (var mr in componentRenderers)
        {
            foreach (var mt in mr.materials)
            {
                if (componentMaterial == null)
                    componentMaterial = mt;
                mt.shader = componentShader; 
            }
        }

        if (componentBorderObject != null)
        {
            var borderShader = Shader.Find("Unlit/Color");
            componentBorderRenderers = componentBorderObject.GetComponentsInChildren<MeshRenderer>();
            foreach (var mr in componentBorderRenderers)
            {
                foreach (var mt in mr.materials)
                {
                    if (componentBorderMaterial == null)
                        componentBorderMaterial = mt;
                    mt.shader = borderShader; 
                }
            }
        }
    }
    /// <summary>
    /// 
    /// </summary>
    /// <param name="type"></param>
    /// <param name="hasBorder">Determines if this component has a border</param>
    /// <param name="texture">Component texture, can be null</param>
    /// <param name="color">Color of component</param>
    /// <param name="borderColor">Color of component border</param>
    public void Init(Transform target, ComponentType type, bool hasBorder)
    {
        //create child model
        string modelName = (type == ComponentType.Square) ? "hudmap_square" : "hudmap_tri";
        componentObject = SimpleForm.Create(modelName, Shader.Find("Unlit/Color")).gameObject;
        if (componentObject == null)
        {
            Debug.LogError($"HudmapItem : Cannot find {modelName} model. Item will not be visible");
            return;
        }

        componentObject.transform.parent = this.transform;
        componentObject.transform.localPosition = new Vector3(0, 4, 0); //raises the object for shadowing support
        componentObject.transform.localRotation = Quaternion.Euler(0, 180, 0); //orient correctly
        componentObject.transform.localScale = Vector3.one;

        //create shadow
        if (hasBorder)
        {
            componentBorderObject = SimpleForm.Create(modelName, Shader.Find("Unlit/Color")).gameObject;
            componentBorderObject.transform.parent = this.transform;
            componentBorderObject.transform.localPosition = new Vector3(0, 2, 0);
            componentBorderObject.transform.localScale = new Vector3(1.3f, 1.3f, 1.3f);
            componentBorderObject.transform.localRotation = Quaternion.Euler(0, 180, 0);
        }

        //set layer and target
        this.gameObject.SetLayer(LayerMask.NameToLayer("Hudmap"), true);
        Transform = target;

        //remove all lod stuffs
        foreach (var lodgroup in this.transform.GetComponentsInChildren<LODGroup>())
        {
            Destroy(lodgroup);
        }

        //finally, get materials
        SetupMaterials();
    }
    
    public void Init()
    {
        Init(null);
    }

    public void Init(Transform target)
    {
        Init(target, ComponentType.Triangle);
    }

    public void Init(Transform target, ComponentType componentType)
    {
        Init(target, componentType, false);
    }

    public virtual void Update()
    {
        if(DestroyIfTargetBecomesNull && Transform == null)
        {
            Debug.Log($"Destrying hudmapitem {GetInstanceID()} because it's target has become null.");
            Destroy(this.gameObject);
            return;
        }

        if(Transform != null)
        {
            if (FollowTransformPosition)
                this.transform.position = Transform.position + Offset;
            if (FollowTransformRotation)
            {
                if (TwoDimensionalRotation)
                {
                    this.transform.rotation = Quaternion.Euler(0f, Transform.eulerAngles.y, 0f);
                }
                else
                {
                    this.transform.rotation = Transform.rotation;
                }
            }
            if (FollowTransformScale)
                this.transform.localScale = Transform.localScale;
        }
    }

    public void SetBorderColor(Color color)
    {
        if (componentBorderMaterial != null)
            componentBorderMaterial.color = color;
    }

    public void SetColor(Color color)
    {
        if (componentMaterial != null && componentMaterial.HasProperty("_Color"))
            componentMaterial.color = color;
    }

    public void SetTexture(string texture)
    {
        if (!string.IsNullOrWhiteSpace(texture) && !string.IsNullOrEmpty(texture))
        {
            SetTexture(TextureCache.Get(texture));
        }
        else
        {
            SetTexture((Texture2D)null);
        }
    }

    public void SetTexture(Texture2D texture)
    {
        if (componentMaterial != null && componentMaterial.HasProperty("_MainTex"))
            componentMaterial.mainTexture = texture;
    }

    public void SetVisible(bool visible)
    {
        foreach(var renderer in componentRenderers)
        {
            renderer.enabled = visible;
        }
        if(componentBorderObject != null)
        {
            foreach (var renderer in componentBorderRenderers)
            {
                renderer.enabled = visible;
            }
        }
    }

    public void Scale(float factor)
    {
        _lastScaleFactor = factor;
        this.transform.localScale = _baseScale * factor;
    }

    private void OnDestroy()
    {
    }
}

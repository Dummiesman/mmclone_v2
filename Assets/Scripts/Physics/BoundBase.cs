using UnityEngine;

public abstract class BoundBase : MonoBehaviour
{ 
    public abstract LevelPhysMaterial GetMaterial(int triangleIndex);
    public abstract bool HasPhysicsMaterial(LevelPhysMaterial material);
}

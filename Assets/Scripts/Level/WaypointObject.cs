using UnityEngine;

public class WaypointObject : MonoBehaviour
{
    public bool Active => active;

    private WaypointInstance instance;
    private bool active = true;

    private float heading;
    private float radius;
    private float height;
    private Vector2 leftGatePt;   // gate endpoints on the XZ plane (world space)
    private Vector2 rightGatePt;

    public float Heading => heading;
    public float Radius => radius;
    public Vector2 LeftGatePt => leftGatePt;
    public Vector2 RightGatePt => rightGatePt;

    public void Deactivate()
    {
        active = false;
        instance.gameObject.SetActive(false);
    }

    public void Activate()
    {
        active = true;
        instance.gameObject.SetActive(true);
    }

    public void SetRadius(float radius)
    {
        transform.localScale = new Vector3(radius, height, height);
        this.radius = radius;
    }

    public void SetPosition(Vector3 position)
    {
        transform.position = position + (Vector3.up * (height / 2.0f));
    }

    public void SetOrientation(float orientation)
    {
        this.heading = orientation;
        transform.localRotation = Quaternion.Euler(0.0f, orientation, 0.0f);
    }
    
    public void Move()
    {
        UpdateGatePoints();
    }

    public void Init(SDLCity level, string model, Vector3 position, float orientation, float radius, float height)
    {
        this.height = height;

        var instanceObj = new GameObject("WaypointInstance");
        instance = instanceObj.AddComponent<WaypointInstance>();
        instance.Init(level, model, Vector3.zero, Quaternion.identity, Vector3.one);
        instanceObj.transform.SetParent(this.transform, false);

        SetPosition(position);
        SetRadius(radius);
        SetOrientation(orientation);
        Move();
    }

    /// <summary>
    /// The gate is a line across the waypoint, 'radius' out to each side along the local X axis
    /// (the same axis the model is scaled along). Call again if you move/rotate the waypoint.
    /// </summary>
    public void UpdateGatePoints()
    {
        Vector3 p = transform.position;
        Vector3 side = transform.right * radius; // transform.right is unit length, unaffected by scale

        leftGatePt = new Vector2(p.x - side.x, p.z - side.z);
        rightGatePt = new Vector2(p.x + side.x, p.z + side.z);
    }

    public bool RadiusHit(Vector3 point)
    {
        Vector3 d = point - transform.position;
        return radius * radius > d.sqrMagnitude;
    }

    // vehicle    : the vehicle's transform
    // linePtA/B  : vehicle position on XZ this frame / last frame
    // iboxh      : vehicle bounding box half-extents
    public bool PlaneHit(Transform vehicle, Vector2 linePtA, Vector2 linePtB, Vector3 iboxh)
    {
        // 1) Movement path of the car vs the gate, padded by half the car width
        if (LineIntersect(linePtB, linePtA, rightGatePt, leftGatePt, iboxh.x))
            return true;

        // 2) Line through the car along its local Y axis (faithful to the original)
        Vector3 a = vehicle.TransformPoint(new Vector3(0f, -iboxh.y, 0f));
        Vector3 b = vehicle.TransformPoint(new Vector3(0f, iboxh.y, 0f));
        if (LineIntersect(a.ToVec2XZ(), b.ToVec2XZ(), rightGatePt, leftGatePt, iboxh.x))
            return true;

        // 3) Line across the car's width (local X axis), no padding
        a = vehicle.TransformPoint(new Vector3(-iboxh.x, 0f, 0f));
        b = vehicle.TransformPoint(new Vector3(iboxh.x, 0f, 0f));
        return LineIntersect(a.ToVec2XZ(), b.ToVec2XZ(), rightGatePt, leftGatePt, 0f);
    }

    // mmWaypointObject::LineIntersect
    // Intersects the two infinite lines (slope/intercept form), then checks that the
    // intersection lies inside both segments' bounding boxes, expanded by 'tolerance'.
    public static bool LineIntersect(Vector2 a0, Vector2 a1, Vector2 b0, Vector2 b1, float tolerance)
    {
        float minX1 = Mathf.Min(a0.x, a1.x) - tolerance;
        float minY1 = Mathf.Min(a0.y, a1.y) - tolerance;
        float maxX1 = Mathf.Max(a0.x, a1.x) + tolerance;
        float maxY1 = Mathf.Max(a0.y, a1.y) + tolerance;

        float minX2 = Mathf.Min(b0.x, b1.x) - tolerance;
        float minY2 = Mathf.Min(b0.y, b1.y) - tolerance;
        float maxX2 = Mathf.Max(b0.x, b1.x) + tolerance;
        float maxY2 = Mathf.Max(b0.y, b1.y) + tolerance;

        float dx1 = a0.x - a1.x;
        float dx2 = b0.x - b1.x;

        float slope1 = dx1 == 0f ? 0f : (a0.y - a1.y) / dx1;
        float slope2 = dx2 == 0f ? 0f : (b0.y - b1.y) / dx2;

        float intercept1 = a0.y - slope1 * a0.x;
        float intercept2 = b0.y - slope2 * b0.x;

        float ix, iy;
        if (dx1 == 0f)
        {
            // first line is vertical
            ix = a0.x;
            iy = slope2 * a0.x + intercept2;
        }
        else if (dx2 == 0f)
        {
            // second line is vertical
            ix = b0.x;
            iy = slope1 * b0.x + intercept1;
        }
        else
        {
            // parallel lines divide by zero -> Infinity/NaN -> all comparisons fail (same as original)
            ix = (intercept2 - intercept1) / (slope1 - slope2);
            iy = ix * slope1 + intercept1;
        }

        return ix >= minX1 && ix <= maxX1 && iy >= minY1 && iy <= maxY1
            && ix >= minX2 && ix <= maxX2 && iy >= minY2 && iy <= maxY2;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;
        float y = transform.position.y;
        Gizmos.DrawLine(new Vector3(leftGatePt.x, y, leftGatePt.y),
                        new Vector3(rightGatePt.x, y, rightGatePt.y));
    }
#endif
}
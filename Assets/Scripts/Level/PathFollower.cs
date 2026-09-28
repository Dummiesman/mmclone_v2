using System.Collections.Generic;
using UnityEngine;

public class PathFollower : MonoBehaviour
{
    public enum RotationMode
    {
        /// <summary>Face along the spline's direction at the current point.</summary>
        Tangent,
        /// <summary>Face a point RotationLookAheadTarget meters further along the spline.</summary>
        LookAhead
    }

    //minimum squared length for a valid direction (~1cm)
    private const float MinDirSqr = 0.0001f;
    //half-width (meters) of the sample window used to measure the tangent
    private const float TangentSampleDistance = 0.05f;

    private PathSet.Path _path;

    public PathSet.Path Path
    {
        get
        {
            return _path;
        }
        set
        {
            _path = value;
            OnPathChanged();
        }
    }

    [Header("Movement")]
    public float FollowSpeed = 1f;
    public bool Looped = true;
    public float HeightOffset = 1f;

    [Header("Spline")]
    [Tooltip("0 = smooth Catmull-Rom curve, 1 = straight lines between points.")]
    [Range(0f, 1f)] public float Tightness = 0f;
    [Tooltip("Samples per segment used to measure arc length. Higher = more even speed.")]
    [Range(2, 64)] public int SamplesPerSegment = 16;

    [Header("Rotation")]
    /// <summary>
    /// Should this path follower effect the GameObject rotation?
    /// </summary>
    public bool ApplyRotation = true;
    public RotationMode RotationSource = RotationMode.Tangent;
    [Tooltip("Meters ahead to look when using LookAhead mode.")]
    public float RotationLookAheadTarget = 2f;
    public bool InterpolateRotation = false;
    [Tooltip("How quickly rotation catches up to the target. Higher = snappier.")]
    public float RotationSharpness = 8f;
    [Tooltip("Optional cap on turn rate in degrees per second. 0 = no cap.")]
    public float RotationMaxDPS = 0f;
    public bool AllowReverseRotation = false;

    [Header("Physics")]
    public bool UseRigidbody = false;
    public Rigidbody Rigidbody;

    public bool AtEndOfPath => !Looped && ((Progress == 0f && FollowSpeed < 0) || (Progress == 1f && FollowSpeed > 0));

    /// <summary>
    /// Normalized progress (0-1) along the spline. When looped, this includes the closing segment.
    /// </summary>
    public float Progress { get; private set; }

    /// <summary>
    /// Arc length of the spline in meters (includes the closing segment when looped).
    /// </summary>
    public float Length => EnsureSpline() ? _length : 0f;

    //spline cache
    private readonly List<Vector3> _points = new List<Vector3>();
    private readonly List<float> _lutDistance = new List<float>();
    private readonly List<float> _lutParam = new List<float>();
    private float _length;
    private bool _dirty = true;
    private float _builtTightness;
    private bool _builtLooped;
    private int _builtSamples;
    private int _builtPointCount;

    private int SegmentCount => Looped ? _points.Count : _points.Count - 1;

    // ------------------------------------------------------------------
    // Public API
    // ------------------------------------------------------------------

    /// <summary>
    /// Call this if you edit Path.Points in place at runtime.
    /// Changes to Path, Looped, Tightness or SamplesPerSegment are picked up automatically.
    /// </summary>
    public void MarkDirty()
    {
        _dirty = true;
    }

    /// <summary>
    /// World position on the spline (without HeightOffset) for a normalized progress value.
    /// </summary>
    public Vector3 EvaluatePosition(float progress)
    {
        if (!EnsureSpline())
            return transform.position;
        return PositionAtDistance(progress * _length);
    }

    public float CalculateMetersFromEnd()
    {
        if (!EnsureSpline())
            return 0f;

        if (Progress > 0.5f)
        {
            return _length - (Progress * _length);
        }
        else
        {
            return Progress * _length;
        }
    }

    public void SetProgress(float progress, bool setRotation = true)
    {
        this.Progress = Mathf.Clamp01(progress);
        UpdateTransform(!setRotation);
    }

    // ------------------------------------------------------------------
    // Spline building
    // ------------------------------------------------------------------

    private bool EnsureSpline()
    {
        if (_path == null || _path.Points == null || _path.Points.Count < 2)
            return false;

        if (_dirty
            || _builtTightness != Tightness
            || _builtLooped != Looped
            || _builtSamples != SamplesPerSegment
            || _builtPointCount != _path.Points.Count)
        {
            RebuildSpline();
        }

        return _length > 0f;
    }

    private void RebuildSpline()
    {
        _dirty = false;
        _builtTightness = Tightness;
        _builtLooped = Looped;
        _builtSamples = SamplesPerSegment;
        _builtPointCount = _path != null && _path.Points != null ? _path.Points.Count : 0;

        _points.Clear();
        _lutDistance.Clear();
        _lutParam.Clear();
        _length = 0f;

        if (_builtPointCount < 2)
            return;

        //copy points, skipping consecutive duplicates (they create zero-length segments)
        foreach (var p in _path.Points)
        {
            if (_points.Count == 0 || (p - _points[_points.Count - 1]).sqrMagnitude > MinDirSqr)
                _points.Add(p);
        }

        //looped path that already closes itself: drop the duplicate end point
        if (Looped && _points.Count > 2 && (_points[0] - _points[_points.Count - 1]).sqrMagnitude <= MinDirSqr)
            _points.RemoveAt(_points.Count - 1);

        if (_points.Count < 2)
            return;

        //build arc-length lookup table so speed is constant regardless of point spacing
        int segCount = SegmentCount;
        int samples = Mathf.Max(2, SamplesPerSegment);

        Vector3 prev = EvaluateParam(0f);
        _lutDistance.Add(0f);
        _lutParam.Add(0f);

        for (int s = 0; s < segCount; s++)
        {
            for (int i = 1; i <= samples; i++)
            {
                float u = s + (float)i / samples;
                Vector3 p = EvaluateParam(u);
                _length += Vector3.Distance(prev, p);
                prev = p;
                _lutDistance.Add(_length);
                _lutParam.Add(u);
            }
        }
    }

    // ------------------------------------------------------------------
    // Spline evaluation
    // ------------------------------------------------------------------

    private Vector3 GetPoint(int i)
    {
        int n = _points.Count;
        if (Looped)
            return _points[((i % n) + n) % n];

        //open path: mirror the end points to create phantom control points
        if (i < 0)
            return 2f * _points[0] - _points[1];
        if (i >= n)
            return 2f * _points[n - 1] - _points[n - 2];
        return _points[i];
    }

    private void GetHermite(int i, out Vector3 p0, out Vector3 p1, out Vector3 m0, out Vector3 m1)
    {
        Vector3 pm1 = GetPoint(i - 1);
        p0 = GetPoint(i);
        p1 = GetPoint(i + 1);
        Vector3 p2 = GetPoint(i + 2);

        //cardinal spline: tightness 0 = Catmull-Rom, 1 = zero tangents (straight lines)
        float s = (1f - Tightness) * 0.5f;

        //scale tangents by relative segment lengths so uneven point spacing doesn't overshoot
        float dPrev = Vector3.Distance(pm1, p0);
        float dCur = Vector3.Distance(p0, p1);
        float dNext = Vector3.Distance(p1, p2);
        float k0 = (dPrev + dCur) > 0f ? 2f * dCur / (dPrev + dCur) : 0f;
        float k1 = (dCur + dNext) > 0f ? 2f * dCur / (dCur + dNext) : 0f;

        m0 = s * k0 * (p1 - pm1);
        m1 = s * k1 * (p2 - p0);
    }

    /// <summary>
    /// Evaluates the spline at parameter u, where the integer part is the segment index
    /// and the fractional part is the position within that segment.
    /// </summary>
    private Vector3 EvaluateParam(float u)
    {
        int segCount = SegmentCount;
        u = Mathf.Clamp(u, 0f, segCount);
        int seg = Mathf.Min(Mathf.FloorToInt(u), segCount - 1);
        float t = u - seg;

        GetHermite(seg, out Vector3 p0, out Vector3 p1, out Vector3 m0, out Vector3 m1);

        float t2 = t * t;
        float t3 = t2 * t;
        return (2f * t3 - 3f * t2 + 1f) * p0
             + (t3 - 2f * t2 + t) * m0
             + (-2f * t3 + 3f * t2) * p1
             + (t3 - t2) * m1;
    }

    private float WrapDistance(float distance)
    {
        return Looped ? Mathf.Repeat(distance, _length) : Mathf.Clamp(distance, 0f, _length);
    }

    private Vector3 PositionAtDistance(float distance)
    {
        distance = WrapDistance(distance);

        //binary search the arc-length table
        int lo = 0;
        int hi = _lutDistance.Count - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (_lutDistance[mid] < distance)
                lo = mid;
            else
                hi = mid;
        }

        float span = _lutDistance[hi] - _lutDistance[lo];
        float f = span > 0f ? (distance - _lutDistance[lo]) / span : 0f;
        return EvaluateParam(Mathf.Lerp(_lutParam[lo], _lutParam[hi], f));
    }

    private Vector3 TangentAtDistance(float distance, float dirSign)
    {
        //central difference over a small arc-length window: stable even where
        //the spline's derivative is zero (e.g. at control points with tightness 1)
        Vector3 ahead = PositionAtDistance(distance + TangentSampleDistance);
        Vector3 behind = PositionAtDistance(distance - TangentSampleDistance);
        return (ahead - behind) * dirSign;
    }

    // ------------------------------------------------------------------
    // Transform updates
    // ------------------------------------------------------------------

    private void UpdateTransform(bool allowInterpolation = true)
    {
        if (!EnsureSpline())
            return;

        float distance = Progress * _length;

        //update position
        Vector3 position = PositionAtDistance(distance) + (Vector3.up * HeightOffset);
        if (UseRigidbody)
        {
            Rigidbody.MovePosition(position);
        }
        else
        {
            this.transform.position = position;
        }

        //update rotation
        if (!ApplyRotation)
            return;

        float dirSign = FollowSpeed < 0f ? -1f : 1f;
        Vector3 lookDir;

        if (RotationSource == RotationMode.LookAhead && RotationLookAheadTarget > 0f)
        {
            lookDir = PositionAtDistance(distance + dirSign * RotationLookAheadTarget) - PositionAtDistance(distance);

            //near the end of an open path the look-ahead point collapses; fall back to the tangent
            if (lookDir.sqrMagnitude < MinDirSqr)
                lookDir = TangentAtDistance(distance, dirSign);
        }
        else
        {
            lookDir = TangentAtDistance(distance, dirSign);
        }

        //no usable direction: keep current rotation
        if (lookDir.sqrMagnitude < MinDirSqr)
            return;

        //direction is parallel to up vector (vertical path): LookRotation would be unstable
        if (Vector3.Cross(lookDir, Vector3.up).sqrMagnitude < MinDirSqr)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(lookDir, Vector3.up);

        //reverse if we're going in reverse and this isn't allowed
        if (dirSign < 0f && !AllowReverseRotation)
            targetRotation *= Quaternion.Euler(0, 180f, 0);

        //smooth towards the target (frame-rate independent), optionally capped in degrees/sec
        if (InterpolateRotation && allowInterpolation)
        {
            Quaternion current = UseRigidbody ? Rigidbody.rotation : transform.rotation;
            float dt = Time.deltaTime;

            Quaternion smoothed = Quaternion.Slerp(current, targetRotation, 1f - Mathf.Exp(-RotationSharpness * dt));
            if (RotationMaxDPS > 0f)
                smoothed = Quaternion.RotateTowards(current, smoothed, RotationMaxDPS * dt);

            targetRotation = smoothed;
        }

        if (UseRigidbody)
        {
            Rigidbody.MoveRotation(targetRotation);
        }
        else
        {
            transform.rotation = targetRotation;
        }
    }

    private void OnPathChanged()
    {
        //prevent invalid path
        if (_path != null && (_path.Points == null || _path.Points.Count <= 1))
            _path = null;

        _dirty = true;

        if (_path == null)
            return;

        //snap to first position
        SetProgress(0f);
    }

    // Update is called once per frame
    void Update()
    {
        //we can't do anything without a path! :(
        if (!EnsureSpline())
            return;

        //update progress, wrapping (keeps overshoot) or clamping
        float next = Progress + (FollowSpeed / _length) * Time.deltaTime;
        Progress = Looped ? Mathf.Repeat(next, 1f) : Mathf.Clamp01(next);

        //update transform if visual only
        if (!UseRigidbody)
            UpdateTransform();
    }

    void FixedUpdate()
    {
        //update physics transform
        if (UseRigidbody)
            UpdateTransform();
    }

    void OnValidate()
    {
        _dirty = true;
    }

    // ------------------------------------------------------------------
    // Gizmos
    // ------------------------------------------------------------------

    void DrawGizmos(bool selected)
    {
        if (_path == null || _path.Points == null)
            return;

        //pick up point edits made in the editor
        if (!Application.isPlaying)
            _dirty = true;

        Gizmos.color = selected ? Color.yellow : Color.red;
        if (EnsureSpline())
        {
            Vector3 prev = EvaluateParam(_lutParam[0]);
            for (int i = 1; i < _lutParam.Count; i++)
            {
                Vector3 p = EvaluateParam(_lutParam[i]);
                Gizmos.DrawLine(prev, p);
                prev = p;
            }
        }

        Gizmos.color = selected ? Color.red : Color.blue;
        foreach (var pt in _path.Points)
        {
            Gizmos.DrawSphere(pt, 0.25f);
        }

        if (selected)
            Gizmos.DrawWireSphere(this.transform.position, 2f);
    }

    void OnDrawGizmos()
    {
        DrawGizmos(false);
    }

    void OnDrawGizmosSelected()
    {
        DrawGizmos(true);
    }
}
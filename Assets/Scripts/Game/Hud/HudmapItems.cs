using MM2;
using UnityEngine;

public class CopHudmapItem : HudmapItem
{
    private bool lastSirenStatus = false;
    private AIPoliceOfficer officer;

    public void Init(Transform target, AIPoliceOfficer officer)
    {
        this.officer = officer;
        base.Init(target);
        SetColor(Color.red);
        SetVisible(false);
        Offset = Vector3.up * 30.0f;
        BaseScale = new Vector3(1.75f * transform.localScale.x, 1.2f * transform.localScale.y, 1.2f * transform.localScale.z);
    }

    public override void Update()
    {
        base.Update();
        bool sirenStatus = officer.InPursuit;
        if (sirenStatus != lastSirenStatus)
        {
            SetVisible(sirenStatus);
        }
        lastSirenStatus = sirenStatus;
    }
}
enum HudmapItemWaypointState
{
    Unknown = -1,
    Hidden = 0,
    Uncleared = 1,
    Cleared = 2,
    Active = 3,
    Finish = 4,
}

public class HudmapItemWaypoint : HudmapItem
{
    private MMWaypoints waypoints;
    private WaypointObject wpobj;
    private int index = -1;
    private HudmapItemWaypointState lastState = HudmapItemWaypointState.Unknown;

    public enum WaypointTexture
    {
        RED_DOT, BLUE_DOT, GREEN_DOT, GREY_DOT, YELLOW_DOT, FINISH_DOT, GOLD_DOT, BANK_DOT, HIDEOUT_DOT
    }

    public void SetDimmed(bool dimmed)
    {
        SetColor(dimmed ? Color.grey : Color.white);
    }

    private void ChangeState(HudmapItemWaypointState newState)
    {
        if (newState == lastState)
            return;

        switch (newState)
        {
            case HudmapItemWaypointState.Hidden:
                SetVisible(false);
                break;
            case HudmapItemWaypointState.Uncleared:
                SetVisible(true);
                SetTexture(WaypointTexture.GREEN_DOT.ToString());
                break;
            case HudmapItemWaypointState.Cleared:
                SetVisible(true);
                SetTexture(WaypointTexture.GREY_DOT.ToString());
                break;
            case HudmapItemWaypointState.Active:
                SetVisible(true);
                SetTexture(WaypointTexture.YELLOW_DOT.ToString());
                break;
            case HudmapItemWaypointState.Finish:
                SetVisible(true);
                SetTexture(WaypointTexture.FINISH_DOT.ToString());
                break;
        }

        lastState = newState;
    }

    /// <summary>
    /// Works out what this dot should look like from the current race state.
    /// </summary>
    private HudmapItemWaypointState GetCurrentState()
    {
        if(waypoints.ShowOnlyActive && (index >= 0 && index != waypoints.TargetWaypoint))
        {
            return HudmapItemWaypointState.Hidden;
        }

        if (waypoints.IsCircuit && index == waypoints.WaypointObjects.Count - 1) return HudmapItemWaypointState.Finish;
        if (waypoints.Type == RaceType.UnorderedFinish && index == waypoints.WaypointObjects.Count - 1) return HudmapItemWaypointState.Finish;

        // Current target always shows as the active (yellow) dot
        if (index >= 0 && index == waypoints.TargetWaypoint)
            return HudmapItemWaypointState.Active;

        return wpobj.Active ? HudmapItemWaypointState.Uncleared : HudmapItemWaypointState.Cleared;
    }

    public void Init(MMWaypoints waypoints, WaypointObject wpobj)
    {
        Init(wpobj.transform, ComponentType.Square);
        Scale(2.0f);
        SetDimmed(false);

        this.FollowTransformRotation = false;
        this.FollowTransformScale = false;
        this.wpobj = wpobj;
        this.waypoints = waypoints;

        // Waypoints are created once in MMWaypoints.Init and never reordered,
        // so the index only needs to be looked up once
        var list = waypoints.WaypointObjects;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] == wpobj)
            {
                index = i;
                break;
            }
        }

        ChangeState(GetCurrentState());
    }

    public override void Update()
    {
        base.Update();

        if (waypoints == null || wpobj == null)
            return;

        ChangeState(GetCurrentState());
    }
}
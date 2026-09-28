using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AudioZoneManager
{
    public Action<Zone> OnZoneChanged;

    private List<AudioSource> _sources = new List<AudioSource>();
    private List<Zone> _zones = new List<Zone>();
    private Zone _lastZone = Zone.Everywhere;

    public enum Zone
    {
        Everywhere,
        Subterranean,
        AboveGround
    }

    public void SetZone(Zone zone)
    {
        //no need to go and loop through stuff if we don't have to
        if (zone == _lastZone)
            return;
        
        for (int i = 0; i < _sources.Count; i++)
        {
            _sources[i].enabled = (_zones[i] == zone || _zones[i] == Zone.Everywhere);
        }

        OnZoneChanged?.Invoke(zone);
        _lastZone = zone;
    }

    public void AddToManager(AudioSource source, Zone zone)
    {
        _sources.Add(source);
        _zones.Add(zone);
    }

    public void RemoveFromManager(AudioSource source)
    {
        int index = _sources.IndexOf(source);
        if (index >= 0)
        {
            _sources.RemoveAt(index);
            _zones.RemoveAt(index);
        }
    }
}

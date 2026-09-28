using PSDL;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class Propulator 
{
    public class PropRule
    {
        public string Name;
        public List<PropDef> Defs = new List<PropDef>();
    }

    public class PropDef
    {
        public string Name;
        public float StartDist;
        public float Spacing;
        public int MaxUse;
        public float MinLerp;
        public float MaxLerp;
        public List<string> ModelNames = new List<string>();

        public float LastOffset = 0f;
        public int LastUsage = 0;

        public void ResetStatus()
        {
            LastOffset = 0f;
            LastUsage = 0;
        }
    }

    public List<PropRule> PropRules = new List<PropRule>();
    public void  ReadProprules(string city)
    {
        // clear list out first
        PropRules.Clear();

        // read
        var csvReader = AssetManager.OpenCSV("city/" + city, "proprules");
        if (csvReader != null)
        {
            csvReader.Seek(1, System.IO.SeekOrigin.Current); // skip header

            while (!csvReader.EOF())
            {
                csvReader.PrepareLine();
                string[] tokens = csvReader.GetTokens();

                var rule = new PropRule()
                {
                    Name = tokens[0]
                };

                for (int i = 1; i < tokens.Length; i++)
                {
                    var def = PropDefs.FirstOrDefault(pd => pd.Name.ToLowerInvariant() == tokens[i].ToLowerInvariant());
                    if (def != null)
                        rule.Defs.Add(def);
                }

                PropRules.Add(rule);
            }
        }
    }

    public List<PropDef> PropDefs = new List<PropDef>();
    public void ReadPropdefs(string city)
    {
        // clear list out first
        PropDefs.Clear();

        // read
        var csvReader = AssetManager.OpenCSV("city/" + city, "propdefs");
        if (csvReader != null)
        {
            csvReader.Seek(1, System.IO.SeekOrigin.Current); // skip header

            while (!csvReader.EOF())
            {
                csvReader.PrepareLine();
                string[] tokens = csvReader.GetTokens();

                var def = new PropDef()
                {
                    Name = csvReader[0].ToLowerInvariant(),
                    StartDist = FastFloatParser.Parse(csvReader[1]),
                    Spacing = FastFloatParser.Parse(csvReader[2]),
                    MaxUse = FastIntParser.Parse(csvReader[3]),
                    MinLerp = FastFloatParser.Parse(csvReader[4]),
                    MaxLerp = FastFloatParser.Parse(csvReader[5])
                };

                // get model files
                for (int i = 6; i < tokens.Length; i++)
                {
                    def.ModelNames.Add(tokens[i]);
                }


                PropDefs.Add(def);
            }
        }
    }

    public void Init(string city)
    {
        if(!AssetManager.Exists($"city", city, "propdefs.csv") ||
           !AssetManager.Exists($"city", city, "proprules.csv"))
        {
            Debug.LogWarning("Cannot find a propdefs or proprules file!");
            return;
        }

        ReadPropdefs(city);
        ReadProprules(city);
    }

    private PropRule CurrentRule;
    private bool Reversed = false;
        
    private void Propulate(Vector3[] outerSw, Vector3[] innerSw, Vector3[] center, PropRule rule, System.Action<string, Vector3, Quaternion> propCb)
    {
        CurrentRule = rule;

        int numRows = outerSw.Length;
        foreach(var def in CurrentRule.Defs)
        {
            float current = def.StartDist + def.LastOffset;
            int usageCount = def.LastUsage;

            for(int i=0; i < numRows - 1; i++)
            {
                rowStart:; // shit

                bool rowComplete = false;
                Vector3 rowCenter = Vector3.Lerp(center[i], center[i], 0.5f);
                Vector3 nextRowCenter = Vector3.Lerp(center[i+1], center[i+1], 0.5f);
                float rowLength = Vector3.Distance(rowCenter, nextRowCenter);

                // check if LastOffset did this for us
                if (current > rowLength)
                {
                    current -= rowLength;
                    rowComplete = true;
                    continue;
                }

                if (usageCount >= def.MaxUse)
                    break;

                float lerp = Random.Range(def.MinLerp, def.MaxLerp);
                Vector3 outerLerp = Vector3.Lerp(outerSw[i], outerSw[i+1], current / rowLength).ConvertCoordinateSpace();
                Vector3 innerLerp = Vector3.Lerp(innerSw[i], innerSw[i+1], current / rowLength).ConvertCoordinateSpace();
                Vector3 centerLerp = Vector3.Lerp(rowCenter, nextRowCenter, current / rowLength).ConvertCoordinateSpace();

                // place this prop
                Vector3 bangerPos = Vector3.Lerp(innerLerp, outerLerp, lerp);
                int modelNum = Random.Range(0, def.ModelNames.Count);
                Quaternion rot = Quaternion.LookRotation((centerLerp - bangerPos).normalized) * Quaternion.Euler(0, -90, 0);
                propCb?.Invoke(def.ModelNames[modelNum], bangerPos, rot);

                current += def.Spacing;
                if(current > rowLength)
                {
                    current -= rowLength;
                    rowComplete = true;
                }

                usageCount++;
                if (usageCount >= def.MaxUse)
                    break;

                if (!rowComplete)
                    goto rowStart;
            }

            def.LastUsage = usageCount;
            def.LastOffset = current;
        }
    }

    private void Propulate(ISDLElement element, PropRule leftRule, PropRule rightRule, System.Action<string, Vector3, Quaternion> propCb)
    {
        Vector3[] ll, l, r, rr, center;
        if(element.Type == ElementType.Road)
        {
            var road = (PSDL.Elements.RoadElement)element;
            int rows = road.RowCount;
            ll = new Vector3[rows];
            l = new Vector3[rows];
            r = new Vector3[rows];
            rr = new Vector3[rows];
            center = new Vector3[rows];

            if (Reversed)
            {
                for (int i = 0; i < rows; i++)
                {
                    var roadCenter = road.GetRowCenterPoint(i).ToUnity();
                    l[i] = roadCenter;
                    ll[i] = road.Vertices[(i * 4) + 1].ToUnity() + (Vector3.up * 0.15f);
                    rr[i] = road.Vertices[(i * 4) + 2].ToUnity() + (Vector3.up * 0.15f);
                    r[i] = roadCenter;
                    center[i] = Vector3.Lerp(l[i], r[i], 0.5f);
                }
            }
            else
            {
                for (int i = 0; i < rows; i++)
                {
                    ll[i] = road.Vertices[i * 4].ToUnity();
                    l[i] = road.Vertices[(i * 4) + 1].ToUnity() + (Vector3.up * 0.15f);
                    r[i] = road.Vertices[(i * 4) + 2].ToUnity() + (Vector3.up * 0.15f);
                    rr[i] = road.Vertices[(i * 4) + 3].ToUnity();
                    center[i] = Vector3.Lerp(l[i], r[i], 0.5f);
                }
            }

            Propulate(ll, l, center, leftRule, propCb);
            Propulate(rr, r, center, rightRule, propCb);
        }
        else if(element.Type == ElementType.DividedRoad)
        {
            var road = (PSDL.Elements.DividedRoadElement)element;
            int rows = road.RowCount;
            ll = new Vector3[rows];
            l = new Vector3[rows];
            r = new Vector3[rows];
            rr = new Vector3[rows];
            center = new Vector3[rows];
            for (int i = 0; i < rows; i++)
            {
                ll[i] = road.Vertices[i * 6].ToUnity();
                l[i] = road.Vertices[(i * 6) + 1].ToUnity() + (Vector3.up * 0.15f);
                r[i] = road.Vertices[(i * 6) + 4].ToUnity() + (Vector3.up * 0.15f);
                rr[i] = road.Vertices[(i * 6) + 5].ToUnity();
                center[i] = Vector3.Lerp(l[i], r[i], 0.5f);
            }

            Propulate(ll, l, center, leftRule, propCb);
            Propulate(rr, r, center, rightRule, propCb);
        }
    }

    public void Propulate(Room room, int propRuleIndex, System.Action<string, Vector3, Quaternion> propCb)
    {
        int leftRule = propRuleIndex * 2;
        int rightRule = leftRule + 1;

        // fail!
        if (leftRule >= PropRules.Count || rightRule >= PropRules.Count)
        {
            Debug.LogWarning($"Room propulation failed because either {leftRule} or {rightRule} are out of bounds.");
            return;
        }

        // set random seed
        var currentRandomState = Random.state;
        Random.InitState(int.MaxValue / 2);

        // propulate
        foreach (var element in room.Elements)
        {
            Propulate(element, PropRules[leftRule], PropRules[rightRule], propCb);
        }

        // reset state
        Random.state = currentRandomState;
    }
    
    public void Propulate(Room room, System.Action<string, Vector3, Quaternion> propCb)
    {
        if (room.PropRule == 0)
            return;
        Propulate(room, room.PropRule - 1, propCb);
    }

    public void Propulate(AIRoad road, System.Action<string, Vector3, Quaternion> propCb)
    {
        if (road.Rooms.Count == 0) return;
        var firstRoom = road.Rooms[0];
        if(firstRoom.PropRule > 0)
        {
            foreach (var room in road.Rooms)
            {
                Propulate(room, firstRoom.PropRule - 1, propCb);
            }
        }

        // reset state
        Reversed = false;
        ResetPropdefs();
    }

    public void ResetPropdefs()
    {
        foreach (var rule in PropRules)
        {
            foreach (var def in rule.Defs)
            {
                def.ResetStatus();
            }
        }
    }
}

using System;
using UnityEngine;

namespace MM2.AI
{
    public class TrafficLightInstance : UnhitBangerInstance
    {
        private const string AdditiveShaderName = "Vehicle/Additive";
        private static Shader additiveShader;

        private static MaterialProperties whiteAdditive;
        private static MaterialProperties WhiteAdditive => whiteAdditive ?? (whiteAdditive = new MaterialProperties().SetColor("_Color", Color.white));

        // indexed by TrafficLightState
        private static readonly string[] LightGroupsDay = { "REDGLOWDAY", "YELLOWGLOWDAY", "GREENGLOWDAY" };
        private static readonly string[] LightGroupsNight = { "REDGLOWNIGHT", "YELLOWGLOWNIGHT", "GREENGLOWNIGHT" };

        // indexed by PedLightState
        private static readonly string[] PedestrianGroupsDay = { "WALK_DAY" , "NOWALK_DAY" };
        private static readonly string[] PedestrianGroupsNight = { "WALK_NIGHT" , "NOWALK_NIGHT" };

        // time of day is fixed, so only one variant of each glow is ever loaded
        private static bool IsNight => GameState.SelectedTimeOfDay == MMTimeOfDay.Night;
        private static string[] LightGroups => IsNight ? LightGroupsNight : LightGroupsDay;
        private static string[] PedestrianGroups => IsNight ? PedestrianGroupsNight : PedestrianGroupsDay;

        private readonly GameObject[] lightParts = new GameObject[LightGroupsDay.Length];
        private readonly GameObject[] pedestrianParts = new GameObject[PedestrianGroupsDay.Length];

        private Vector3 bangerCenter;

        private TrafficLightState lightState = TrafficLightState.Red;
        private PedLightState pedestrianLightState = PedLightState.NoWalk;

        public TrafficLightState LightState
        {
            get => lightState;
            set
            {
                if (lightState == value) return;
                lightState = value;
                ShowOnly(lightParts, 2 - (int)lightState);
            }
        }

        public PedLightState PedestrianLightState
        {
            get => pedestrianLightState;
            set
            {
                if (pedestrianLightState == value) return;
                pedestrianLightState = value;
                ShowOnly(pedestrianParts, 1 - (int)pedestrianLightState);
            }
        }

        public static TrafficLightInstance RequestTrafficLight(SDLCity level, string propType, Vector3 propPos, Quaternion propRot)
        {
            var built = new GameObject(propType);
            var inst = built.AddComponent<TrafficLightInstance>();
            inst.Init(level, propType, propPos, propRot, Vector3.one);
            return inst;
        }

        protected override bool ShouldInstantiatePart(string partName)
        {
            return base.ShouldInstantiatePart(partName)
                || IndexOf(LightGroups, partName) >= 0
                || IndexOf(PedestrianGroups, partName) >= 0;
        }

        public override void Init(SDLCity level, string basename, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            if (level != null)
            {
                int dataIndex =level.BangerDataManager.AddEntry(basename);
                if(dataIndex >= 0)
                {
                    bangerCenter = level.BangerDataManager.GetEntry(dataIndex).CG;
                }
            }

            base.Init(level, basename, position, rotation, scale);
        }

        private static Shader GetAdditiveShader()
        {
            if (additiveShader == null)
                additiveShader = Shader.Find(AdditiveShaderName);
            return additiveShader;
        }

        protected override void LoadExtraGroups(PackageObjectLoader loader)
        {
            // everything after this point is a glow
            loader.SetShader(GetAdditiveShader());
            loader.SetProperties(WhiteAdditive);

            foreach (var group in LightGroupsDay)
                loader.LoadGroup(group, applyPivot: false);
            foreach (var group in PedestrianGroupsDay)
                loader.LoadGroup(group, applyPivot: false);
            foreach (var group in LightGroupsNight)
                loader.LoadGroup(group, applyPivot: false);
            foreach (var group in PedestrianGroupsNight)
                loader.LoadGroup(group, applyPivot: false);
        }

        protected override void AssignNamedParts(PackageObjectInstance instance)
        {
            base.AssignNamedParts(instance);

            var lightGroups = LightGroups;
            var pedestrianGroups = PedestrianGroups;

            foreach (var part in instance.Parts)
            {
                if (part.Object == null) continue;

                int index = IndexOf(lightGroups, part.Name);
                if (index >= 0)
                {
                    lightParts[index] = part.Object;
                    part.Object.transform.localPosition -= bangerCenter;
                    continue;
                }


                index = IndexOf(pedestrianGroups, part.Name);
                if (index >= 0)
                {
                    pedestrianParts[index] = part.Object;
                    part.Object.transform.localPosition -= bangerCenter;
                }
            }

            ShowOnly(lightParts, (int)lightState);
            ShowOnly(pedestrianParts, (int)pedestrianLightState);
        }

        private static int IndexOf(string[] names, string partName)
        {
            return Array.FindIndex(names, n => string.Equals(n, partName, StringComparison.OrdinalIgnoreCase));
        }

        private static void ShowOnly(GameObject[] parts, int active)
        {
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] != null)
                    parts[i].SetActive(i == active);
            }
        }
    }
}
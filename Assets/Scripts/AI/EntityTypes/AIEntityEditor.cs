#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
/*
namespace MM2.AI
{
    [CustomEditor(typeof(AIEntity), true)]
    public class AIEntityEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            base.DrawDefaultInspector();

            var targetE = (AIEntity)target;
            GUILayout.Label("AI Information", EditorStyles.boldLabel);

            var rd = targetE.RoadInfo.RoadInstance;

            GUILayout.Label($"Speed: {targetE.Speed}");
            GUILayout.Space(5f);

            if (rd != null)
            {
                GUILayout.Label($"Road: {rd.Id}");
                GUILayout.Label($"Road SD: {targetE.RoadInfo.SideOfRoad}");
                GUILayout.Label($"Road Rail: {targetE.RoadInfo.RailIndex}");
                GUILayout.Label($"Road Rail Count: {targetE.RoadInfo.RoadData.GetRailCount(targetE.RailType)}");
            }

            var nextRd = targetE.NextRoadInfo.RoadInstance;
            if (nextRd != null)
            {
                GUILayout.Space(5f);
                GUILayout.Label($"Next Road: {nextRd.Id}");
                GUILayout.Label($"Next Road SD: {targetE.NextRoadInfo.SideOfRoad}");
                GUILayout.Label($"Next Road Rail: {targetE.NextRoadInfo.RailIndex}");

                var nrd = targetE.NextRoadInfo.SideOfRoad > 0 ? nextRd.RightData : nextRd.LeftData;
                GUILayout.Label($"Next Road Rail Count: {nrd.GetRailCount(targetE.RailType)}");
            }
        }
    }
}
*/
#endif
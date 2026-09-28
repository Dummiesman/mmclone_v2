#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ParticleSim))]
[CanEditMultipleObjects]
public class ParticleSimEditor : Editor
{
    private int emitCount = 10;
    private bool showBirthRule = true;

    public override bool RequiresConstantRepaint()
    {
        //keep the live particle count / birth rule readout ticking in play mode
        return Application.isPlaying;
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        DrawControls();

        if (targets.Length == 1)
        {
            EditorGUILayout.Space();
            DrawBirthRule((ParticleSim)target);
        }
    }

    private void DrawControls()
    {
        EditorGUILayout.LabelField("Controls", EditorStyles.boldLabel);

        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            if (!Application.isPlaying)
                EditorGUILayout.HelpBox("Enter play mode to emit particles.", MessageType.None);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Blast", GUILayout.Height(24f)))
                    ForEachTarget(sim => sim.Blast());

                if (GUILayout.Button($"Emit {emitCount}", GUILayout.Height(24f)))
                    ForEachTarget(sim => sim.Emit(emitCount));

                emitCount = Mathf.Max(1, EditorGUILayout.IntField(emitCount, GUILayout.Width(50f), GUILayout.Height(24f)));
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Clear"))
                    ForEachTarget(sim => sim.ClearParticles());

                if (GUILayout.Button("Reset Emit Counter"))
                    ForEachTarget(sim => sim.ResetEmitCounter());

                if (GUILayout.Button("Dump To Console"))
                    ForEachTarget(sim => sim.Dump());
            }
        }

        if (Application.isPlaying && targets.Length == 1)
        {
            var sim = (ParticleSim)target;
            EditorGUILayout.LabelField("Live", $"{sim.ParticleCount} particles   bounds {sim.Bounds.size}");
        }
    }

    private void ForEachTarget(System.Action<ParticleSim> action)
    {
        foreach (var t in targets)
        {
            var sim = t as ParticleSim;
            if (sim == null)
                continue;

            if (sim.BirthRule == null)
            {
                Debug.LogWarning($"{sim.name} has no BirthRule assigned.", sim);
                continue;
            }

            action(sim);
        }
    }

    //BIRTH RULE
    //ParticleBirthRule is a plain class assigned at runtime, so Unity never serializes it.
    //Everything below is drawn and written back by hand.
    private void DrawBirthRule(ParticleSim sim)
    {
        showBirthRule = EditorGUILayout.Foldout(showBirthRule, "Birth Rule", true, EditorStyles.foldoutHeader);
        if (!showBirthRule)
            return;

        var rule = sim.BirthRule;
        if (rule == null)
        {
            EditorGUILayout.HelpBox("No BirthRule assigned. It's set at runtime, usually when the effect is loaded.",
                                    MessageType.Info);
            return;
        }

        EditorGUI.indentLevel++;
        EditorGUI.BeginChangeCheck();

        DrawVariable("Position", rule.Position);
        DrawVariable("Velocity", rule.Velocity);
        DrawVariable("Life", rule.Life);
        DrawVariable("Mass", rule.Mass);
        DrawVariable("Radius", rule.Radius);
        DrawVariable("Drag", rule.Drag);
        DrawVariable("Damp", rule.Damp);
        DrawVariable("DRadius", rule.DRadius);
        DrawVariable("DAlpha", rule.DAlpha);
        DrawVariable("DRotation", rule.DRotation);

        EditorGUILayout.Space();
        rule.InitialBlast = EditorGUILayout.IntField("Initial Blast", rule.InitialBlast);
        rule.SpewRate = EditorGUILayout.FloatField("Spew Rate", rule.SpewRate);
        rule.SpewTimeLimit = EditorGUILayout.FloatField("Spew Time Limit", rule.SpewTimeLimit);
        rule.Gravity = EditorGUILayout.FloatField("Gravity", rule.Gravity);

        EditorGUILayout.Space();
        rule.TexFrameStart = EditorGUILayout.IntField("Tex Frame Start", rule.TexFrameStart);
        rule.TexFrameEnd = EditorGUILayout.IntField("Tex Frame End", rule.TexFrameEnd);
        rule.BirthFlags = (ParticleBirthFlags)EditorGUILayout.EnumFlagsField("Birth Flags", rule.BirthFlags);

        EditorGUILayout.Space();
        rule.Height = EditorGUILayout.FloatField("Height", rule.Height);
        rule.Intensity = EditorGUILayout.FloatField("Intensity", rule.Intensity);
        rule.Color = EditorGUILayout.ColorField("Color", rule.Color);

        if (EditorGUI.EndChangeCheck())
        {
            //the rule is a shared reference, so edits reach every sim already using it.
            //nothing to mark dirty - it isn't serialized - but repaint the scene view
            //so the emitter gizmos update immediately.
            SceneView.RepaintAll();
        }

        if ((rule.BirthFlags & ParticleBirthFlags.Animated) != 0 && rule.TexFrameEnd <= rule.TexFrameStart)
        {
            EditorGUILayout.HelpBox("Animated is set but TexFrameEnd is not greater than TexFrameStart, " +
                                    "so no animation will play.", MessageType.Warning);
        }

        EditorGUI.indentLevel--;
    }

    private static void DrawVariable(string label, ParticleBirthRule.VariableProperty<float> property)
    {
        if (property == null)
        {
            EditorGUILayout.LabelField(label, "<null>");
            return;
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.PrefixLabel(label);
            property.Value = EditorGUILayout.FloatField(property.Value);
            GUILayout.Label("±", GUILayout.Width(12f));
            property.Variation = EditorGUILayout.FloatField(property.Variation);
        }
    }

    private static void DrawVariable(string label, ParticleBirthRule.VariableProperty<Vector3> property)
    {
        if (property == null)
        {
            EditorGUILayout.LabelField(label, "<null>");
            return;
        }

        EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
        EditorGUI.indentLevel++;
        property.Value = EditorGUILayout.Vector3Field("Value", property.Value);
        property.Variation = EditorGUILayout.Vector3Field("Variation", property.Variation);
        EditorGUI.indentLevel--;
    }
}
#endif
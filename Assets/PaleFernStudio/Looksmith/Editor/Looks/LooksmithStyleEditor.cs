using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace PaleFernStudio.Looksmith.Editor
{
    /// <summary>
    /// Inspector for every Art Style override: Intensity, Order and Layers first (Layers as a rendering-layer mask, Pro), then the
    /// style's own settings.
    /// </summary>
    [CustomEditor(typeof(LooksmithStyle), true)]
    sealed class LooksmithStyleEditor : VolumeComponentEditor
    {
        static readonly string[] k_Stack = { "intensity", "order", "layers" };
        readonly List<(string name, SerializedDataParameter parameter)> m_Parameters = new List<(string, SerializedDataParameter)>();
        static string[] s_LayerNames;

        public override void OnEnable()
        {
            base.OnEnable();
            m_Parameters.Clear();
            foreach (var name in k_Stack)
                m_Parameters.Add((name, Unpack(serializedObject.FindProperty(name))));
            foreach (var field in target.GetType().GetFields())
            {
                if (!typeof(UnityEngine.Rendering.VolumeParameter).IsAssignableFrom(field.FieldType) || System.Array.IndexOf(k_Stack, field.Name) >= 0)
                    continue;
                var property = serializedObject.FindProperty(field.Name);
                if (property != null)
                    m_Parameters.Add((field.Name, Unpack(property)));
            }
        }

        public override void OnInspectorGUI()
        {
            if (!LooksmithUserSettings.instance.showAllFields)
            {
                LooksmithTweaksInspector.Header("Look", "One of the art styles in this look.", null);
                return;
            }
            for (int i = 0; i < m_Parameters.Count; i++)
            {
                var (name, p) = m_Parameters[i];
                if (name == "layers")
                {
                    if (LooksmithEdition.Shows(target.GetType(), name))   // Lite has no layer mask (a Pro pass draws it)
                        RenderingLayersField(p, EditorGUIUtility.TrTextContent("Layers", "Rendering layers that get this style. Objects on other layers stay crisp (characters, props, world-space UI)."));
                }
                else
                    PropertyField(p);
                if (i == k_Stack.Length - 1)
                    EditorGUILayout.Space();
            }
        }

        void RenderingLayersField(SerializedDataParameter p, GUIContent label)
        {
            if (s_LayerNames == null)
            {
                s_LayerNames = new string[32];
                for (int i = 0; i < 32; i++)
                {
                    var n = RenderingLayerMask.RenderingLayerToName(i);
                    s_LayerNames[i] = string.IsNullOrEmpty(n) ? $"Layer {i}" : n;
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawOverrideCheckbox(p);
                using (new EditorGUI.DisabledScope(!p.overrideState.boolValue))
                    p.value.intValue = EditorGUILayout.MaskField(label, p.value.intValue, s_LayerNames);
            }
        }
    }
}

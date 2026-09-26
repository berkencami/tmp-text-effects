using UnityEditor;
using UnityEngine;

namespace TMPTextEffects.Editor
{
    [CustomEditor(typeof(LayeredText))]
    [CanEditMultipleObjects]
    public class LayeredTextEditor : UnityEditor.Editor
    {
        private SerializedProperty _style;
        private SerializedProperty _arcAngle;
        private UnityEditor.Editor _styleEditor;
        private static bool _styleFoldout = true;

        private void OnEnable()
        {
            _style = serializedObject.FindProperty("_Style");
            _arcAngle = serializedObject.FindProperty("_ArcAngle");
        }

        private void OnDisable()
        {
            if (_styleEditor != null) DestroyImmediate(_styleEditor);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(_style);
                if (GUILayout.Button(_style.objectReferenceValue == null ? "New" : "Clone", GUILayout.Width(52)))
                    CreateStyle((LayeredTextStyle)_style.objectReferenceValue);
            }

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(_arcAngle, new GUIContent("Arc", _arcAngle.tooltip));
            bool arcChanged = EditorGUI.EndChangeCheck();

            serializedObject.ApplyModifiedProperties();
            if (arcChanged)
            {
                foreach (var t in targets) ((LayeredText)t).Text.SetVerticesDirty();
            }

            var style = _style.hasMultipleDifferentValues ? null : _style.objectReferenceValue as LayeredTextStyle;
            if (style == null)
            {
                EditorGUILayout.HelpBox("Assign a Layered Text Style (or press New) to render layers.", MessageType.Info);
                return;
            }

            if (!AssetDatabase.IsOpenForEdit(style) || AssetDatabase.GetAssetPath(style).StartsWith("Packages/com.berkencami"))
            {
                EditorGUILayout.HelpBox("This is a built-in preset. Press Clone to get an editable copy.", MessageType.Info);
            }

            var tmp = ((LayeredText)target).Text;
            if (tmp != null && tmp.font != null && tmp.font.atlasPadding < 12)
            {
                EditorGUILayout.HelpBox(
                    $"Font atlas padding is {tmp.font.atlasPadding}px — thick outlines/glows saturate at the padding. " +
                    "For bold multi-outline titles, select a font file and use Tools ▸ TMP Text Effects ▸ Create Wide-Padding Font Asset.",
                    MessageType.None);
            }

            EditorGUILayout.Space(4);
            _styleFoldout = EditorGUILayout.InspectorTitlebar(_styleFoldout, style);
            if (_styleFoldout)
            {
                CreateCachedEditor(style, typeof(LayeredTextStyleEditor), ref _styleEditor);
                EditorGUI.indentLevel++;
                _styleEditor.OnInspectorGUI();
                EditorGUI.indentLevel--;
            }
        }

        private void CreateStyle(LayeredTextStyle source)
        {
            string path = EditorUtility.SaveFilePanelInProject(source != null ? "Clone Layered Text Style" : "New Layered Text Style",
                source != null ? source.name : "New Style", "asset", "Where should the style be saved?", "Assets");
            if (string.IsNullOrEmpty(path)) return;

            var style = CreateInstance<LayeredTextStyle>();
            if (source != null)
            {
                style.GradientSpace = source.GradientSpace;
                style.GradientAngle = source.GradientAngle;
                foreach (var l in source.Layers) style.Layers.Add(l.Clone());
            }
            else
            {
                style.Layers.Add(TextLayer.Create(TextLayerType.Shadow));
                style.Layers.Add(TextLayer.Create(TextLayerType.Outline));
                style.Layers.Add(TextLayer.Create(TextLayerType.Face));
            }

            AssetDatabase.CreateAsset(style, path);
            AssetDatabase.SaveAssets();

            serializedObject.Update();
            _style.objectReferenceValue = style;
            serializedObject.ApplyModifiedProperties();
            foreach (var t in targets) ((LayeredText)t).Apply();
            EditorGUIUtility.PingObject(style);
        }
    }
}

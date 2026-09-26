using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace TMPTextEffects.Editor
{
    [CustomEditor(typeof(LayeredTextStyle))]
    public class LayeredTextStyleEditor : UnityEditor.Editor
    {
        private SerializedProperty _layers;
        private SerializedProperty _gradientSpace;
        private SerializedProperty _gradientAngle;
        private ReorderableList _list;

        private static readonly float _line = EditorGUIUtility.singleLineHeight;
        private static readonly float _gap = EditorGUIUtility.standardVerticalSpacing;

        private void OnEnable()
        {
            _layers = serializedObject.FindProperty(nameof(LayeredTextStyle.Layers));
            _gradientSpace = serializedObject.FindProperty(nameof(LayeredTextStyle.GradientSpace));
            _gradientAngle = serializedObject.FindProperty(nameof(LayeredTextStyle.GradientAngle));

            _list = new ReorderableList(serializedObject, _layers, true, true, true, true)
            {
                drawHeaderCallback = r => EditorGUI.LabelField(r, "Layers  (top = back, bottom = front)"),
                elementHeightCallback = ElementHeight,
                drawElementCallback = DrawElement,
                onAddDropdownCallback = (_, _) => ShowAddMenu(),
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(_gradientSpace);
            EditorGUILayout.PropertyField(_gradientAngle);
            EditorGUILayout.Space(2);
            _list.DoLayoutList();

            int enabled = 0;
            for (int i = 0; i < _layers.arraySize; i++)
            {
                if (_layers.GetArrayElementAtIndex(i).FindPropertyRelative(nameof(TextLayer.Enabled)).boolValue) enabled++;
            }
            if (enabled > LayeredTextStyle.MaxLayers)
            {
                EditorGUILayout.HelpBox(
                    $"Only the first {LayeredTextStyle.MaxLayers} enabled layers render ({enabled} enabled).",
                    MessageType.Warning);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void ShowAddMenu()
        {
            var menu = new GenericMenu();
            foreach (TextLayerType type in System.Enum.GetValues(typeof(TextLayerType)))
            {
                var t = type;
                menu.AddItem(new GUIContent(t.ToString()), false, () => AddLayer(t));
            }
            menu.ShowAsContext();
        }

        private void AddLayer(TextLayerType type)
        {
            var style = (LayeredTextStyle)target;
            Undo.RecordObject(style, "Add Text Layer");

            // New fills go behind the face; faces go on top.
            var layer = TextLayer.Create(type);
            int faceIndex = style.Layers.FindIndex(l => l.Type == TextLayerType.Face);
            if (type == TextLayerType.Face || faceIndex < 0) style.Layers.Add(layer);
            else style.Layers.Insert(faceIndex, layer);

            EditorUtility.SetDirty(style);
            style.NotifyChanged();
            serializedObject.Update();
        }

        // ── element layout ──────────────────────────────────────

        private static int RowCount(SerializedProperty el)
        {
            var type = (TextLayerType)el.FindPropertyRelative(nameof(TextLayer.Type)).enumValueIndex;
            bool gradient = el.FindPropertyRelative(nameof(TextLayer.UseGradient)).boolValue;

            int rows = 1;                  // header: enabled, name, type
            rows += 2;                     // color, gradient toggle
            if (gradient) rows += 1;       // color2
            rows += 2;                     // dilate, softness

            switch (type)
            {
                case TextLayerType.Face:
                    rows += 2;             // tint, texture
                    if (el.FindPropertyRelative(nameof(TextLayer.Texture)).objectReferenceValue != null) rows += 3;
                    rows += 1;             // gloss color
                    if (GlossOn(el)) rows += 4;
                    rows += 2;             // inner shadow, inner highlight colors
                    if (InnerOn(el)) rows += 2;
                    rows += 1;             // shine color
                    if (ShineOn(el)) rows += 3;
                    break;
                case TextLayerType.Outline:
                case TextLayerType.Shadow:
                    rows += 1;             // offset
                    break;
                case TextLayerType.Extrude:
                    rows += 3;             // angle, depth, steps
                    break;
            }
            return rows;
        }

        private float ElementHeight(int index)
        {
            var el = _layers.GetArrayElementAtIndex(index);
            if (!el.isExpanded) return _line + _gap * 2;
            return RowCount(el) * (_line + _gap) + _gap * 2;
        }

        private void DrawElement(Rect rect, int index, bool active, bool focused)
        {
            var el = _layers.GetArrayElementAtIndex(index);
            var type = (TextLayerType)el.FindPropertyRelative(nameof(TextLayer.Type)).enumValueIndex;

            rect.y += _gap;
            var row = new Rect(rect.x, rect.y, rect.width, _line);

            // Header row: foldout + enabled + name + type.
            var foldRect = new Rect(row.x + 10, row.y, 14, _line);
            el.isExpanded = EditorGUI.Foldout(foldRect, el.isExpanded, GUIContent.none, true);
            var enRect = new Rect(row.x + 14, row.y, 18, _line);
            EditorGUI.PropertyField(enRect, el.FindPropertyRelative(nameof(TextLayer.Enabled)), GUIContent.none);
            var typeRect = new Rect(row.xMax - 90, row.y, 90, _line);
            var nameRect = new Rect(enRect.xMax + 2, row.y, typeRect.x - enRect.xMax - 6, _line);
            EditorGUI.PropertyField(nameRect, el.FindPropertyRelative(nameof(TextLayer.Name)), GUIContent.none);
            EditorGUI.PropertyField(typeRect, el.FindPropertyRelative(nameof(TextLayer.Type)), GUIContent.none);

            if (!el.isExpanded) return;

            EditorGUI.indentLevel++;
            bool extrude = type == TextLayerType.Extrude;
            bool gradient = el.FindPropertyRelative(nameof(TextLayer.UseGradient)).boolValue;

            Row(ref row, el, nameof(TextLayer.Color), extrude ? "Front Color" : gradient ? "Top Color" : "Color");
            Row(ref row, el, nameof(TextLayer.UseGradient), extrude ? "Fade To Back Color" : "Gradient");
            if (gradient) Row(ref row, el, nameof(TextLayer.Color2), extrude ? "Back Color" : "Bottom Color");
            Row(ref row, el, nameof(TextLayer.Dilate), type == TextLayerType.Outline ? "Thickness" : "Dilate");
            Row(ref row, el, nameof(TextLayer.Softness), "Softness");

            switch (type)
            {
                case TextLayerType.Face:
                    Row(ref row, el, nameof(TextLayer.TintWithVertexColor), "Tint With Text Color");
                    Row(ref row, el, nameof(TextLayer.Texture), "Texture");
                    if (el.FindPropertyRelative(nameof(TextLayer.Texture)).objectReferenceValue != null)
                    {
                        Row(ref row, el, nameof(TextLayer.TextureTiling), "Tiling");
                        Row(ref row, el, nameof(TextLayer.TextureOffset), "Offset");
                        Row(ref row, el, nameof(TextLayer.TextureBlend), "Blend");
                    }

                    Row(ref row, el, nameof(TextLayer.GlossColor), "Gloss");
                    if (GlossOn(el))
                    {
                        Row(ref row, el, nameof(TextLayer.GlossPosition), "  Position");
                        Row(ref row, el, nameof(TextLayer.GlossSoftness), "  Softness");
                        Row(ref row, el, nameof(TextLayer.GlossCurve), "  Curve");
                        Row(ref row, el, nameof(TextLayer.GlossInset), "  Inset");
                    }
                    Row(ref row, el, nameof(TextLayer.InnerShadowColor), "Inner Shadow");
                    Row(ref row, el, nameof(TextLayer.InnerHighlightColor), "Inner Highlight");
                    if (InnerOn(el))
                    {
                        Row(ref row, el, nameof(TextLayer.InnerOffset), "  Offset (em)");
                        Row(ref row, el, nameof(TextLayer.InnerSoftness), "  Softness");
                    }
                    Row(ref row, el, nameof(TextLayer.ShineColor), "Shine (animator)");
                    if (ShineOn(el))
                    {
                        Row(ref row, el, nameof(TextLayer.ShineWidth), "  Width");
                        Row(ref row, el, nameof(TextLayer.ShineSoftness), "  Softness");
                        Row(ref row, el, nameof(TextLayer.ShineAngle), "  Angle");
                    }
                    break;
                case TextLayerType.Outline:
                case TextLayerType.Shadow:
                    Row(ref row, el, nameof(TextLayer.Offset), "Offset (em)");
                    break;
                case TextLayerType.Extrude:
                    Row(ref row, el, nameof(TextLayer.ExtrudeAngle), "Angle");
                    Row(ref row, el, nameof(TextLayer.ExtrudeDepth), "Depth (em)");
                    Row(ref row, el, nameof(TextLayer.ExtrudeSteps), "Steps");
                    break;
            }
            EditorGUI.indentLevel--;
        }

        private static bool GlossOn(SerializedProperty el) =>
            el.FindPropertyRelative(nameof(TextLayer.GlossColor)).colorValue.a > 0;

        private static bool ShineOn(SerializedProperty el) =>
            el.FindPropertyRelative(nameof(TextLayer.ShineColor)).colorValue.a > 0;

        private static bool InnerOn(SerializedProperty el) =>
            el.FindPropertyRelative(nameof(TextLayer.InnerShadowColor)).colorValue.a > 0 ||
            el.FindPropertyRelative(nameof(TextLayer.InnerHighlightColor)).colorValue.a > 0;

        private static void Row(ref Rect row, SerializedProperty el, string field, string label)
        {
            row.y += _line + _gap;
            var prop = el.FindPropertyRelative(field);
            EditorGUI.PropertyField(row, prop, new GUIContent(label, prop.tooltip));
        }
    }
}

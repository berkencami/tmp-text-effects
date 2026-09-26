using System;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace TMPTextEffects.Editor
{
    [CustomEditor(typeof(TextAnimator))]
    public class TextAnimatorEditor : UnityEditor.Editor
    {
        private static readonly (string label, Type type)[] _effectTypes =
        {
            ("Transition/Pop", typeof(PopEffect)),
            ("Transition/Fade", typeof(FadeEffect)),
            ("Transition/Slide", typeof(SlideEffect)),
            ("Transition/Rotate", typeof(RotateEffect)),
            ("Transition/Flash", typeof(FlashEffect)),
            ("Transition/Color Fade", typeof(ColorFadeEffect)),
            ("Loop/Wave", typeof(WaveEffect)),
            ("Loop/Shake", typeof(ShakeEffect)),
            ("Loop/Pulse", typeof(PulseEffect)),
            ("Loop/Rainbow", typeof(RainbowEffect)),
            ("Loop/Shine (LayeredText)", typeof(ShineEffect)),
        };

        private SerializedProperty _effects;
        private SerializedProperty _tagEffects;
        private ReorderableList _list;
        private ReorderableList _tagList;

        private void OnEnable()
        {
            _effects = serializedObject.FindProperty("_Effects");
            _list = new ReorderableList(serializedObject, _effects, true, true, true, true)
            {
                drawHeaderCallback = r => EditorGUI.LabelField(r, "Effects"),
                elementHeightCallback = i =>
                    EditorGUI.GetPropertyHeight(_effects.GetArrayElementAtIndex(i), true) + EditorGUIUtility.standardVerticalSpacing * 2,
                drawElementCallback = DrawElement,
                onAddDropdownCallback = (_, _) => ShowAddMenu(),
            };

            _tagEffects = serializedObject.FindProperty("_TagEffects");
            _tagList = new ReorderableList(serializedObject, _tagEffects, true, true, true, true)
            {
                drawHeaderCallback = r => EditorGUI.LabelField(r, "Tag Effects   <tag>text</tag>  ·  <pause=0.5>"),
                elementHeightCallback = TagElementHeight,
                drawElementCallback = DrawTagElement,
                onAddCallback = l =>
                {
                    int i = _tagEffects.arraySize;
                    _tagEffects.InsertArrayElementAtIndex(i);
                    var el = _tagEffects.GetArrayElementAtIndex(i);
                    el.FindPropertyRelative(nameof(TagEffect.Tag)).stringValue = "tag";
                    el.FindPropertyRelative(nameof(TagEffect.Effect)).managedReferenceValue = new WaveEffect();
                },
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var animator = (TextAnimator)target;

            DrawPlaybackBar(animator);
            EditorGUILayout.Space(4);

            EditorGUILayout.PropertyField(serializedObject.FindProperty("_Progress"));
            EditorGUILayout.Space(2);

            EditorGUILayout.LabelField("Timing", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_PlayOnEnable"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_Delay"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_CharDuration"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_Stagger"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_Order"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_SentencePause"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_CommaPause"));
            var loop = serializedObject.FindProperty("_Loop");
            EditorGUILayout.PropertyField(loop);
            if (loop.enumValueIndex != (int)LoopMode.Once)
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_LoopDelay"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_UnscaledTime"));
            EditorGUILayout.LabelField(" ", $"Total transition: {animator.Duration:0.00}s", EditorStyles.miniLabel);

            EditorGUILayout.Space(4);
            _list.DoLayoutList();
            _tagList.DoLayoutList();

            if (serializedObject.ApplyModifiedProperties()) animator.SetDirty();
        }

        private void DrawPlaybackBar(TextAnimator animator)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (Application.isPlaying)
                {
                    if (GUILayout.Button("▶ Play")) animator.Play();
                    if (GUILayout.Button("Hide")) animator.Hide();
                    if (GUILayout.Button("Complete")) animator.Complete();
                }
                else if (animator.IsPreviewing)
                {
                    if (GUILayout.Button("■ Stop Preview")) animator.StopPreview();
                }
                else
                {
                    if (GUILayout.Button("▶ Preview")) animator.StartPreview();
                }

                if (GUILayout.Button("Presets ▾", GUILayout.Width(80))) ShowPresetMenu(animator);
            }
        }

        private void DrawElement(Rect rect, int index, bool active, bool focused)
        {
            var el = _effects.GetArrayElementAtIndex(index);
            rect.y += EditorGUIUtility.standardVerticalSpacing;
            rect.x += 10;
            rect.width -= 10;

            string name = el.managedReferenceValue is TextEffect e ? e.DisplayName : "(missing)";
            EditorGUI.PropertyField(rect, el, new GUIContent(name), true);
        }

        private float TagElementHeight(int index)
        {
            var effect = _tagEffects.GetArrayElementAtIndex(index).FindPropertyRelative(nameof(TagEffect.Effect));
            float line = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            return line + (effect.managedReferenceValue != null ? EditorGUI.GetPropertyHeight(effect, true) : 0) + 4;
        }

        private void DrawTagElement(Rect rect, int index, bool active, bool focused)
        {
            var el = _tagEffects.GetArrayElementAtIndex(index);
            var tag = el.FindPropertyRelative(nameof(TagEffect.Tag));
            var effect = el.FindPropertyRelative(nameof(TagEffect.Effect));

            rect.y += 2;
            float h = EditorGUIUtility.singleLineHeight;
            var tagRect = new Rect(rect.x, rect.y, rect.width * 0.4f, h);
            var typeRect = new Rect(tagRect.xMax + 4, rect.y, rect.width - tagRect.width - 4, h);
            tag.stringValue = EditorGUI.TextField(tagRect, tag.stringValue);

            int current = Array.FindIndex(_effectTypes, t => effect.managedReferenceValue?.GetType() == t.type);
            string[] labels = Array.ConvertAll(_effectTypes, t => t.label);
            int chosen = EditorGUI.Popup(typeRect, current, labels);
            if (chosen != current && chosen >= 0)
                effect.managedReferenceValue = Activator.CreateInstance(_effectTypes[chosen].type);

            if (effect.managedReferenceValue == null) return;
            var body = new Rect(rect.x + 10, rect.y + h + EditorGUIUtility.standardVerticalSpacing, rect.width - 10,
                EditorGUI.GetPropertyHeight(effect, true));
            effect.isExpanded = true;
            EditorGUI.PropertyField(body, effect, new GUIContent("Settings"), true);
        }

        private void ShowAddMenu()
        {
            var menu = new GenericMenu();
            foreach (var (label, type) in _effectTypes)
            {
                var t = type;
                menu.AddItem(new GUIContent(label), false, () =>
                {
                    serializedObject.Update();
                    int i = _effects.arraySize;
                    _effects.InsertArrayElementAtIndex(i);
                    var el = _effects.GetArrayElementAtIndex(i);
                    el.managedReferenceValue = Activator.CreateInstance(t);
                    el.isExpanded = true;
                    serializedObject.ApplyModifiedProperties();
                    ((TextAnimator)target).SetDirty();
                });
            }
            menu.ShowAsContext();
        }

        private void ShowPresetMenu(TextAnimator animator)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Pop In"), false, () => ApplyPreset(animator, TextAnimatorPresets.PopIn));
            menu.AddItem(new GUIContent("Typewriter"), false, () => ApplyPreset(animator, TextAnimatorPresets.Typewriter));
            menu.AddItem(new GUIContent("Slide Up"), false, () => ApplyPreset(animator, TextAnimatorPresets.SlideUp));
            menu.AddItem(new GUIContent("Spin In"), false, () => ApplyPreset(animator, TextAnimatorPresets.SpinIn));
            menu.AddItem(new GUIContent("Wave (loop)"), false, () => ApplyPreset(animator, TextAnimatorPresets.Wave));
            menu.AddItem(new GUIContent("Shake (loop)"), false, () => ApplyPreset(animator, TextAnimatorPresets.Shake));
            menu.AddItem(new GUIContent("Rainbow (loop)"), false, () => ApplyPreset(animator, TextAnimatorPresets.Rainbow));
            menu.AddItem(new GUIContent("Add Shine (loop)"), false, () => ApplyPreset(animator, TextAnimatorPresets.AddShine));
            menu.ShowAsContext();
        }

        private static void ApplyPreset(TextAnimator animator, Action<TextAnimator> preset)
        {
            Undo.RecordObject(animator, "Apply Text Animation Preset");
            preset(animator);
            EditorUtility.SetDirty(animator);
            animator.SetDirty();
        }
    }
}

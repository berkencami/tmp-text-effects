using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEngine;

namespace TMPTextEffects.Tests
{
    /// <summary>Creates UI texts under a throwaway canvas and cleans everything up after each test.</summary>
    public abstract class TextTestFixture
    {
        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDownObjects()
        {
            foreach (var o in _created)
            {
                if (o != null) Object.DestroyImmediate(o);
            }
            _created.Clear();
        }

        protected T Track<T>(T obj) where T : Object
        {
            _created.Add(obj);
            return obj;
        }

        protected TextMeshProUGUI CreateText(string text)
        {
            var canvas = Track(new GameObject("Test Canvas", typeof(Canvas)));
            var go = new GameObject("Test Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(canvas.transform, false);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.rectTransform.sizeDelta = new Vector2(2000, 400);
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.text = text;
            return tmp;
        }

        protected TextAnimator AddAnimator(TextMeshProUGUI tmp, float charDuration, float stagger, StaggerOrder order)
        {
            var animator = tmp.gameObject.AddComponent<TextAnimator>();
            animator.PlayOnEnable = false;
            animator.CharDuration = charDuration;
            animator.Stagger = stagger;
            animator.Order = order;
            return animator;
        }

        protected static void Regenerate(TMP_Text tmp) => tmp.ForceMeshUpdate(true, true);

        protected LayeredTextStyle CreateStyle(params TextLayerType[] layers)
        {
            var style = Track(ScriptableObject.CreateInstance<LayeredTextStyle>());
            foreach (var type in layers) style.Layers.Add(TextLayer.Create(type));
            return style;
        }
    }
}

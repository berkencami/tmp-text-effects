using System;
using UnityEngine;

namespace TMPTextEffects
{
    public enum TextLayerType
    {
        /// <summary>The glyph itself: color / vertical gradient / texture, tinted by the TMP vertex color.</summary>
        Face,
        /// <summary>A dilated copy of the glyph (thickness via <see cref="TextLayer.dilate"/>).</summary>
        Outline,
        /// <summary>An offset, optionally blurred copy (drop shadow; zero offset + softness = glow).</summary>
        Shadow,
        /// <summary>The glyph swept along a direction — 2.5D depth.</summary>
        Extrude,
    }

    /// <summary>Where the vertical color gradient's 0..1 range is measured.</summary>
    public enum GradientSpace
    {
        Line,
        Character,
        WholeText,
    }

    [Serializable]
    public class TextLayer
    {
        public string Name = "Layer";
        public bool Enabled = true;
        public TextLayerType Type = TextLayerType.Outline;

        [Tooltip("Face/Outline/Shadow: top color. Extrude: front color.")]
        public Color Color = Color.white;

        [Tooltip("Use a second color: bottom color for fill layers, back color for Extrude.")]
        public bool UseGradient;

        [Tooltip("Face/Outline/Shadow: bottom color. Extrude: back color.")]
        public Color Color2 = Color.white;

        [Tooltip("Grows (+) or shrinks (-) the glyph shape. 1 = the font atlas padding, the maximum the SDF can express.")]
        [Range(-1f, 1f)] public float Dilate;

        [Tooltip("Edge blur. Use on shadows for a soft drop shadow or glow.")]
        [Range(0f, 1f)] public float Softness;

        [Tooltip("Offset in em (1 = font size). Face/Outline/Shadow.")]
        public Vector2 Offset;

        [Tooltip("Extrude direction in degrees (-90 = straight down).")]
        [Range(-180f, 180f)] public float ExtrudeAngle = -90f;

        [Tooltip("Extrude depth in em (1 = font size).")]
        [Min(0f)] public float ExtrudeDepth = 0.08f;

        [Tooltip("Samples along the extrude. More = smoother steep sides, costs fill rate.")]
        [Range(1, 32)] public int ExtrudeSteps = 12;

        [Tooltip("Face: multiply by the TMP vertex color (Color property, <color> tags, vertex gradient).")]
        public bool TintWithVertexColor = true;

        [Tooltip("Face: optional texture multiplied onto the face (only the first Face layer's texture is used).")]
        public Texture2D Texture;

        public Vector2 TextureTiling = Vector2.one;
        public Vector2 TextureOffset;

        [Range(0f, 1f)] public float TextureBlend = 1f;

        [Tooltip("Face: glossy highlight band over the upper part of the face. Alpha 0 = off.")]
        public Color GlossColor = new(1f, 1f, 1f, 0f);

        [Tooltip("Face: where the band's lower edge sits, 0 = bottom, 1 = top of the gradient range.")]
        [Range(0f, 1f)] public float GlossPosition = 0.55f;

        [Tooltip("Face: blur of the band's lower edge.")]
        [Range(0f, 0.5f)] public float GlossSoftness = 0.04f;

        [Tooltip("Face: bends the band's edge (+ dips at the sides, - bulges up at the sides).")]
        [Range(-1f, 1f)] public float GlossCurve = 0.25f;

        [Tooltip("Face: keeps the band away from the glyph edge (SDF units, like Dilate).")]
        [Range(0f, 1f)] public float GlossInset = 0.2f;

        [Tooltip("Face: darkened inner edge on the side away from the light. Alpha 0 = off.")]
        public Color InnerShadowColor = new(0f, 0f, 0f, 0f);

        [Tooltip("Face: brightened inner edge on the side facing the light. Alpha 0 = off. With an inner shadow = bevel.")]
        public Color InnerHighlightColor = new(1f, 1f, 1f, 0f);

        [Tooltip("Face: how far the inner shadow reaches in from the edge, in em; the direction is fixed on screen.")]
        public Vector2 InnerOffset = new(0.015f, -0.03f);

        [Tooltip("Face: blur of the inner shadow / highlight.")]
        [Range(0f, 1f)] public float InnerSoftness = 0.3f;

        [Tooltip("Face: colour of the shine band a TextAnimator Shine effect sweeps across the text. Alpha 0 = off.")]
        public Color ShineColor = new(1f, 1f, 1f, 0.85f);

        [Tooltip("Face: shine band width (fraction of the gradient box).")]
        [Range(0.01f, 1f)] public float ShineWidth = 0.18f;

        [Tooltip("Face: shine band edge blur.")]
        [Range(0f, 0.5f)] public float ShineSoftness = 0.06f;

        [Tooltip("Face: direction the shine travels, in degrees (0 = left to right; 30 = slanted).")]
        [Range(-180f, 180f)] public float ShineAngle = 25f;

        /// <summary>Extrude vector in em.</summary>
        public Vector2 ExtrudeVector
        {
            get
            {
                float a = ExtrudeAngle * Mathf.Deg2Rad;
                return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * ExtrudeDepth;
            }
        }

        public TextLayer Clone() => (TextLayer)MemberwiseClone();

        /// <summary>
        /// Writes the blend of <paramref name="a"/> → <paramref name="b"/> at <paramref name="t"/> into
        /// <paramref name="result"/>. Numbers and colours interpolate; switches (type, texture, flags) flip at the midpoint.
        /// A null side fades: the other layer with its alpha taken to zero.
        /// </summary>
        public static void Lerp(TextLayer a, TextLayer b, float t, TextLayer result)
        {
            if (a == null && b == null) return;
            if (a == null) { LerpToClear(b, 1 - t, result); return; }
            if (b == null) { LerpToClear(a, t, result); return; }

            var discrete = t < 0.5f ? a : b;
            result.Name = discrete.Name;
            result.Enabled = a.Enabled || b.Enabled;
            result.Type = discrete.Type;
            result.Texture = discrete.Texture;
            result.TintWithVertexColor = discrete.TintWithVertexColor;

            // Either side may use one colour; blend the effective top/bottom pair.
            result.UseGradient = a.UseGradient || b.UseGradient;
            result.Color = Color.Lerp(a.Color, b.Color, t);
            result.Color2 = Color.Lerp(a.UseGradient ? a.Color2 : a.Color, b.UseGradient ? b.Color2 : b.Color, t);

            float Visible(TextLayer l) => l.Enabled ? 1 : 0;
            float visibility = Mathf.Lerp(Visible(a), Visible(b), t);
            result.Color.a *= visibility;
            result.Color2.a *= visibility;

            result.Dilate = Mathf.Lerp(a.Dilate, b.Dilate, t);
            result.Softness = Mathf.Lerp(a.Softness, b.Softness, t);
            result.Offset = Vector2.Lerp(a.Offset, b.Offset, t);
            result.ExtrudeAngle = Mathf.LerpAngle(a.ExtrudeAngle, b.ExtrudeAngle, t);
            result.ExtrudeDepth = Mathf.Lerp(a.ExtrudeDepth, b.ExtrudeDepth, t);
            result.ExtrudeSteps = Mathf.RoundToInt(Mathf.Lerp(a.ExtrudeSteps, b.ExtrudeSteps, t));
            result.TextureTiling = Vector2.Lerp(a.TextureTiling, b.TextureTiling, t);
            result.TextureOffset = Vector2.Lerp(a.TextureOffset, b.TextureOffset, t);
            result.TextureBlend = Mathf.Lerp(a.TextureBlend, b.TextureBlend, t);
            result.GlossColor = Color.Lerp(a.GlossColor, b.GlossColor, t);
            result.GlossPosition = Mathf.Lerp(a.GlossPosition, b.GlossPosition, t);
            result.GlossSoftness = Mathf.Lerp(a.GlossSoftness, b.GlossSoftness, t);
            result.GlossCurve = Mathf.Lerp(a.GlossCurve, b.GlossCurve, t);
            result.GlossInset = Mathf.Lerp(a.GlossInset, b.GlossInset, t);
            result.InnerShadowColor = Color.Lerp(a.InnerShadowColor, b.InnerShadowColor, t);
            result.InnerHighlightColor = Color.Lerp(a.InnerHighlightColor, b.InnerHighlightColor, t);
            result.InnerOffset = Vector2.Lerp(a.InnerOffset, b.InnerOffset, t);
            result.InnerSoftness = Mathf.Lerp(a.InnerSoftness, b.InnerSoftness, t);
            result.ShineColor = Color.Lerp(a.ShineColor, b.ShineColor, t);
            result.ShineWidth = Mathf.Lerp(a.ShineWidth, b.ShineWidth, t);
            result.ShineSoftness = Mathf.Lerp(a.ShineSoftness, b.ShineSoftness, t);
            result.ShineAngle = Mathf.LerpAngle(a.ShineAngle, b.ShineAngle, t);
        }

        private static void LerpToClear(TextLayer layer, float t, TextLayer result)
        {
            Lerp(layer, layer, 0, result);
            float keep = 1 - t;
            result.Color.a *= keep;
            result.Color2.a *= keep;
            result.GlossColor.a *= keep;
            result.InnerShadowColor.a *= keep;
            result.InnerHighlightColor.a *= keep;
            result.ShineColor.a *= keep;
        }

        public static TextLayer Create(TextLayerType type)
        {
            var l = new TextLayer { Type = type, Name = type.ToString() };
            switch (type)
            {
                case TextLayerType.Face:
                    l.Color = Color.white;
                    break;
                case TextLayerType.Outline:
                    l.Color = new Color(0.1f, 0.1f, 0.15f);
                    l.Dilate = 0.35f;
                    break;
                case TextLayerType.Shadow:
                    l.Color = new Color(0f, 0f, 0f, 0.5f);
                    l.Dilate = 0.2f;
                    l.Softness = 0.3f;
                    l.Offset = new Vector2(0.03f, -0.05f);
                    break;
                case TextLayerType.Extrude:
                    l.Color = new Color(0.35f, 0.2f, 0.45f);
                    l.UseGradient = true;
                    l.Color2 = new Color(0.15f, 0.08f, 0.2f);
                    l.Dilate = 0.35f;
                    break;
            }
            return l;
        }
    }
}

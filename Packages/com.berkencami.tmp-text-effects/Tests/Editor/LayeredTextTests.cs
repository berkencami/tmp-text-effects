using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace TMPTextEffects.Tests
{
    public class LayeredTextTests : TextTestFixture
    {
        private static readonly FieldInfo _meshField =
            typeof(LayeredText).GetField("_mesh", BindingFlags.NonPublic | BindingFlags.Instance);

        private static Mesh LayeredMesh(LayeredText text) => (Mesh)_meshField.GetValue(text);

        private LayeredText CreateLayered(string content, LayeredTextStyle style)
        {
            var tmp = CreateText(content);
            var layered = tmp.gameObject.AddComponent<LayeredText>();
            layered.Style = style;
            Regenerate(tmp);
            return layered;
        }

        [Test]
        public void Mesh_HasOneQuadPerLayerPerVisibleGlyph()
        {
            var style = CreateStyle(TextLayerType.Shadow, TextLayerType.Outline, TextLayerType.Face);
            var layered = CreateLayered("ab c", style);

            Assert.That(LayeredMesh(layered).vertexCount, Is.EqualTo(3 /*layers*/ * 3 /*glyphs*/ * 4));
        }

        [Test]
        public void Mesh_IsLayerMajor_BackLayerFirst()
        {
            var style = CreateStyle(TextLayerType.Outline, TextLayerType.Face);
            var mesh = LayeredMesh(CreateLayered("ab", style));

            var uv3 = new System.Collections.Generic.List<Vector4>();
            mesh.GetUVs(3, uv3);
            // All glyphs of layer 0 (outline) come before any glyph of layer 1 (face).
            for (int v = 0; v < 8; v++) Assert.That(uv3[v].x, Is.EqualTo(0), $"vertex {v}");
            for (int v = 8; v < 16; v++) Assert.That(uv3[v].x, Is.EqualTo(1), $"vertex {v}");
        }

        [Test]
        public void DisabledLayers_AreSkipped()
        {
            var style = CreateStyle(TextLayerType.Outline, TextLayerType.Face);
            style.Layers[0].Enabled = false;
            var layered = CreateLayered("ab", style);

            Assert.That(LayeredMesh(layered).vertexCount, Is.EqualTo(1 * 2 * 4));
        }

        [Test]
        public void Underline_IsKeptAsExtraQuads()
        {
            var style = CreateStyle(TextLayerType.Face);
            var plain = LayeredMesh(CreateLayered("ab", style)).vertexCount;
            var underlined = LayeredMesh(CreateLayered("<u>ab</u>", style)).vertexCount;

            Assert.That(underlined, Is.GreaterThan(plain));
        }

        [Test]
        public void Highlight_IsDrawnOnceBehindAllLayers()
        {
            var style = CreateStyle(TextLayerType.Outline, TextLayerType.Face);
            var mesh = LayeredMesh(CreateLayered("<mark=#FF000080>ab</mark>", style));

            var uv3 = new System.Collections.Generic.List<Vector4>();
            mesh.GetUVs(3, uv3);
            Assert.That(uv3[0].x, Is.EqualTo(-1), "first quad is the solid highlight");
            int solid = 0;
            foreach (var u in uv3) if (u.x < -0.5f) solid++;
            Assert.That(solid, Is.EqualTo(4), "one highlight quad, not one per layer");
        }

        [Test]
        public void Disabling_RestoresTheFontMaterial()
        {
            var style = CreateStyle(TextLayerType.Face);
            var layered = CreateLayered("ab", style);
            var tmp = layered.Text;
            Assert.That(tmp.fontSharedMaterial.shader.name, Is.EqualTo(LayeredTextStyle.ShaderName));

            layered.enabled = false;
            Assert.That(tmp.fontSharedMaterial, Is.SameAs(tmp.font.material));
        }

        [Test]
        public void TextsSharingAStyleAndFont_ShareAMaterial()
        {
            var style = CreateStyle(TextLayerType.Face);
            var a = CreateLayered("ab", style);
            var b = CreateLayered("cd", style);

            Assert.That(a.Text.fontSharedMaterial, Is.SameAs(b.Text.fontSharedMaterial));
        }

        [Test]
        public void Blend_InterpolatesValuesAndFadesUnmatchedLayers()
        {
            var from = CreateStyle(TextLayerType.Outline, TextLayerType.Face);
            var to = CreateStyle(TextLayerType.Outline);
            from.Layers[0].Dilate = 0.2f;
            to.Layers[0].Dilate = 0.6f;

            var blend = CreateStyle();
            blend.Blend(from, to, 0.5f);

            Assert.That(blend.Layers.Count, Is.EqualTo(2));
            Assert.That(blend.Layers[0].Dilate, Is.EqualTo(0.4f).Within(1e-4));
            Assert.That(blend.Layers[1].Color.a, Is.EqualTo(from.Layers[1].Color.a * 0.5f).Within(1e-4));
        }

        [Test]
        public void TextLayerLerp_SwitchesDiscreteFieldsAtTheMidpoint()
        {
            var a = TextLayer.Create(TextLayerType.Outline);
            var b = TextLayer.Create(TextLayerType.Shadow);
            var result = new TextLayer();

            TextLayer.Lerp(a, b, 0.4f, result);
            Assert.That(result.Type, Is.EqualTo(TextLayerType.Outline));
            TextLayer.Lerp(a, b, 0.6f, result);
            Assert.That(result.Type, Is.EqualTo(TextLayerType.Shadow));
        }
    }
}

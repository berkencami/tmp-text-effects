using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace TMPTextEffects
{
    /// <summary>
    /// Renders a TMP text (UI TextMeshProUGUI or world-space TextMeshPro) with a <see cref="LayeredTextStyle"/>:
    /// after TMP builds its mesh, every glyph
    /// quad is duplicated once per layer in layer-major order (all shadows, then all extrudes, ... then all
    /// faces), so a layer never covers another letter's frontmost layer. One mesh, one material, one draw call.
    /// </summary>
    /// <remarks>
    /// Only the primary font mesh is layered; sub-meshes (fallback-font glyphs, &lt;sprite&gt;) render as usual.
    /// Code that pushes vertices itself via <c>TMP_Text.UpdateVertexData</c> bypasses the layering until the
    /// next regeneration.
    /// </remarks>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("UI/Layered Text")]
    public class LayeredText : MonoBehaviour
    {
        [SerializeField] private LayeredTextStyle _Style;

        [Tooltip("Bends the text along a circle. Degrees of arc spanned by the text's width: + bends up (rainbow), - bends down (smile).")]
        [SerializeField, Range(-360f, 360f)] private float _ArcAngle;

        private TMP_Text _tmp;
        private MeshFilter _meshFilter; // world-space TextMeshPro: where the layered mesh is shown
        private Mesh _mesh;

        // Style transition: a runtime style blended from _transitionFrom to _Style, shown while it runs.
        private LayeredTextStyle _blend;
        private LayeredTextStyle _transitionFrom;
        private float _transitionTime, _transitionDuration;
        private Ease _transitionEase;
        private bool _transitionUnscaled;
        private TMP_FontAsset _appliedFont;
        private Material _appliedMaterial;

        private readonly List<TextLayer> _layers = new();
        private readonly List<Vector3> _verts = new();
        private readonly List<Color32> _colors = new();
        private readonly List<Vector4> _uv0 = new();
        private readonly List<Vector2> _uv1 = new();
        private readonly List<Vector4> _uv2 = new();
        private readonly List<Vector4> _uv3 = new();
        private readonly List<Vector3> _normals = new();
        private readonly List<Vector4> _tangents = new();
        private readonly List<int> _tris = new();

        private const AdditionalCanvasShaderChannels RequiredChannels =
            AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2 |
            AdditionalCanvasShaderChannels.TexCoord3 | AdditionalCanvasShaderChannels.Normal |
            AdditionalCanvasShaderChannels.Tangent;

        public LayeredTextStyle Style
        {
            get => _Style;
            set
            {
                if (_Style == value) return;
                StopTransition();
                _Style = value;
                if (isActiveAndEnabled) Apply();
            }
        }

        /// <summary>Degrees of arc spanned by the text's width (+ up, - down, 0 = straight).</summary>
        public float ArcAngle
        {
            get => _ArcAngle;
            set
            {
                value = Mathf.Clamp(value, -360f, 360f);
                if (Mathf.Approximately(_ArcAngle, value)) return;
                _ArcAngle = value;
                if (isActiveAndEnabled) Text.SetVerticesDirty();
            }
        }

        public bool IsTransitioning => _transitionFrom != null;

        /// <summary>
        /// Smoothly changes to <paramref name="target"/>: colours, thicknesses, offsets, extrude, gloss… interpolate,
        /// layers pair up by index and unmatched ones fade. While it runs the text uses its own material (no batching).
        /// </summary>
        public void TransitionTo(LayeredTextStyle target, float duration, Ease ease = Ease.OutQuad, bool unscaledTime = true)
        {
            if (target == null || _Style == null || duration <= 0 || !isActiveAndEnabled)
            {
                StopTransition();
                Style = target;
                return;
            }
            if (target == _Style && !IsTransitioning) return;

            // Start from what is on screen: mid-transition that is the current blend.
            if (IsTransitioning)
            {
                var snapshot = CreateRuntimeStyle("LayeredText Snapshot");
                snapshot.Blend(_blend, _blend, 0);
                ReleaseTransitionFrom();
                _transitionFrom = snapshot;
            }
            else
            {
                _transitionFrom = _Style;
            }

            _Style = target;
            _transitionTime = 0;
            _transitionDuration = duration;
            _transitionEase = ease;
            _transitionUnscaled = unscaledTime;
            if (_blend == null) _blend = CreateRuntimeStyle("LayeredText Blend");
            _blend.Blend(_transitionFrom, _Style, 0);
            ApplyStyle(_blend);
        }

        private static LayeredTextStyle CreateRuntimeStyle(string name)
        {
            var style = ScriptableObject.CreateInstance<LayeredTextStyle>();
            style.name = name;
            style.hideFlags = HideFlags.DontSave;
            return style;
        }

        private void StepTransition(float dt)
        {
            _transitionTime += dt;
            float t = Mathf.Clamp01(_transitionTime / _transitionDuration);
            _blend.Blend(_transitionFrom, _Style, Easing.Evaluate(_transitionEase, t)); // re-meshes via Changed
            if (t >= 1) StopTransition();
        }

        private void StopTransition()
        {
            if (!IsTransitioning) return;
            ReleaseTransitionFrom();
            _transitionFrom = null;
            if (_Style != null && isActiveAndEnabled) ApplyStyle(_Style);
        }

        private void ReleaseTransitionFrom()
        {
            // Only snapshots are ours to destroy; a real style asset is just dropped.
            if (_transitionFrom != null && _transitionFrom.hideFlags == HideFlags.DontSave) DestroyStyle(_transitionFrom);
        }

        private static void DestroyStyle(LayeredTextStyle style)
        {
            style.ReleaseMaterials();
            if (Application.isPlaying) Destroy(style);
            else DestroyImmediate(style);
        }

        /// <summary>What renders right now: the transition blend while one runs, else the assigned style.</summary>
        private LayeredTextStyle ActiveStyle => IsTransitioning ? _blend : _Style;

        public TMP_Text Text => _tmp != null ? _tmp : _tmp = GetComponent<TMP_Text>();

        private void OnEnable()
        {
            if (Text == null)
            {
                Debug.LogWarning("[LayeredText] needs a TextMeshPro text (UI or 3D) on the same GameObject.", this);
                enabled = false;
                return;
            }
            TMPro_EventManager.TEXT_CHANGED_EVENT.Add(OnTextChanged);
            LayeredTextStyle.Changed += OnStyleChanged;
            Apply();
        }

        private void OnDisable()
        {
            TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(OnTextChanged);
            LayeredTextStyle.Changed -= OnStyleChanged;
            StopTransition();
            if (_blend != null) { DestroyStyle(_blend); _blend = null; }
            RestoreFontMaterial();
            RestoreTmpMesh();
            // Domain reloads call OnDisable (not OnDestroy): a HideAndDontSave mesh kept past here would leak.
            ReleaseMesh();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Inspector edits (style swap) — defer: TMP must not be dirtied from inside OnValidate.
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null && isActiveAndEnabled) Apply();
            };
        }
#endif

        private void LateUpdate()
        {
            if (IsTransitioning) StepTransition(_transitionUnscaled ? Time.unscaledDeltaTime : Time.deltaTime);
            // The user (or TMP itself) swapped the font: TMP resets to the font's default material.
            if (ActiveStyle != null && Text.font != _appliedFont) Apply();
        }

        private void OnCanvasHierarchyChanged() => EnsureCanvasChannels();

        /// <summary>Re-assigns the style material and forces TMP to regenerate (which re-layers the mesh).</summary>
        public void Apply() => ApplyStyle(ActiveStyle);

        private void ApplyStyle(LayeredTextStyle style)
        {
            var tmp = Text;
            if (tmp == null) return;

            if (style == null)
            {
                RestoreFontMaterial();
                return;
            }

            // Remember the font even on failure (e.g. shader missing): LateUpdate retries only when it changes.
            _appliedFont = tmp.font;
            var mat = style.GetMaterial(tmp.font);
            if (mat == null) return;

            _appliedMaterial = mat;
            if (tmp.fontSharedMaterial != mat) tmp.fontSharedMaterial = mat;
            EnsureCanvasChannels();
            tmp.SetVerticesDirty();
        }

        private void RestoreFontMaterial()
        {
            var tmp = Text;
            if (tmp == null || tmp.font == null) return;
            if (_appliedMaterial != null && tmp.fontSharedMaterial == _appliedMaterial)
                tmp.fontSharedMaterial = tmp.font.material;
            _appliedMaterial = null;
            _appliedFont = null;
            tmp.SetVerticesDirty();
        }

        private void EnsureCanvasChannels()
        {
            if (Text is not TextMeshProUGUI ugui) return; // world-space meshes carry every channel
            var canvas = ugui.canvas;
            if (canvas != null && (canvas.additionalShaderChannels & RequiredChannels) != RequiredChannels)
                canvas.additionalShaderChannels |= RequiredChannels;
        }

        private void OnStyleChanged(LayeredTextStyle style)
        {
            if (!isActiveAndEnabled || style != ActiveStyle) return;
            // A transition step only changes layer values: re-layer TMP's current geometry, no text regeneration.
            if (style == _blend) RefreshMesh();
            else Text.SetVerticesDirty();
        }

        private void OnTextChanged(Object obj)
        {
            if (obj != _tmp || ActiveStyle == null || !isActiveAndEnabled) return;
            if (_tmp.fontSharedMaterial != _appliedMaterial) return; // not ours (yet) — Apply() will follow
            // The animator may not have seen this regeneration yet (event order); its stagger slots must match.
            if (TryGetComponent(out TextAnimator animator) && animator.isActiveAndEnabled) animator.RebuildOrder();
            BuildLayeredMesh();
        }

        /// <summary>Rebuilds the layered mesh from TMP's current geometry without regenerating the text (per-frame animation).</summary>
        public void RefreshMesh()
        {
            if (ActiveStyle == null || !isActiveAndEnabled || Text.fontSharedMaterial != _appliedMaterial) return;
            BuildLayeredMesh();
        }

        /// <summary>Per-glyph data shared by every layer, computed once per build.</summary>
        private struct Glyph
        {
            public int VertexIndex;
            public Vector4 Rect;          // padded glyph rect in atlas uv
            public Vector2 LocalPerUV;
            public Vector2 UVPerLocal;
            public float Em;
            public Vector2 Pivot;         // baseline centre, unplaced
            public Vector2 PivotPlaced;   // after animation offset + arc
            public Vector2 Scale;
            public float Cos, Sin;        // total rotation (animation + arc)
            public bool Placed;
            public bool Animated;
            public byte AlphaMul;
            public float GradientBottom, GradientInv;
            public float GradientLeft, GradientInvX;
            public float Shine;           // sweep position, NaN = none
            public Color Tint;
            public float Flash;
        }

        private readonly List<Glyph> _glyphs = new();
        private readonly List<int> _decorations = new();
        private readonly List<int> _highlights = new();
        private bool[] _claimed = System.Array.Empty<bool>();

        private void BuildLayeredMesh()
        {
            var tmp = _tmp;
            var info = tmp.textInfo;
            var style = ActiveStyle;
            style.GetActiveLayers(_layers);

            _verts.Clear(); _colors.Clear(); _uv0.Clear(); _uv1.Clear();
            _uv2.Clear(); _uv3.Clear(); _normals.Clear(); _tangents.Clear(); _tris.Clear();

            EnsureMesh();

            if (info == null || info.characterCount == 0 || _layers.Count == 0 || info.meshInfo.Length == 0)
            {
                _mesh.Clear();
                PushMesh();
                return;
            }

            var mi = info.meshInfo[0];
            GetTextBounds(info, out float textTop, out float textBottom);
            GetTextFrame(info, out float textCenterX, out float textWidth, out _);
            float textLeft = textCenterX - textWidth * 0.5f;
            float textGradientInvX = 1f / Mathf.Max(1e-5f, textWidth);
            CollectGlyphs(info, mi, textTop, textBottom, textLeft, textWidth);
            CollectDecorations(info, mi);

            float texScale = 1f / Mathf.Max(1e-4f, TextUnits.Em(tmp));
            float textGradientInv = 1f / Mathf.Max(1e-5f, textTop - textBottom);

            // <mark> highlights: one solid quad in TMP's colour behind every layer, as TMP draws them.
            foreach (int vi in _highlights)
            {
                int baseIndex = _verts.Count;
                for (int k = 0; k < 4; k++)
                {
                    Vector3 p = mi.vertices[vi + k];
                    AddVertex(p, mi.uvs0[vi + k], mi.colors32[vi + k], Vector2.zero, Vector4.zero, new Vector4(-1, 0, 0, 0), 0);
                }
                AddQuad(baseIndex);
            }

            for (int li = 0; li < _layers.Count; li++)
            {
                var layer = _layers[li];
                bool isExtrude = layer.Type == TextLayerType.Extrude;
                Vector2 extrudeEm = isExtrude ? layer.ExtrudeVector : Vector2.zero;
                Vector2 innerEm = layer.Type == TextLayerType.Face ? layer.InnerOffset : Vector2.zero;
                Vector2 offsetEm = isExtrude ? Vector2.zero : layer.Offset;

                foreach (var g in _glyphs)
                {
                    // Shadow offsets and the extrude direction stay fixed on screen (one light direction), so the
                    // extrude vector is expressed in the glyph's own, possibly rotated, frame.
                    Vector2 e = extrudeEm * g.Em;
                    var extrude = new Vector2(e.x * g.Cos + e.y * g.Sin, -e.x * g.Sin + e.y * g.Cos);
                    Vector3 shift = Vector2.Scale(offsetEm * g.Em, g.Scale);
                    Vector2 extrudeUV = Vector2.Scale(extrude, g.UVPerLocal);
                    if (!isExtrude)
                    {
                        // Face: uv3.yz carries the inner shadow offset instead, fixed on screen like the extrude.
                        Vector2 i = innerEm * g.Em;
                        extrudeUV = Vector2.Scale(new Vector2(i.x * g.Cos + i.y * g.Sin, -i.x * g.Sin + i.y * g.Cos), g.UVPerLocal);
                    }

                    int baseIndex = _verts.Count;
                    for (int k = 0; k < 4; k++)
                    {
                        Vector3 p = mi.vertices[g.VertexIndex + k];
                        Vector4 u = mi.uvs0[g.VertexIndex + k];
                        bool left = k <= 1, bottom = k == 0 || k == 3; // TMP quad order: BL, TL, TR, BR

                        // Inflate the corner to the padded rect, moving position and UV together.
                        float tu = left ? g.Rect.x : g.Rect.z;
                        float tv = bottom ? g.Rect.y : g.Rect.w;
                        p.x += (tu - u.x) * g.LocalPerUV.x;
                        p.y += (tv - u.y) * g.LocalPerUV.y;
                        u.x = tu; u.y = tv;

                        // Extrude: grow the quad over the whole sweep. UV keeps mapping the FRONT copy; the shader
                        // samples back along the sweep from there.
                        if (extrude.x != 0 && extrude.x < 0 == left) { p.x += extrude.x; u.x += extrude.x * g.UVPerLocal.x; }
                        if (extrude.y != 0 && extrude.y < 0 == bottom) { p.y += extrude.y; u.y += extrude.y * g.UVPerLocal.y; }

                        float gy = (p.y - g.GradientBottom) * g.GradientInv;
                        float gx = (p.x - g.GradientLeft) * g.GradientInvX;

                        if (g.Placed)
                        {
                            float dx = (p.x - g.Pivot.x) * g.Scale.x, dy = (p.y - g.Pivot.y) * g.Scale.y;
                            p.x = g.PivotPlaced.x + dx * g.Cos - dy * g.Sin;
                            p.y = g.PivotPlaced.y + dx * g.Sin + dy * g.Cos;
                        }
                        p += shift;

                        var col = mi.colors32[g.VertexIndex + k];
                        if (g.Animated)
                        {
                            col = (Color)col * g.Tint;
                            col.a = (byte)(mi.colors32[g.VertexIndex + k].a * g.AlphaMul / 255);
                        }
                        AddVertex(p, u, col, new Vector2(p.x * texScale, p.y * texScale), g.Rect, new Vector4(li, extrudeUV.x, extrudeUV.y, gy), gx, g.Shine, g.Flash);
                    }
                    AddQuad(baseIndex);
                }

                // Underline / strikethrough / <mark> quads: not tied to a character, so no inflation, animation or
                // arc; they still get every layer (outline, shadow offset) within their own quad.
                Vector3 decorationShift = offsetEm * TextUnits.Em(tmp);
                foreach (int vi in _decorations)
                {
                    Vector4 rect = QuadUVRect(mi, vi);
                    int baseIndex = _verts.Count;
                    for (int k = 0; k < 4; k++)
                    {
                        Vector3 p = mi.vertices[vi + k];
                        float gy = (p.y - textBottom) * textGradientInv;
                        float gx = (p.x - textLeft) * textGradientInvX;
                        p += decorationShift;
                        AddVertex(p, mi.uvs0[vi + k], mi.colors32[vi + k], new Vector2(p.x * texScale, p.y * texScale), rect, new Vector4(li, 0, 0, gy), gx);
                    }
                    AddQuad(baseIndex);
                }
            }

            _mesh.Clear();
            _mesh.indexFormat = _verts.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            _mesh.SetVertices(_verts);
            _mesh.SetColors(_colors);
            _mesh.SetUVs(0, _uv0);
            _mesh.SetUVs(1, _uv1);
            _mesh.SetUVs(2, _uv2);
            _mesh.SetUVs(3, _uv3);
            _mesh.SetNormals(_normals);
            _mesh.SetTangents(_tangents);
            _mesh.SetTriangles(_tris, 0);
            _mesh.RecalculateBounds();

            PushMesh();
        }

        private void CollectGlyphs(TMP_TextInfo info, TMP_MeshInfo mi, float textTop, float textBottom, float textLeft, float textWidth)
        {
            _glyphs.Clear();

            // Arc: every glyph is placed rigidly on a circle (pivot = its baseline centre) and rotated to follow
            // it. Circle centre sits below (+) or above (-) the first baseline; later lines are concentric.
            bool arc = Mathf.Abs(_ArcAngle) > 0.01f;
            float arcRadius = 0, arcCenterX = 0, arcBaseline = 0;
            if (arc)
            {
                GetTextFrame(info, out arcCenterX, out float arcWidth, out arcBaseline);
                arcRadius = arcWidth / (_ArcAngle * Mathf.Deg2Rad);
                arc = Mathf.Abs(arcRadius) > 1e-3f && !float.IsInfinity(arcRadius);
            }

            TryGetComponent(out TextAnimator animator);
            if (animator != null && !animator.isActiveAndEnabled) animator = null;

            for (int c = 0; c < info.characterCount; c++)
            {
                ref var ci = ref info.characterInfo[c];
                if (!ci.isVisible || ci.materialReferenceIndex != 0 || ci.elementType != TMP_TextElementType.Character)
                    continue;

                var font = ci.fontAsset;
                if (font == null || ci.textElement == null) continue;

                int vi = ci.vertexIndex;
                if (vi + 3 >= mi.vertices.Length) continue;
                Vector3 pBL = mi.vertices[vi], pTL = mi.vertices[vi + 1], pBR = mi.vertices[vi + 3];
                Vector4 uBL = mi.uvs0[vi], uTL = mi.uvs0[vi + 1], uBR = mi.uvs0[vi + 3];

                // Collapsed quads (maxVisibleCharacters, page mode) have no extent: nothing to draw.
                float dxLocal = pBR.x - pBL.x, dyLocal = pTL.y - pBL.y;
                float dxUV = uBR.x - uBL.x, dyUV = uTL.y - uBL.y;
                if (Mathf.Abs(dxLocal) < 1e-6f || Mathf.Abs(dyLocal) < 1e-6f || Mathf.Abs(dxUV) < 1e-9f || Mathf.Abs(dyUV) < 1e-9f)
                    continue;

                var g = new Glyph { VertexIndex = vi, Em = TextUnits.Em(ci) };
                g.LocalPerUV = new Vector2(dxLocal / dxUV, dyLocal / dyUV);
                g.UVPerLocal = new Vector2(1f / g.LocalPerUV.x, 1f / g.LocalPerUV.y);

                // Padded glyph rect in atlas UV: the region the SDF is valid in. Every layer quad is inflated to it
                // (TMP's own quad may carry less padding than wide outlines need); the shader clamps to it.
                var gr = ci.textElement.glyph.glyphRect;
                float pad = font.atlasPadding;
                float aw = font.atlasWidth, ah = font.atlasHeight;
                g.Rect = new Vector4((gr.x - pad) / aw, (gr.y - pad) / ah, (gr.x + gr.width + pad) / aw, (gr.y + gr.height + pad) / ah);

                // Placement around the baseline centre: animation (scale, rotation, offset), then the arc.
                var anim = CharacterState.Identity;
                g.Animated = animator != null && animator.TryGetState(c, out anim);
                g.Pivot = new Vector2((ci.origin + ci.xAdvance) * 0.5f, ci.baseLine);
                g.PivotPlaced = g.Pivot + anim.Offset * g.Em;
                g.Scale = anim.Scale;
                float angle = anim.Rotation * Mathf.Deg2Rad;
                if (arc)
                {
                    float theta = (g.PivotPlaced.x - arcCenterX) / arcRadius;
                    float r = arcRadius + (g.PivotPlaced.y - arcBaseline);
                    g.PivotPlaced = new Vector2(arcCenterX + r * Mathf.Sin(theta), arcBaseline - arcRadius + r * Mathf.Cos(theta));
                    angle -= theta;
                }
                g.Placed = g.Animated || arc;
                g.Cos = Mathf.Cos(angle);
                g.Sin = Mathf.Sin(angle);
                g.AlphaMul = (byte)Mathf.RoundToInt(Mathf.Clamp01(anim.Alpha) * 255f);
                g.Shine = anim.Shine;
                g.Tint = anim.Tint;
                g.Flash = anim.Flash;

                // Gradient box for this glyph (vertical and horizontal 0..1 ranges).
                float gTop, gBottom, gLeft, gWidth;
                switch (ActiveStyle.GradientSpace)
                {
                    case GradientSpace.Character:
                        gTop = ci.ascender; gBottom = ci.descender;
                        gLeft = ci.origin; gWidth = ci.xAdvance - ci.origin;
                        break;
                    case GradientSpace.WholeText:
                        gTop = textTop; gBottom = textBottom;
                        gLeft = textLeft; gWidth = textWidth;
                        break;
                    default:
                        var line = info.lineInfo[ci.lineNumber];
                        gTop = line.ascender; gBottom = line.descender;
                        gLeft = line.lineExtents.min.x; gWidth = line.lineExtents.max.x - line.lineExtents.min.x;
                        break;
                }
                g.GradientBottom = gBottom;
                g.GradientInv = 1f / Mathf.Max(1e-5f, gTop - gBottom);
                g.GradientLeft = gLeft;
                g.GradientInvX = 1f / Mathf.Max(1e-5f, gWidth);

                _glyphs.Add(g);
            }
        }

        /// <summary>
        /// Quads in the primary mesh that no character owns: underline and strikethrough (SDF segments of the
        /// underline glyph) and &lt;mark&gt; highlights (no SDF scale in uv0.w, drawn solid).
        /// </summary>
        private void CollectDecorations(TMP_TextInfo info, TMP_MeshInfo mi)
        {
            _decorations.Clear();
            _highlights.Clear();
            int vertexCount = Mathf.Min(mi.vertexCount, mi.vertices.Length);
            if (_claimed.Length < vertexCount) _claimed = new bool[vertexCount];
            System.Array.Clear(_claimed, 0, vertexCount);

            for (int c = 0; c < info.characterCount; c++)
            {
                ref var ci = ref info.characterInfo[c];
                if (ci.materialReferenceIndex == 0 && ci.vertexIndex >= 0 && ci.vertexIndex < vertexCount) _claimed[ci.vertexIndex] = true;
            }

            for (int vi = 0; vi + 3 < vertexCount; vi += 4)
            {
                if (_claimed[vi]) continue;
                if ((mi.vertices[vi + 2] - mi.vertices[vi]).sqrMagnitude < 1e-8f) continue; // unused / collapsed slot
                if (Mathf.Abs(mi.uvs0[vi].w) < 1e-6f) _highlights.Add(vi);
                else _decorations.Add(vi);
            }
        }

        private static Vector4 QuadUVRect(TMP_MeshInfo mi, int vi)
        {
            Vector2 min = mi.uvs0[vi], max = min;
            for (int k = 1; k < 4; k++)
            {
                Vector2 u = mi.uvs0[vi + k];
                min = Vector2.Min(min, u);
                max = Vector2.Max(max, u);
            }
            return new Vector4(min.x, min.y, max.x, max.y);
        }

        private void AddVertex(Vector3 position, Vector4 uv0, Color32 color, Vector2 uv1, Vector4 uv2, Vector4 uv3,
            float gradientX, float shine = float.NaN, float flash = 0)
        {
            // tangent: x = horizontal gradient position, y = shine position, z = 1 when the shine is on, w = flash.
            bool shineOn = !float.IsNaN(shine);
            _tangents.Add(new Vector4(gradientX, shineOn ? shine : 0, shineOn ? 1 : 0, flash));
            _verts.Add(position);
            _uv0.Add(uv0);
            _colors.Add(color);
            _uv1.Add(uv1);
            _uv2.Add(uv2);
            _uv3.Add(uv3);
            _normals.Add(new Vector3(0, 0, -1));
        }

        private void AddQuad(int baseIndex)
        {
            _tris.Add(baseIndex); _tris.Add(baseIndex + 1); _tris.Add(baseIndex + 2);
            _tris.Add(baseIndex + 2); _tris.Add(baseIndex + 3); _tris.Add(baseIndex);
        }

        /// <summary>Shows the layered mesh: CanvasRenderer for UI text, the MeshFilter for world-space text.</summary>
        private void PushMesh()
        {
            if (_tmp is TextMeshProUGUI ugui)
            {
                ugui.canvasRenderer.SetMesh(_mesh);
                return;
            }
            if (_meshFilter == null) TryGetComponent(out _meshFilter);
            if (_meshFilter != null && _meshFilter.sharedMesh != _mesh) _meshFilter.sharedMesh = _mesh;
        }

        /// <summary>World-space text: give the MeshFilter back TMP's own mesh.</summary>
        private void RestoreTmpMesh()
        {
            if (_tmp is TextMeshProUGUI || _meshFilter == null || _tmp == null) return;
            if (_meshFilter.sharedMesh == _mesh) _meshFilter.sharedMesh = _tmp.mesh;
        }

        private void EnsureMesh()
        {
            if (_mesh != null) return;
            _mesh = new Mesh { name = "LayeredText", hideFlags = HideFlags.HideAndDontSave };
            _mesh.MarkDynamic();
        }

        private void ReleaseMesh()
        {
            if (_mesh == null) return;
            if (Application.isPlaying) Destroy(_mesh);
            else DestroyImmediate(_mesh);
            _mesh = null;
        }

        private static void GetTextFrame(TMP_TextInfo info, out float centerX, out float width, out float baseline)
        {
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < info.lineCount; i++)
            {
                var ext = info.lineInfo[i].lineExtents;
                if (ext.max.x < ext.min.x) continue;
                min = Mathf.Min(min, ext.min.x);
                max = Mathf.Max(max, ext.max.x);
            }
            if (min > max) { min = max = 0; }
            centerX = (min + max) * 0.5f;
            width = max - min;
            baseline = info.lineCount > 0 ? info.lineInfo[0].baseline : 0;
        }

        private static void GetTextBounds(TMP_TextInfo info, out float top, out float bottom)
        {
            top = float.MinValue; bottom = float.MaxValue;
            for (int i = 0; i < info.lineCount; i++)
            {
                top = Mathf.Max(top, info.lineInfo[i].ascender);
                bottom = Mathf.Min(bottom, info.lineInfo[i].descender);
            }
            if (top < bottom) { top = 1; bottom = 0; }
        }
    }
}

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace TMPTextEffects
{
    /// <summary>
    /// A reusable stack of text layers. Owns one material per font atlas (stored as sub-assets), with
    /// every layer's parameters written into material properties — texts sharing a style and font share
    /// a material, so they batch.
    /// </summary>
    [CreateAssetMenu(menuName = "Layered Text/Style", fileName = "New Layered Text Style")]
    public class LayeredTextStyle : ScriptableObject
    {
        public const int MaxLayers = 8;
        public const string ShaderName = "TMP Text Effects/Layered SDF";

        // Shader-side layer kinds (Outline and Shadow render identically; they differ only in defaults/geometry).
        private const float KindFill = 0, KindFace = 1, KindExtrude = 2;

        [Tooltip("Drawn back to front: the first layer is the backmost.")]
        public List<TextLayer> Layers = new();

        public GradientSpace GradientSpace = GradientSpace.Line;

        [Tooltip("Direction of every layer's two-colour gradient: 0 = vertical (top colour on top), 90 = horizontal (top colour on the right).")]
        [Range(-180f, 180f)] public float GradientAngle;

        [SerializeField, HideInInspector] private Shader _Shader;
        [SerializeField, HideInInspector] private List<FontMaterial> _Materials = new();

        [Serializable]
        private class FontMaterial
        {
            public TMP_FontAsset Font;
            public Material Material;
        }

        /// <summary>Raised after any edit to a style (inspector, undo, code calling <see cref="NotifyChanged"/>).</summary>
        public static event Action<LayeredTextStyle> Changed;

        private static readonly int[] _idA = new int[MaxLayers], _idB = new int[MaxLayers],
            _idP = new int[MaxLayers], _idV = new int[MaxLayers];
        private static readonly int _idCount = Shader.PropertyToID("_LayerCount");
        private static readonly int _idFaceTex = Shader.PropertyToID("_FaceTex");
        private static readonly int _idFaceTexST = Shader.PropertyToID("_FaceTex_ST");
        private static readonly int _idGradientDir = Shader.PropertyToID("_GradientDir");
        private static readonly int _idGlossColor = Shader.PropertyToID("_GlossColor");
        private static readonly int _idGlossParams = Shader.PropertyToID("_GlossParams");
        private static readonly int _idInnerShadowColor = Shader.PropertyToID("_InnerShadowColor");
        private static readonly int _idInnerHighlightColor = Shader.PropertyToID("_InnerHighlightColor");
        private static readonly int _idInnerParams = Shader.PropertyToID("_InnerParams");
        private static readonly int _idShineColor = Shader.PropertyToID("_ShineColor");
        private static readonly int _idShineParams = Shader.PropertyToID("_ShineParams");

        static LayeredTextStyle()
        {
            for (int i = 0; i < MaxLayers; i++)
            {
                _idA[i] = Shader.PropertyToID($"_L{i}A");
                _idB[i] = Shader.PropertyToID($"_L{i}B");
                _idP[i] = Shader.PropertyToID($"_L{i}P");
                _idV[i] = Shader.PropertyToID($"_L{i}V");
            }
        }

        /// <summary>The layers that actually render, in draw order. Mesh and material both index into this.</summary>
        public void GetActiveLayers(List<TextLayer> result)
        {
            result.Clear();
            foreach (var l in Layers)
            {
                if (l == null || !l.Enabled) continue;
                result.Add(l);
                if (result.Count == MaxLayers) break;
            }
        }

        public Material GetMaterial(TMP_FontAsset font)
        {
            if (font == null) return null;
            _Materials.RemoveAll(m => m == null || m.Font == null || m.Material == null);

            foreach (var fm in _Materials)
            {
                if (fm.Font != font) continue;
                Persist(fm.Material);
                return fm.Material;
            }

            var shader = ResolveShader();
            if (shader == null)
            {
                Debug.LogError($"[TMP Text Effects] Shader '{ShaderName}' not found.", this);
                return null;
            }

            var mat = new Material(shader) { name = $"{font.name} ({name})" };
            _Materials.Add(new FontMaterial { Font = font, Material = mat });
            ApplyTo(mat, font);
            Persist(mat);
            return mat;
        }

        /// <summary>
        /// Stores a generated material as a sub-asset of this style, so scenes reference an asset rather than
        /// embedding the material. Must run before anything saves a scene using it; deferred only while importing.
        /// Styles that are not assets (created at runtime) or can't be written (an immutable package, a locked file)
        /// keep a transient material, which <see cref="LayeredText"/> re-assigns whenever it is enabled.
        /// </summary>
        private void Persist(Material mat)
        {
#if UNITY_EDITOR
            if (UnityEditor.AssetDatabase.Contains(this) && IsWritableAsset())
            {
                if (UnityEditor.AssetDatabase.Contains(mat)) return;

                void Add()
                {
                    if (this == null || mat == null || UnityEditor.AssetDatabase.Contains(mat)) return;
                    mat.hideFlags = HideFlags.None;
                    UnityEditor.AssetDatabase.AddObjectToAsset(mat, this);
                    UnityEditor.EditorUtility.SetDirty(this);
                    UnityEditor.AssetDatabase.SaveAssetIfDirty(this);
                }

                if (UnityEditor.EditorApplication.isUpdating) UnityEditor.EditorApplication.delayCall += Add;
                else Add();
                return;
            }
#endif
            mat.hideFlags = HideFlags.DontSave;
        }

        /// <summary>
        /// Makes this (runtime) style the blend of <paramref name="a"/> → <paramref name="b"/> at <paramref name="t"/>.
        /// Layers pair up by index; unmatched layers fade in / out. Used for style transitions.
        /// </summary>
        public void Blend(LayeredTextStyle a, LayeredTextStyle b, float t)
        {
            int count = Mathf.Max(a.Layers.Count, b.Layers.Count);
            while (Layers.Count < count) Layers.Add(new TextLayer());
            if (Layers.Count > count) Layers.RemoveRange(count, Layers.Count - count);

            for (int i = 0; i < count; i++)
            {
                var la = i < a.Layers.Count ? a.Layers[i] : null;
                var lb = i < b.Layers.Count ? b.Layers[i] : null;
                TextLayer.Lerp(la, lb, t, Layers[i]);
            }
            GradientSpace = t < 0.5f ? a.GradientSpace : b.GradientSpace;
            GradientAngle = Mathf.LerpAngle(a.GradientAngle, b.GradientAngle, t);
            NotifyChanged();
        }

        /// <summary>Destroys the materials of a runtime (non-asset) style.</summary>
        public void ReleaseMaterials()
        {
            foreach (var fm in _Materials)
            {
                if (fm?.Material == null) continue;
#if UNITY_EDITOR
                if (UnityEditor.AssetDatabase.Contains(fm.Material)) continue;
#endif
                if (Application.isPlaying) Destroy(fm.Material);
                else DestroyImmediate(fm.Material);
            }
            _Materials.Clear();
        }

#if UNITY_EDITOR
        private bool IsWritableAsset()
        {
            string path = UnityEditor.AssetDatabase.GetAssetPath(this);
            if (!UnityEditor.AssetDatabase.IsOpenForEdit(path)) return false;
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(path);
            return package == null || package.source is UnityEditor.PackageManager.PackageSource.Embedded
                or UnityEditor.PackageManager.PackageSource.Local;
        }

        /// <summary>Editor only: deletes the generated per-font materials (sub-assets included) so they regenerate.</summary>
        public void DeleteGeneratedMaterials()
        {
            foreach (var fm in _Materials)
            {
                if (fm?.Material != null) DestroyImmediate(fm.Material, true);
            }
            _Materials.Clear();
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif

        public void NotifyChanged()
        {
            RefreshMaterials();
            Changed?.Invoke(this);
        }

        private void OnEnable() => RefreshMaterials();

        private void OnValidate() => NotifyChanged();

        private Shader ResolveShader()
        {
            if (_Shader == null) _Shader = Shader.Find(ShaderName);
            return _Shader;
        }

        private void RefreshMaterials()
        {
            foreach (var fm in _Materials)
            {
                if (fm?.Material != null && fm.Font != null) ApplyTo(fm.Material, fm.Font);
            }
        }

        private static readonly List<TextLayer> _active = new();

        private void ApplyTo(Material mat, TMP_FontAsset font)
        {
            var shader = ResolveShader();
            if (shader != null && mat.shader != shader) mat.shader = shader;

            // The font atlas + the SDF constants TMP's own shaders read from the font material.
            mat.SetTexture(ShaderUtilities.ID_MainTex, font.atlasTexture);
            mat.SetFloat(ShaderUtilities.ID_GradientScale, font.atlasPadding + 1);
            mat.SetFloat(ShaderUtilities.ID_TextureWidth, font.atlasWidth);
            mat.SetFloat(ShaderUtilities.ID_TextureHeight, font.atlasHeight);
            mat.SetFloat(ShaderUtilities.ID_WeightNormal, font.normalStyle);
            mat.SetFloat(ShaderUtilities.ID_WeightBold, font.boldStyle);

            GetActiveLayers(_active);
            mat.SetFloat(_idCount, _active.Count);

            Texture faceTex = null;
            Vector4 faceST = new(1, 1, 0, 0);

            // Gloss and inner shadow/highlight belong to the first Face layer (one set per material).
            TextLayer primaryFace = _active.Find(l => l.Type == TextLayerType.Face);
            float angle = GradientAngle * Mathf.Deg2Rad;
            mat.SetVector(_idGradientDir, new Vector4(Mathf.Sin(angle), Mathf.Cos(angle), 0, 0));
            mat.SetColor(_idGlossColor, primaryFace?.GlossColor ?? Color.clear);
            mat.SetVector(_idGlossParams, primaryFace == null ? Vector4.zero
                : new Vector4(primaryFace.GlossPosition, primaryFace.GlossSoftness, primaryFace.GlossCurve, primaryFace.GlossInset));
            mat.SetColor(_idInnerShadowColor, primaryFace?.InnerShadowColor ?? Color.clear);
            mat.SetColor(_idInnerHighlightColor, primaryFace?.InnerHighlightColor ?? Color.clear);
            mat.SetVector(_idInnerParams, new Vector4(primaryFace?.InnerSoftness ?? 0, 0, 0, 0));
            float shineAngle = (primaryFace?.ShineAngle ?? 0) * Mathf.Deg2Rad;
            mat.SetColor(_idShineColor, primaryFace?.ShineColor ?? Color.clear);
            mat.SetVector(_idShineParams, new Vector4(Mathf.Cos(shineAngle), Mathf.Sin(shineAngle),
                primaryFace?.ShineWidth ?? 0, primaryFace?.ShineSoftness ?? 0));

            for (int i = 0; i < MaxLayers; i++)
            {
                if (i >= _active.Count)
                {
                    mat.SetColor(_idA[i], Color.clear);
                    mat.SetColor(_idB[i], Color.clear);
                    mat.SetVector(_idP[i], Vector4.zero);
                    mat.SetVector(_idV[i], Vector4.zero);
                    continue;
                }

                var l = _active[i];
                float kind = l.Type switch
                {
                    TextLayerType.Face => KindFace,
                    TextLayerType.Extrude => KindExtrude,
                    _ => KindFill,
                };

                mat.SetColor(_idA[i], l.Color);
                mat.SetColor(_idB[i], l.UseGradient ? l.Color2 : l.Color);
                mat.SetVector(_idP[i], new Vector4(kind, l.Dilate, l.Softness, l.Type == TextLayerType.Extrude ? l.ExtrudeSteps : 0));

                float texBlend = 0;
                if (l.Type == TextLayerType.Face && l.Texture != null)
                {
                    if (faceTex == null)
                    {
                        faceTex = l.Texture;
                        faceST = new Vector4(l.TextureTiling.x, l.TextureTiling.y, l.TextureOffset.x, l.TextureOffset.y);
                    }
                    if (faceTex == l.Texture) texBlend = l.TextureBlend;
                }
                // V.x flags the layer that carries the face extras; the extrude vector is baked into the mesh.
                mat.SetVector(_idV[i], new Vector4(l == primaryFace ? 1 : 0, 0, texBlend, l.TintWithVertexColor ? 1 : 0));
            }

            mat.SetTexture(_idFaceTex, faceTex != null ? faceTex : Texture2D.whiteTexture);
            mat.SetVector(_idFaceTexST, faceST);
        }
    }
}

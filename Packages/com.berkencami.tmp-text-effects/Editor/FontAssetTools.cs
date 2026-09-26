using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace TMPTextEffects.Editor
{
    /// <summary>
    /// Creates TMP font assets suited to layered text: a large atlas padding (thick stacked outlines and glows can't
    /// grow past it) and a single atlas (glyphs on a second atlas land in a sub-mesh, which is not layered).
    /// </summary>
    public static class FontAssetTools
    {
        /// <summary>ASCII, Latin-1 and common extras (Turkish, typographic quotes, dashes, ellipsis, euro).</summary>
        public const string LatinCharacters =
            " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~" +
            "¡¢£¤¥¦§¨©ª«¬®¯°±²³´µ¶·¸¹º»¼½¾¿ÀÁÂÃÄÅÆÇÈÉÊËÌÍÎÏÐÑÒÓÔÕÖ×ØÙÚÛÜÝÞßàáâãäåæçèéêëìíîïðñòóôõö÷øùúûüýþÿ" +
            "ĞğİıŞş‘’“”–—…€";

        private const string MenuPath = "Tools/TMP Text Effects/Create Wide-Padding Font Asset";

        [MenuItem(MenuPath, true)]
        private static bool CanCreateFromSelection() => Selection.activeObject is Font;

        [MenuItem(MenuPath)]
        private static void CreateFromSelection()
        {
            var source = (Font)Selection.activeObject;
            string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(source))?.Replace('\\', '/');
            if (string.IsNullOrEmpty(folder) || !folder.StartsWith("Assets")) folder = "Assets";

            string path = EditorUtility.SaveFilePanelInProject("Create Wide-Padding Font Asset", $"{source.name} SDF Wide",
                "asset", "Where should the font asset be saved?", folder);
            if (string.IsNullOrEmpty(path)) return;

            var asset = Create(source, path);
            if (asset != null) EditorGUIUtility.PingObject(asset);
        }

        /// <summary>
        /// Creates a static SDF font asset at <paramref name="path"/> with <paramref name="characters"/> baked in.
        /// The default padding is ~20% of the sampling size.
        /// </summary>
        public static TMP_FontAsset Create(Font source, string path, int samplingPointSize = 72, int padding = 14,
            int atlasSize = 2048, string characters = LatinCharacters)
        {
            var asset = TMP_FontAsset.CreateFontAsset(source, samplingPointSize, padding, GlyphRenderMode.SDFAA,
                atlasSize, atlasSize, AtlasPopulationMode.Dynamic, enableMultiAtlasSupport: false);
            if (asset == null)
            {
                Debug.LogError($"[TMP Text Effects] Could not create a font asset from '{source.name}'.");
                return null;
            }

            asset.name = Path.GetFileNameWithoutExtension(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            AssetDatabase.CreateAsset(asset, path);
            asset.atlasTexture.name = asset.name + " Atlas";
            AssetDatabase.AddObjectToAsset(asset.atlasTexture, asset);
            asset.material.name = asset.name + " Material";
            AssetDatabase.AddObjectToAsset(asset.material, asset);

            if (!asset.TryAddCharacters(characters, out string missing) && !string.IsNullOrEmpty(missing))
                Debug.LogWarning($"[TMP Text Effects] '{source.name}' has no glyphs for: {missing}", asset);

            // Baked: nothing is added at runtime, so the asset also works from a read-only package.
            asset.atlasPopulationMode = AtlasPopulationMode.Static;
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            return asset;
        }
    }
}

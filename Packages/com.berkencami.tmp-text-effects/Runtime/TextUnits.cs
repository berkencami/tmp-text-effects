using TMPro;
using UnityEngine;

namespace TMPTextEffects
{
    /// <summary>
    /// Em size (1 = font size) in the text's local units. UI text measures in font-size units; world-space
    /// TextMeshPro scales by 0.1 (non-orthographic), so offsets given in em must go through here, not pointSize.
    /// </summary>
    internal static class TextUnits
    {
        /// <summary>Em of one character, from TMP's own element scale (includes &lt;size&gt;, auto-size, 3D scale).</summary>
        public static float Em(in TMP_CharacterInfo ci)
        {
            var face = ci.fontAsset != null ? ci.fontAsset.faceInfo : default;
            if (face.pointSize <= 0 || face.scale <= 0) return ci.pointSize;
            return ci.scale * face.pointSize / face.scale;
        }

        /// <summary>Em of the text's base font size.</summary>
        public static float Em(TMP_Text text) =>
            text.fontSize * (text is TextMeshProUGUI || text.isOrthographic ? 1f : 0.1f);
    }
}

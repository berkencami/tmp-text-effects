using TMPro;
using UnityEditor;
using UnityEngine;

namespace TMPTextEffects.Editor
{
    internal static class LayeredTextMenu
    {
        private const string DefaultStylePath = "Packages/com.berkencami.tmp-text-effects/Presets/Candy Title.asset";

        [MenuItem("GameObject/UI (Canvas)/Layered Text - TextMeshPro", false, 2001)]
        private static void CreateLayeredText(MenuCommand command)
        {
            // Unity invokes a hierarchy-context MenuItem once per selected object; create a single text.
            var selection = Selection.gameObjects;
            if (selection.Length > 1 && command.context != selection[0]) return;

            // Reuse TMP's own creation path (Canvas/EventSystem setup, parenting to the selection, undo).
            Selection.activeObject = command.context;
            if (!EditorApplication.ExecuteMenuItem("GameObject/UI (Canvas)/Text - TextMeshPro")) return;

            var go = Selection.activeGameObject;
            if (go == null || !go.TryGetComponent(out TextMeshProUGUI tmp)) return;

            go.name = "Layered Text";
            tmp.text = "Layered Text";
            tmp.fontSize = 72;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.rectTransform.sizeDelta = new Vector2(700, 160);

            var layered = Undo.AddComponent<LayeredText>(go);
            layered.Style = AssetDatabase.LoadAssetAtPath<LayeredTextStyle>(DefaultStylePath);
        }

        [MenuItem("GameObject/3D Object/Layered Text - TextMeshPro", false, 31)]
        private static void CreateLayeredText3D(MenuCommand command)
        {
            var selection = Selection.gameObjects;
            if (selection.Length > 1 && command.context != selection[0]) return;

            Selection.activeObject = command.context;
            if (!EditorApplication.ExecuteMenuItem("GameObject/3D Object/Text - TextMeshPro")) return;

            var go = Selection.activeGameObject;
            if (go == null || !go.TryGetComponent(out TextMeshPro tmp)) return;

            go.name = "Layered Text";
            tmp.text = "Layered Text";
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;

            var layered = Undo.AddComponent<LayeredText>(go);
            layered.Style = AssetDatabase.LoadAssetAtPath<LayeredTextStyle>(DefaultStylePath);
        }

        [MenuItem("CONTEXT/TextMeshProUGUI/Add Layered Text")]
        [MenuItem("CONTEXT/TextMeshPro/Add Layered Text")]
        private static void AddToExisting(MenuCommand command)
        {
            var tmp = (TMP_Text)command.context;
            if (tmp.GetComponent<LayeredText>() != null) return;
            var layered = Undo.AddComponent<LayeredText>(tmp.gameObject);
            layered.Style = AssetDatabase.LoadAssetAtPath<LayeredTextStyle>(DefaultStylePath);
        }
    }
}

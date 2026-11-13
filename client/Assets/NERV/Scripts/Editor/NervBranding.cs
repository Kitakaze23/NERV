using UnityEditor;
using UnityEngine;

namespace Nerv.Client.Editor
{
    /// <summary>
    /// Фирменные настройки Mendal: имя продукта, иконка приложения и импорт картинок знака
    /// (прозрачность без тёмной каймы, без сжатия). Выполняется при загрузке редактора, только если что-то не так.
    /// </summary>
    [InitializeOnLoad]
    internal static class NervBranding
    {
        private const string ProductName = "Mendal";
        private const string IconPath = "Assets/NERV/Branding/MendalIcon.png";
        private static readonly string[] MarkPaths =
        {
            "Assets/NERV/Resources/Textures/MendalMark.png",
            "Assets/NERV/Resources/Textures/MendalMarkFlat.png",
        };

        static NervBranding() => EditorApplication.delayCall += Apply;

        [MenuItem("NERV/Применить фирменный стиль Mendal")]
        private static void Apply()
        {
            foreach (string path in MarkPaths) SetupTexture(path, true);
            SetupTexture(IconPath, false);

            if (PlayerSettings.productName != ProductName) PlayerSettings.productName = ProductName;

            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            Texture2D[] current = PlayerSettings.GetIcons(UnityEditor.Build.NamedBuildTarget.Unknown, IconKind.Any);
            if (icon != null && (current.Length == 0 || current[0] != icon))
                PlayerSettings.SetIcons(UnityEditor.Build.NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
        }

        private static void SetupTexture(string path, bool transparent)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) return;
            bool changed = false;
            if (ti.alphaIsTransparency != transparent) { ti.alphaIsTransparency = transparent; changed = true; }
            if (ti.textureCompression != TextureImporterCompression.Uncompressed)
            {
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                changed = true;
            }
            if (!ti.mipmapEnabled) { ti.mipmapEnabled = true; changed = true; }
            if (changed) ti.SaveAndReimport();
        }
    }
}

using System.IO;
using UnityEditor;
using UnityEngine;

namespace Nerv.Client.Editor
{
    /// <summary>
    /// Копирует химические данные из core/data (единственный источник) в Resources клиента
    /// при загрузке редактора и по команде меню. Расширения меняются на .txt — так Unity читает их как TextAsset.
    /// </summary>
    [InitializeOnLoad]
    internal static class NervDataSync
    {
        private static readonly (string from, string to)[] Files =
        {
            ("compounds.tsv", "compounds.txt"),
            ("ions.tsv", "ions.txt"),
            ("reactions.yaml", "reactions.txt"),
        };

        static NervDataSync() => EditorApplication.delayCall += () => Sync(false);

        [MenuItem("NERV/Обновить химические данные")]
        private static void SyncMenu() => Sync(true);

        private static void Sync(bool verbose)
        {
            string src = Path.GetFullPath(Path.Combine(Application.dataPath, "../../core/data"));
            string dst = Path.Combine(Application.dataPath, "NERV/Resources/NervData");
            if (!Directory.Exists(src))
            {
                if (verbose) Debug.LogWarning("NERV: не найдена папка данных " + src);
                return;
            }
            Directory.CreateDirectory(dst);
            bool changed = false;
            foreach ((string from, string to) in Files)
            {
                string text = File.ReadAllText(Path.Combine(src, from));
                string path = Path.Combine(dst, to);
                if (File.Exists(path) && File.ReadAllText(path) == text) continue;
                File.WriteAllText(path, text);
                changed = true;
            }
            if (changed) AssetDatabase.Refresh();
            if (verbose) Debug.Log(changed ? "NERV: химические данные обновлены." : "NERV: химические данные актуальны.");
        }
    }
}

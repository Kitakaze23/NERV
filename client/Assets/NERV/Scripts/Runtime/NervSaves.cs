using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Nerv.Core.Sim;
using UnityEngine;
using UnityEngine.UI;

namespace Nerv.Client
{
    /// <summary>
    /// Свои сцены, снимки экрана и уведомления.
    /// Файлы лежат в папке данных приложения (на Mac — ~/Library/Application Support/…/Mendal):
    /// «Сцены» (*.mendal), «Снимки» (*.png), «Выгрузки» (*.csv).
    /// Сохранение — через команду <see cref="Checkpoint"/>: поток симуляции отдаёт слепок мира на ближайшем шаге
    /// (команды выполняются и на паузе), после чего файл пишется на диск.
    /// </summary>
    public sealed class NervSaves : MonoBehaviour
    {
        public static NervSaves Instance { get; private set; }

        public static string Root => Application.persistentDataPath;
        public static string ScenesDir => Path.Combine(Root, "Сцены");
        public static string ShotsDir => Path.Combine(Root, "Снимки");
        public static string ExportsDir => Path.Combine(Root, "Выгрузки");

        public struct SavedScene
        {
            public string Path, Name;
            public DateTime When;
        }

        private struct PendingSave
        {
            public Checkpoint Cp;
            public string Name;
            public double Temperature, Box;
            public Nerv.Core.Model.Medium Medium;
        }

        private readonly List<PendingSave> _pending = new List<PendingSave>();

        // Уведомление.
        private RectTransform _toast;
        private Text _toastText;
        private CanvasGroup _toastGroup;
        private float _toastUntil;

        // Окно «Сохранить сцену».
        private RectTransform _dialog;
        private InputField _nameInput;

        private void Awake() => Instance = this;
        private void OnDestroy() { if (Instance == this) Instance = null; }

        // ================= Сцены =================

        /// <summary>Сохранить текущую сцену под именем (файл пишется, как только поток симуляции отдаст слепок).</summary>
        public void Save(string name)
        {
            NervApp app = NervApp.Instance;
            if (app == null || app.Host == null) return;
            name = string.IsNullOrWhiteSpace(name) ? DefaultName(app) : name.Trim();
            var cp = new Checkpoint();
            app.Submit(cp);
            _pending.Add(new PendingSave { Cp = cp, Name = name, Temperature = app.Temperature, Box = app.BoxHalfSize, Medium = app.Medium });
        }

        public static string DefaultName(NervApp app) =>
            (string.IsNullOrEmpty(app.SceneName) ? "Моя сцена" : app.SceneName) + " · " + DateTime.Now.ToString("d MMM, HH:mm", new System.Globalization.CultureInfo("ru-RU"));

        private void WritePending()
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                PendingSave p = _pending[i];
                if (p.Cp.State == null) continue;
                _pending.RemoveAt(i);
                try
                {
                    Directory.CreateDirectory(ScenesDir);
                    string path = UniquePath(ScenesDir, SafeFileName(p.Name), SceneFile.Extension);
                    string text = SceneFile.Write(new SceneData
                    {
                        Name = p.Name, Temperature = p.Temperature, Medium = p.Medium, BoxHalfSize = p.Box, State = p.Cp.State,
                    });
                    File.WriteAllText(path, text, new UTF8Encoding(false));
                    Toast("Сцена «" + p.Name + "» сохранена");
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    Toast("Не удалось сохранить сцену: " + e.Message);
                }
            }
        }

        /// <summary>Сохранённые сцены, новые сверху.</summary>
        public static List<SavedScene> List()
        {
            var list = new List<SavedScene>();
            if (!Directory.Exists(ScenesDir)) return list;
            foreach (string f in Directory.GetFiles(ScenesDir, "*" + SceneFile.Extension))
            {
                string name = Path.GetFileNameWithoutExtension(f);
                try
                {
                    // Имя — из файла (вторая строка), файловое имя могло быть упрощено.
                    foreach (string line in File.ReadLines(f).Take(4))
                        if (line.StartsWith("name=", StringComparison.Ordinal) && line.Length > 5) name = line.Substring(5);
                }
                catch (IOException) { }
                list.Add(new SavedScene { Path = f, Name = name, When = File.GetLastWriteTime(f) });
            }
            return list.OrderByDescending(s => s.When).ToList();
        }

        public void Load(SavedScene s)
        {
            try
            {
                SceneData d = SceneFile.Read(File.ReadAllText(s.Path));
                if (string.IsNullOrEmpty(d.Name)) d.Name = s.Name;
                NervApp.Instance.LoadSaved(d);
                Toast("Загружена сцена «" + d.Name + "»");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Toast("Не удалось открыть сцену: " + e.Message);
            }
        }

        public void Delete(SavedScene s)
        {
            try
            {
                File.Delete(s.Path);
                Toast("Сцена «" + s.Name + "» удалена");
            }
            catch (Exception e) { Toast("Не удалось удалить: " + e.Message); }
        }

        // ================= Снимки и папки =================

        /// <summary>Снимок экрана целиком (с панелями) — файл появится в конце кадра.</summary>
        public void Screenshot()
        {
            try
            {
                Directory.CreateDirectory(ShotsDir);
                string path = UniquePath(ShotsDir, "Mendal " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss"), ".png");
                ScreenCapture.CaptureScreenshot(path);
                Toast("Снимок сохранён: " + Path.GetFileName(path));
            }
            catch (Exception e) { Toast("Не удалось сделать снимок: " + e.Message); }
        }

        public static void OpenFolder(string dir)
        {
            Directory.CreateDirectory(dir);
            Application.OpenURL(new Uri(dir).AbsoluteUri);
        }

        public static string SafeFileName(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in name)
                sb.Append(Path.GetInvalidFileNameChars().Contains(c) || c == ':' || c == '/' || c == '\\' ? '-' : c);
            string s = sb.ToString().Trim().TrimEnd('.');
            return s.Length == 0 ? "Сцена" : s.Length > 80 ? s.Substring(0, 80) : s;
        }

        public static string UniquePath(string dir, string baseName, string ext)
        {
            string path = Path.Combine(dir, baseName + ext);
            for (int i = 2; File.Exists(path); i++) path = Path.Combine(dir, baseName + " (" + i + ")" + ext);
            return path;
        }

        // ================= Уведомления =================

        /// <summary>Короткое уведомление внизу по центру, гаснет само через 3 с.</summary>
        public static void Toast(string message)
        {
            if (Instance == null) { Debug.Log(message); return; }
            Instance.ShowToast(message);
        }

        private void ShowToast(string message)
        {
            if (_toast == null) BuildToast();
            _toastText.text = message;
            _toast.sizeDelta = new Vector2(Mathf.Min(720, _toastText.preferredWidth + 40), 44);
            _toast.SetAsLastSibling();
            _toast.gameObject.SetActive(true);
            _toastUntil = Time.unscaledTime + 3f;
        }

        private void BuildToast()
        {
            UI.NervUI.Init();
            Image p = UI.NervUI.Panel(UI.NervUI.Root, "Toast", 12, UI.NervUI.WithAlpha(UI.NervUI.Energy, 0.6f));
            _toast = p.rectTransform;
            _toast.anchorMin = _toast.anchorMax = new Vector2(0.5f, 0);
            _toast.pivot = new Vector2(0.5f, 0);
            _toast.anchoredPosition = new Vector2(0, NervHud.Margin + 52 + 10 + 52 + 10);
            p.raycastTarget = false;
            _toastGroup = p.gameObject.AddComponent<CanvasGroup>();
            _toastGroup.blocksRaycasts = false;
            _toastText = UI.NervUI.Label(p.transform, "", UI.NervUI.SemiBold, 14, Color.white, TextAnchor.MiddleCenter);
            UI.NervUI.Stretch(_toastText.rectTransform, 12);
        }

        // ================= Окно «Сохранить сцену» =================

        public bool DialogOpen => _dialog != null && _dialog.gameObject.activeSelf;

        public void OpenSaveDialog()
        {
            NervApp app = NervApp.Instance;
            if (app == null) return;
            if (_dialog == null) BuildDialog();
            _nameInput.text = DefaultName(app);
            _dialog.SetAsLastSibling();
            _dialog.gameObject.SetActive(true);
            _nameInput.Select();
            _nameInput.ActivateInputField();
        }

        public void CloseDialog()
        {
            if (_dialog != null) _dialog.gameObject.SetActive(false);
        }

        private void BuildDialog()
        {
            UI.NervUI.Init();
            Transform root = UI.NervUI.Root;
            // Затемнение — щелчок мимо окна закрывает.
            Button dim = UI.NervUI.Button(root, "", UI.NervUI.ButtonStyle.Plain, CloseDialog, 12, 0, "Save Dialog");
            _dialog = (RectTransform)dim.transform;
            UI.NervUI.Stretch(_dialog);
            dim.GetComponent<Image>().color = new Color(0.02f, 0.03f, 0.06f, 0.6f);
            dim.transition = Selectable.Transition.None;

            Image panel = UI.NervUI.Panel(_dialog, "Window", 16);
            RectTransform pr = panel.rectTransform;
            pr.anchorMin = pr.anchorMax = pr.pivot = new Vector2(0.5f, 0.5f);
            pr.sizeDelta = new Vector2(480, 232);
            UI.NervUI.RegisterOccluder(pr);

            Text title = UI.NervUI.Label(panel.transform, "Сохранить сцену", UI.NervUI.Display, 20, Color.white, TextAnchor.MiddleLeft);
            UI.NervUI.Place(title.rectTransform, 24, 20, 432, 32);
            Text sub = UI.NervUI.Label(panel.transform, "Сохраняется всё: атомы, связи, скорости, температура и среда.", UI.NervUI.Regular, 13, UI.NervUI.TextSoft, TextAnchor.MiddleLeft);
            UI.NervUI.Place(sub.rectTransform, 24, 56, 432, 20);

            RectTransform host = UI.NervUI.Rect(panel.transform, "Name");
            UI.NervUI.Place(host, 24, 92, 432, 44);
            _nameInput = UI.NervUI.TextInput(host, "Название сцены");
            UI.NervUI.Stretch((RectTransform)_nameInput.transform);
            _nameInput.onSubmit.AddListener(_ => SubmitDialog());

            Button cancel = UI.NervUI.Button(panel.transform, "Отмена", UI.NervUI.ButtonStyle.Ghost, CloseDialog, 14);
            UI.NervUI.Place((RectTransform)cancel.transform, 196, 160, 120, 44);
            Button ok = UI.NervUI.Button(panel.transform, "Сохранить", UI.NervUI.ButtonStyle.Primary, SubmitDialog, 14);
            UI.NervUI.Place((RectTransform)ok.transform, 324, 160, 132, 44);
            _dialog.gameObject.SetActive(false);
        }

        private void SubmitDialog()
        {
            if (!DialogOpen) return;
            Save(_nameInput.text);
            CloseDialog();
        }

        // ================= Кадр =================

        private void Update()
        {
            WritePending();
            if (DialogOpen && NervInput.EscapePressed) CloseDialog();

            if (_toast != null && _toast.gameObject.activeSelf)
            {
                float left = _toastUntil - Time.unscaledTime;
                _toastGroup.alpha = Mathf.Clamp01(left / 0.4f);
                if (left <= 0) _toast.gameObject.SetActive(false);
            }
        }
    }
}

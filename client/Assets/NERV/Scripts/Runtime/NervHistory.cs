using System.Collections.Generic;
using Nerv.Core.Sim;
using UnityEngine;

namespace Nerv.Client
{
    /// <summary>
    /// Отмена и повтор действий пользователя (↶ ↷, Cmd/Ctrl+Z, Cmd/Ctrl+Shift+Z).
    /// Перед каждым изменением сцены (поставить, удалить, тянуть, искра, нагрев, собрать, очистить)
    /// в поток симуляции уходит <see cref="Checkpoint"/> — слепок мира на этот момент.
    /// Отмена = слепок текущего мира в стек повтора + <see cref="RestoreState"/> к прошлому слепку.
    /// Химия между действиями (реакции, горение) тоже откатывается: отмена искры гасит пламя.
    /// Настройки среды (температура, вода, скорость) история не трогает.
    /// </summary>
    public sealed class NervHistory : MonoBehaviour
    {
        public static NervHistory Instance { get; private set; }

        private const int Depth = 40;
        private readonly List<Checkpoint> _undo = new List<Checkpoint>();
        private readonly List<Checkpoint> _redo = new List<Checkpoint>();
        private int _lastRecordFrame = -1;

        public bool CanUndo => _undo.Count > 0 && _undo[_undo.Count - 1].State != null;
        public bool CanRedo => _redo.Count > 0 && _redo[_redo.Count - 1].State != null;

        private void Awake() => Instance = this;
        private void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>
        /// Запомнить мир перед изменением. Несколько вызовов за кадр (удаление «мазком») — один шаг истории.
        /// Возвращает слепок, чтобы его можно было отозвать (<see cref="Discard"/>), если действие не случилось.
        /// </summary>
        public Checkpoint Record()
        {
            NervApp app = NervApp.Instance;
            if (app == null || app.Host == null) return null;
            if (Time.frameCount == _lastRecordFrame && _undo.Count > 0) return _undo[_undo.Count - 1];
            _lastRecordFrame = Time.frameCount;
            var cp = new Checkpoint();
            app.Submit(cp);
            Push(_undo, cp);
            _redo.Clear();
            return cp;
        }

        /// <summary>Отозвать запись: например, щелчок рукой только открыл карточку и ничего не сдвинул.</summary>
        public void Discard(Checkpoint cp)
        {
            if (cp != null && _undo.Count > 0 && _undo[_undo.Count - 1] == cp) _undo.RemoveAt(_undo.Count - 1);
        }

        /// <summary>Новая сцена — новая история.</summary>
        public void Clear()
        {
            _undo.Clear();
            _redo.Clear();
        }

        public void Undo() => Step(_undo, _redo);
        public void Redo() => Step(_redo, _undo);

        private void Step(List<Checkpoint> from, List<Checkpoint> to)
        {
            NervApp app = NervApp.Instance;
            if (app == null || app.Host == null || from.Count == 0) return;
            Checkpoint target = from[from.Count - 1];
            if (target.State == null) return; // ещё не дошёл до потока симуляции — нажмут ещё раз
            from.RemoveAt(from.Count - 1);
            var now = new Checkpoint();
            app.Submit(now);
            Push(to, now);
            app.Submit(new RestoreState(target.State));
            UI.MoleculeCardPanel.Instance?.Close(); // id атомов после восстановления новые
        }

        private static void Push(List<Checkpoint> stack, Checkpoint cp)
        {
            stack.Add(cp);
            if (stack.Count > Depth) stack.RemoveAt(0);
        }

        private void Update()
        {
            if (NervInput.UndoPressed) Undo();
            else if (NervInput.RedoPressed) Redo();
        }
    }
}

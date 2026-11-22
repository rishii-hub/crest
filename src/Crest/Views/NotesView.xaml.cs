using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Crest.Core;
using Crest.Services;

namespace Crest.Views
{
    public partial class NotesView : UserControl, ITabView
    {
        readonly ObservableCollection<TodoItem> _todos;

        public NotesView()
        {
            InitializeComponent();
            var data = NotesStore.Data;
            NoteBox.Text = data.Text;
            _todos = new ObservableCollection<TodoItem>(data.Todos);
            Todos.ItemsSource = _todos;

            NoteBox.TextChanged += (s, e) => { NotesStore.Data.Text = NoteBox.Text; NotesStore.Save(); UpdateCounts(); };
            CopyNote.Click += (s, e) =>
            {
                try { if (NoteBox.Text.Length > 0) Clipboard.SetText(NoteBox.Text); } catch { }
                NotchWindow.Instance?.ShowBanner(new Toast { Glyph = Glyphs.Copy, Title = "Note copied" });
            };
            NewTodo.KeyDown += (s, e) =>
            {
                if (e.Key != Key.Enter || string.IsNullOrWhiteSpace(NewTodo.Text)) return;
                _todos.Insert(_todos.TakeWhile(t => !t.Done).Count(), new TodoItem { Text = NewTodo.Text.Trim() });
                NewTodo.Text = "";
                Persist();
                e.Handled = true;
            };
            ClearDone.Click += (s, e) =>
            {
                foreach (var t in _todos.Where(t => t.Done).ToList()) _todos.Remove(t);
                Persist();
            };
            UpdateCounts();
        }

        public void OnActivated() => UpdateCounts();
        public void OnDeactivated() { }

        void Persist()
        {
            NotesStore.Data.Todos = _todos.ToList();
            NotesStore.Save();
            UpdateCounts();
        }

        void UpdateCounts()
        {
            var words = NoteBox.Text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
            WordCount.Text = words == 0 ? "" : words == 1 ? "1 word" : $"{words} words";
            int open = _todos?.Count(t => !t.Done) ?? 0;
            TodoCount.Text = _todos == null || _todos.Count == 0 ? "" : $"{open} left";
            ClearDone.Visibility = _todos != null && _todos.Any(t => t.Done) ? Visibility.Visible : Visibility.Collapsed;
        }

        void OnToggle(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is TodoItem item)
            {
                // Done items sink to the bottom, like Reminders.
                _todos.Remove(item);
                if (item.Done) _todos.Add(item);
                else _todos.Insert(_todos.TakeWhile(t => !t.Done).Count(), item);
            }
            Persist();
        }

        void OnDelete(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is TodoItem item) _todos.Remove(item);
            Persist();
        }
    }
}

﻿
namespace Notepads.Core
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using Windows.System;
    using Windows.Storage;
    using Windows.UI;
    using Windows.UI.Core;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Controls;
    using Windows.UI.Xaml.Media;
    using Windows.UI.Xaml.Input;
    using Notepads.EventArgs;
    using Notepads.Services;
    using Notepads.Controls.TextEditor;
    using Notepads.Utilities;
    using SetsView;

    public class NotepadsCore : INotepadsCore
    {
        public SetsView Sets;

        public readonly string DefaultNewFileName;

        public event EventHandler<TextEditor> OnTextEditorLoaded;

        public event EventHandler<TextEditor> OnTextEditorUnloaded;

        public event EventHandler<TextEditor> OnTextEditorClosingWithUnsavedContent;

        public event EventHandler<TextEditor> OnTextEditorSelectionChanged;

        public event EventHandler<TextEditor> OnTextEditorEncodingChanged;

        public event EventHandler<TextEditor> OnTextEditorLineEndingChanged;

        public event KeyEventHandler OnTextEditorKeyDown;

        public NotepadsCore(SetsView sets, 
            string defaultNewFileName)
        {
            Sets = sets;
            Sets.SetClosing += SetsView_OnSetClosing;
            Sets.SetTapped += (sender, args) => { FocusOnTextEditor(args.Item as TextEditor); };

            DefaultNewFileName = defaultNewFileName;

            ThemeSettingsService.OnAccentColorChanged += OnAppAccentColorChanged;
            EditorSettingsService.OnDefaultLineEndingChanged += EditorSettingsService_OnDefaultLineEndingChanged;
            EditorSettingsService.OnDefaultEncodingChanged += EditorSettingsService_OnDefaultEncodingChanged;
        }

        public void OpenNewTextEditor()
        {
            OpenNewTextEditor(string.Empty,
                null,
                EditorSettingsService.EditorDefaultEncoding,
                EditorSettingsService.EditorDefaultLineEnding);
        }

        public async Task OpenNewTextEditor(StorageFile file)
        {
            if (FileOpened(file))
            {
                SwitchTo(file);
                return;
            }

            var textFile = await FileSystemUtility.ReadFile(file);

            OpenNewTextEditor(textFile.Content,
                file,
                textFile.Encoding,
                textFile.LineEnding);
        }

        private void OpenNewTextEditor(string text, 
            StorageFile file, 
            Encoding encoding, 
            LineEnding lineEnding)
        {
            var textEditor = new TextEditor()
            {
                EditingFile = file,
                Encoding = encoding,
                LineEnding = lineEnding,
                Saved = true,
            };

            textEditor.SetText(text);
            textEditor.ClearUndoQueue();
            textEditor.Loaded += TextEditor_Loaded;
            textEditor.Unloaded += TextEditor_Unloaded;
            textEditor.TextChanging += TextEditor_TextChanging;
            textEditor.SelectionChanged += TextEditor_SelectionChanged;
            textEditor.KeyDown += OnTextEditorKeyDown;
            textEditor.OnSetClosingKeyDown += TextEditor_OnSetClosingKeyDown;

            var newItem = new SetsViewItem
            {
                Header = file == null ? DefaultNewFileName : file.Name,
                Content = textEditor,
                SelectionIndicatorForeground = Application.Current.Resources["SystemControlForegroundAccentBrush"] as SolidColorBrush,
                Icon = new SymbolIcon(Symbol.Save)
                {
                    Foreground = Application.Current.Resources["SystemControlForegroundAccentBrush"] as SolidColorBrush,
                }
            };
            newItem.Icon.Visibility = Visibility.Collapsed;

            // Notepads should replace current "New Document.txt" with open file if it is empty and it is the only tab that has been created.
            if (GetNumberOfOpenedTextEditors() == 1 && file != null)
            {
                var selectedEditor = GetSelectedTextEditor();
                if (selectedEditor.Saved && selectedEditor.EditingFile == null)
                {
                    Sets.Items?.Clear();
                }
            }

            Sets.Items?.Add(newItem);

            if (GetNumberOfOpenedTextEditors() > 1)
            {
                Sets.SelectedItem = newItem;
                Sets.ScrollToLastSet();
            }
        }

        public async Task<bool> SaveTextEditorContentToFile(TextEditor textEditor, StorageFile file)
        {
            var success = await textEditor.SaveToFile(file);
            if (success)
            {
                var item = GetTextEditorSetsViewItem(textEditor);
                if (item != null)
                {
                    item.Header = file.Name;
                    item.Icon.Visibility = Visibility.Collapsed;
                }
            }
            return success;
        }

        public void DeleteTextEditor(TextEditor textEditor)
        {
            var item = GetTextEditorSetsViewItem(textEditor);
            item.IsEnabled = false;
            Sets.Items?.Remove(item);
        }

        public int GetNumberOfOpenedTextEditors()
        {
            return Sets.Items?.Count ?? 0;
        }

        public bool TryGetSharingContent(TextEditor textEditor, out string title, out string content)
        {
            title = textEditor.EditingFile != null ? textEditor.EditingFile.Name : DefaultNewFileName;
            content = textEditor.GetContentForSharing();
            return !string.IsNullOrEmpty(content);
        }

        public bool HaveUnsavedTextEditor()
        {
            if (Sets.Items == null || Sets.Items.Count == 0) return false;
            foreach (SetsViewItem setsItem in Sets.Items)
            {
                if (!(setsItem.Content is TextEditor textEditor)) continue;
                if (textEditor.Saved) continue;
                return true;
            }
            return false;
        }

        public void ChangeLineEnding(TextEditor textEditor, LineEnding lineEnding)
        {
            if (lineEnding == textEditor.LineEnding) return;
            textEditor.LineEnding = lineEnding;
            MarkTextEditorSetNotSaved(textEditor);
            OnTextEditorLineEndingChanged?.Invoke(this, textEditor);
        }

        public void ChangeEncoding(TextEditor textEditor, Encoding encoding)
        {
            if (EncodingUtility.Equals(textEditor.Encoding, encoding)) return;
            textEditor.Encoding = encoding;
            MarkTextEditorSetNotSaved(textEditor);
            OnTextEditorEncodingChanged?.Invoke(this, textEditor);
        }

        public void SwitchTo(bool next)
        {
            if (Sets.Items == null) return;
            if (Sets.Items.Count < 2) return;

            var setsCount = Sets.Items.Count;
            var selected = Sets.SelectedIndex;

            if (next && setsCount > 1)
            {
                if (selected == setsCount - 1) Sets.SelectedIndex = 0;
                else Sets.SelectedIndex += 1;
            }
            else if (!next && setsCount > 1)
            {
                if (selected == 0) Sets.SelectedIndex = setsCount - 1;
                else Sets.SelectedIndex -= 1;
            }
        }

        private void SwitchTo(StorageFile file)
        {
            var item = GetTextEditorSetsViewItem(file);
            Sets.SelectedItem = item;
            Sets.ScrollIntoView(item);
        }

        public TextEditor GetSelectedTextEditor()
        {
            if ((!((Sets.SelectedItem as SetsViewItem)?.Content is TextEditor textEditor))) return null;
            return textEditor;
        }

        public void FocusOnSelectedTextEditor()
        {
            GetSelectedTextEditor()?.Focus(FocusState.Programmatic);
        }

        public void FocusOnTextEditor(TextEditor textEditor)
        {
            textEditor?.Focus(FocusState.Programmatic);
        }

        private void SetsView_OnSetClosing(object sender, SetClosingEventArgs e)
        {
            if (!(e.Set.Content is TextEditor textEditor)) return;
            if (textEditor.Saved) return;
            if (OnTextEditorClosingWithUnsavedContent != null)
            {
                e.Cancel = true;
                OnTextEditorClosingWithUnsavedContent.Invoke(this, textEditor);
            }
        }

        private bool FileOpened(StorageFile file)
        {
            var item = GetTextEditorSetsViewItem(file);
            return item != null;
        }

        private SetsViewItem GetTextEditorSetsViewItem(StorageFile file)
        {
            if (Sets.Items == null) return null;
            foreach (SetsViewItem setsItem in Sets.Items)
            {
                if (setsItem.Content is TextEditor textEditor)
                {
                    if (string.Equals(textEditor.EditingFile?.Path, file.Path))
                    {
                        return setsItem;
                    }
                }
            }
            return null;
        }

        private SetsViewItem GetTextEditorSetsViewItem(TextEditor textEditor)
        {
            if (Sets.Items == null) return null;
            foreach (SetsViewItem setsItem in Sets.Items)
            {
                if (setsItem.Content is TextEditor editor)
                {
                    if (textEditor == editor) return setsItem;
                }
            }
            return null;
        }

        private void MarkTextEditorSetNotSaved(TextEditor textEditor)
        {
            if (textEditor != null)
            {
                textEditor.Saved = false;
            }
            var item = GetTextEditorSetsViewItem(textEditor);
            if (item != null)
            {
                item.Icon.Visibility = Visibility.Visible;
            }
        }

        private void TextEditor_Loaded(object sender, RoutedEventArgs e)
        {
            if (!(sender is TextEditor textEditor)) return;
            OnTextEditorLoaded?.Invoke(this, textEditor);
        }

        private void TextEditor_Unloaded(object sender, RoutedEventArgs e)
        {
            if (!(sender is TextEditor textEditor)) return;
            OnTextEditorUnloaded?.Invoke(this, textEditor);
        }

        private void TextEditor_OnSetClosingKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (!(sender is TextEditor textEditor)) return;
            if (Sets.Items == null) return;
            foreach (SetsViewItem setsItem in Sets.Items)
            {
                if (setsItem.Content != textEditor) continue;
                setsItem.Close();
                e.Handled = true;
                break;
            }
        }

        private void TextEditor_TextChanging(object sender, RichEditBoxTextChangingEventArgs args)
        {
            if (!(sender is TextEditor textEditor) || !args.IsContentChanging) return;
            if (textEditor.Saved)
            {
                MarkTextEditorSetNotSaved(textEditor);
            }
        }

        private void TextEditor_SelectionChanged(object sender, RoutedEventArgs e)
        {
            if (!(sender is TextEditor textEditor)) return;
            OnTextEditorSelectionChanged?.Invoke(this, textEditor);
        }

        private void OnAppAccentColorChanged(object sender, Color color)
        {
            if (Sets.Items == null) return;
            foreach (SetsViewItem item in Sets.Items)
            {
                item.Icon.Foreground = Application.Current.Resources["SystemControlForegroundAccentBrush"] as SolidColorBrush;
                item.SelectionIndicatorForeground = Application.Current.Resources["SystemControlForegroundAccentBrush"] as SolidColorBrush;
            }
        }

        private void EditorSettingsService_OnDefaultEncodingChanged(object sender, Encoding encoding)
        {
            if (Sets.Items == null) return;
            foreach (SetsViewItem setItem in Sets.Items)
            {
                if (!(setItem.Content is TextEditor textEditor)) continue;
                if (textEditor.EditingFile != null) continue;

                textEditor.Encoding = encoding;
                OnTextEditorEncodingChanged?.Invoke(this, textEditor);
            }
        }                                     

        private void EditorSettingsService_OnDefaultLineEndingChanged(object sender, LineEnding lineEnding)
        {
            if (Sets.Items == null) return;
            foreach (SetsViewItem setItem in Sets.Items)
            {
                if (!(setItem.Content is TextEditor textEditor)) continue;
                if (textEditor.EditingFile != null) continue;

                textEditor.LineEnding = lineEnding;
                OnTextEditorLineEndingChanged?.Invoke(this, textEditor);
            }
        }
    }
}

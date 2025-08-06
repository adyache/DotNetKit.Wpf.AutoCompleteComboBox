using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using DotNetKit.Misc.Disposables;
using DotNetKit.Windows.Media;

namespace DotNetKit.Windows.Controls
{
    /// <summary>
    /// AutoCompleteComboBox.xaml
    /// </summary>
    public partial class AutoCompleteComboBox : ComboBox
    {
        private readonly SerialDisposable _disposable = new();

        private TextBox _editableTextBoxCache;
        private Predicate<object> _defaultItemsFilter;

        public TextBox EditableTextBox
        {
            get
            {
                const string name = "PART_EditableTextBox";
                return _editableTextBoxCache ??= (TextBox)VisualTreeModule.FindChild(this, name);
            }
        }

        /// <summary>
        /// Gets text to match with the query from an item.
        /// Never null.
        /// </summary>
        /// <param name="item"/>
        private string TextFromItem(object item)
        {
            if (item == null) return string.Empty;

            var d = new DependencyVariable<string>();
            d.SetBinding(item, TextSearch.GetTextPath(this));
            return d.Value ?? string.Empty;
        }

        #region ItemsSource
        public new static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(AutoCompleteComboBox),
                new PropertyMetadata(null, ItemsSourcePropertyChanged));

        public new IEnumerable ItemsSource
        {
            get => (IEnumerable)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        private static void ItemsSourcePropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
        {
            var comboBox = (ComboBox)dependencyObject;
            var previousSelectedItem = comboBox.SelectedItem;

            if (e.NewValue is ICollectionView cv)
            {
                ((AutoCompleteComboBox)dependencyObject)._defaultItemsFilter = cv.Filter;
                comboBox.ItemsSource = cv;
            }
            else
            {
                ((AutoCompleteComboBox)dependencyObject)._defaultItemsFilter = null;
                var newValue = e.NewValue as IEnumerable;
                var newCollectionViewSource = new CollectionViewSource
                {
                    Source = newValue
                };
                comboBox.ItemsSource = newCollectionViewSource.View;
            }

            comboBox.SelectedItem = previousSelectedItem;

            // if ItemsSource doesn't contain previousSelectedItem
            if (comboBox.SelectedItem != previousSelectedItem)
            {
                comboBox.SelectedItem = null;
            }
        }
        #endregion ItemsSource

        #region Setting

        public static readonly DependencyProperty SettingProperty =
            DependencyProperty.Register(
                nameof(Setting),
                typeof(AutoCompleteComboBoxSetting),
                typeof(AutoCompleteComboBox)
            );

        public AutoCompleteComboBoxSetting Setting
        {
            get => (AutoCompleteComboBoxSetting)GetValue(SettingProperty);
            set => SetValue(SettingProperty, value);
        }

        AutoCompleteComboBoxSetting SettingOrDefault => Setting ?? AutoCompleteComboBoxSetting.Default;

        #endregion

        #region OnTextChanged

        private long revisionId;
        private string previousText;

        struct TextBoxStatePreserver
            : IDisposable
        {
            private readonly TextBox textBox;
            private readonly int selectionStart;
            private readonly int selectionLength;
            private readonly string text;

            public void Dispose()
            {
                //Debug.WriteLine($"Preserver dispose: '{textBox.Text}' [{textBox.SelectionStart}, {textBox.SelectionLength}] => '{text}' [{selectionStart}, {selectionLength}]");
                textBox.Text = text;
                textBox.Select(selectionStart, selectionLength);
            }

            public TextBoxStatePreserver(TextBox textBox)
            {
                this.textBox = textBox;
                selectionStart = textBox.SelectionStart;
                selectionLength = textBox.SelectionLength;
                text = textBox.Text;
                //Debug.WriteLine($"Preserver constructor: '{text}' [{selectionStart}, {selectionLength}]");
            }
        }

        private static int CountWithMax<T>(IEnumerable<T> xs, Predicate<T> predicate, int maxCount)
        {
            var count = 0;
            foreach (var x in xs)
            {
                if (predicate(x))
                {
                    count++;
                    if (count > maxCount) return count;
                }
            }
            return count;
        }

        private void Unselect()
        {
            //var textBox = EditableTextBox;
            //textBox.Select(textBox.SelectionStart + textBox.SelectionLength, 0);
        }

        private void UpdateFilter(Predicate<object> filter)
        {
            using (new TextBoxStatePreserver(EditableTextBox))
            using (Items.DeferRefresh())
            {
                // Can empty the text box. I don't why.
                Items.Filter = filter;
            }
        }

        private void OpenDropDown(Predicate<object> filter)
        {
            using (new TextBoxStatePreserver(EditableTextBox))
            {
                IsDropDownOpen = false;
                UpdateFilter(filter);
                IsDropDownOpen = true;
                Unselect();
            }
        }

        private void OpenDropDown()
        {
            var filter = GetFilter();
            OpenDropDown(filter);
        }

        protected override void OnSelectionChanged(SelectionChangedEventArgs e)
        {
            using (NextEventDepth)
            {
                base.OnSelectionChanged(e);
            }
        }

        protected override void OnDropDownClosed(EventArgs e)
        {
            //closing dropdown resets filter
            base.OnDropDownClosed(e);

            using (new TextBoxStatePreserver(EditableTextBox))
            using (Items.DeferRefresh())
            {
                Items.Filter = _defaultItemsFilter;
            }
        }

        protected override void OnLostFocus(RoutedEventArgs e)
        {
            //losing focus resets filter. useful when dropdown was never opened, e.g. on an auto-complete that resolved to a single item
            using (new TextBoxStatePreserver(EditableTextBox))
            using (Items.DeferRefresh())
            {
                Items.Filter = _defaultItemsFilter;
            }

            base.OnLostFocus(e);
        }

        private void UpdateSuggestionList()
        {
            var text = TextWithoutAutocomplete;

            if (text == previousText) return;
            previousText = text;

            if (string.IsNullOrEmpty(Text))
            {
                //Debug.WriteLine("Update suggestion list - clearing selection, resetting filter");
                IsDropDownOpen = false;
                SelectedItem = null;

                using (Items.DeferRefresh())
                {
                    Items.Filter = _defaultItemsFilter;
                }
            }
            //else if (SelectedItem != null && TextFromItem(SelectedItem) == text)
            //{
            //    // It seems the user selected an item.
            //    // Do nothing.
            //}
            else
            {
                using (NextEventDepth)
                {
                    //using (new TextBoxStatePreserver(EditableTextBox))
                    //{
                    //    Debug.WriteLine("Setting selected item to null");
                    //    SelectedItem = null;
                    //}

                    var filter = GetFilter();
                    var maxCount = SettingOrDefault.MaxSuggestionCount;
                    var count = CountWithMax(ItemsSource?.Cast<object>() ?? [], filter, maxCount);
                    UpdateFilter(filter);

                    if (1 < count && count <= maxCount || count == 1 && !string.Equals(TextFromItem(Items[0]), Text, StringComparison.InvariantCultureIgnoreCase))
                    {
                        using (new TextBoxStatePreserver(EditableTextBox))
                        {
                            IsDropDownOpen = false;
                            IsDropDownOpen = true; //OpenDropDown(filter);
                        }
                    }
                }
            }
        }

        private IDisposable NextEventDepth => new EventDepthTracker(this);

        private class EventDepthTracker : IDisposable
        {
            private readonly AutoCompleteComboBox _combo;

            public EventDepthTracker(AutoCompleteComboBox combo)
            {
                _combo = combo;
                _combo._eventDepth++;
            }

            public void Dispose()
            {
                _combo._eventDepth--;
            }
        }

        private int _eventDepth;

        private void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_eventDepth > 0) return;

            //Debug.WriteLine($"OnTextChanged. Open: {IsDropDownOpen} Text: '{Text}' Selection: [{EditableTextBox.SelectionStart}, {EditableTextBox.SelectionLength}], Selected item: {SelectedItem != null}");
            var id = unchecked(++revisionId);
            var setting = SettingOrDefault;

            if (setting.Delay <= TimeSpan.Zero)
            {
                UpdateSuggestionList();
                return;
            }

            _disposable.Content =
                new Timer(
                    state =>
                    {
                        Dispatcher.InvokeAsync(() =>
                        {
                            if (revisionId != id) return;
                            UpdateSuggestionList();
                        });
                    },
                    null,
                    setting.Delay,
                    Timeout.InfiniteTimeSpan
                );
        }

        #endregion

        /* Progress
         * Fixed: When refiltering with the dropdown already open, lose down keys until dropdown is closed and re-opened
         * Fixed: Selecting an item from dropdown (keyboard or mouse) resets filter.  Shouldn't until dropdown is closed
         * Fixed: Selecting an item from dropdown: keyboard doesn't work (sometimes?), mouse resets filter (ok) and doesn't clear (ok)
         * Fixed: Keeping auto-search item doesn't reset filter. Fixed by resetting on focus loss
         * Fixed: Closing dropdown should reset filter.
         * Fixed: Losing focus should reset filter.
         * Fixed: Up/Down keys should NOT scroll through dropdown options
         * Up/Down keys, and perhaps sometimes left/right, should raise some navigation event that can be handled outside.
         */

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.Space)
            {
                OpenDropDown();
                e.Handled = true;
            }
            else if (TextWithoutAutocomplete == string.Empty && (Keyboard.Modifiers & ~ModifierKeys.Shift) == ModifierKeys.None && e.IsDown && e.Key is >= Key.D0 and <= Key.Z)
            {
                //Debug.WriteLine("Key preview - Clearing selection, resetting filter");
                IsDropDownOpen = false;
                SelectedItem = null;

                using (Items.DeferRefresh())
                {
                    Items.Filter = _defaultItemsFilter;
                }
            }
            else if (Keyboard.Modifiers == ModifierKeys.None && e.IsDown && (e.Key == Key.Down || e.Key == Key.Up) && !IsDropDownOpen)
            {
                //todo move focus to the next control up/down
                e.Handled = true;
            }

            if (!e.Handled) base.OnPreviewKeyDown(e);
        }

        private string TextWithoutAutocomplete
        {
            get
            {
                var selectionLength = EditableTextBox.SelectionLength;
                var text = Text;
                return selectionLength == 0 
                    ? text 
                    : text.Remove(EditableTextBox.SelectionStart, selectionLength);
            }
        }

        private Predicate<object> GetFilter()
        {
            var filter = SettingOrDefault.GetFilter(TextWithoutAutocomplete, TextFromItem);

            return _defaultItemsFilter != null
                ? i => _defaultItemsFilter(i) && filter(i)
                : filter;
        }

        public AutoCompleteComboBox()
        {
            InitializeComponent();
        }
    }
}
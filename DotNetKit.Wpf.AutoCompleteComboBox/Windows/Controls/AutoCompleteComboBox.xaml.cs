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
        readonly SerialDisposable disposable = new SerialDisposable();

        TextBox editableTextBoxCache;

        Predicate<object> defaultItemsFilter;

        public TextBox EditableTextBox
        {
            get
            {
                if (editableTextBoxCache == null)
                {
                    const string name = "PART_EditableTextBox";
                    editableTextBoxCache = (TextBox)VisualTreeModule.FindChild(this, name);
                }
                return editableTextBoxCache;
            }
        }

        /// <summary>
        /// Gets text to match with the query from an item.
        /// Never null.
        /// </summary>
        /// <param name="item"/>
        string TextFromItem(object item)
        {
            if (item == null) return string.Empty;

            var d = new DependencyVariable<string>();
            d.SetBinding(item, TextSearch.GetTextPath(this));
            return d.Value ?? string.Empty;
        }

        #region ItemsSource
        public static new readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(AutoCompleteComboBox),
                new PropertyMetadata(null, ItemsSourcePropertyChanged));
        public new IEnumerable ItemsSource
        {
            get
            {
                return (IEnumerable)GetValue(ItemsSourceProperty);
            }
            set
            {
                SetValue(ItemsSourceProperty, value);
            }
        }

        private static void ItemsSourcePropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs dpcea)
        {
            var comboBox = (ComboBox)dependencyObject;
            var previousSelectedItem = comboBox.SelectedItem;

            if (dpcea.NewValue is ICollectionView cv)
            {
                ((AutoCompleteComboBox)dependencyObject).defaultItemsFilter = cv.Filter;
                comboBox.ItemsSource = cv;
            }
            else
            {
                ((AutoCompleteComboBox)dependencyObject).defaultItemsFilter = null;
                IEnumerable newValue = dpcea.NewValue as IEnumerable;
                CollectionViewSource newCollectionViewSource = new CollectionViewSource
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
        static readonly DependencyProperty settingProperty =
            DependencyProperty.Register(
                "Setting",
                typeof(AutoCompleteComboBoxSetting),
                typeof(AutoCompleteComboBox)
            );

        public static DependencyProperty SettingProperty
        {
            get { return settingProperty; }
        }

        public AutoCompleteComboBoxSetting Setting
        {
            get { return (AutoCompleteComboBoxSetting)GetValue(SettingProperty); }
            set { SetValue(SettingProperty, value); }
        }

        AutoCompleteComboBoxSetting SettingOrDefault
        {
            get { return Setting ?? AutoCompleteComboBoxSetting.Default; }
        }
        #endregion

        #region OnTextChanged
        long revisionId;
        string previousText;

        struct TextBoxStatePreserver
            : IDisposable
        {
            readonly TextBox textBox;
            readonly int selectionStart;
            readonly int selectionLength;
            readonly string text;

            public void Dispose()
            {
                Debug.WriteLine($"Presrever dispose: '{textBox.Text}' [{textBox.SelectionStart}, {textBox.SelectionLength}] => '{text}' [{selectionStart}, {selectionLength}]");
                textBox.Text = text;
                textBox.Select(selectionStart, selectionLength);
            }

            public TextBoxStatePreserver(TextBox textBox)
            {
                this.textBox = textBox;
                selectionStart = textBox.SelectionStart;
                selectionLength = textBox.SelectionLength;
                text = textBox.Text;
                Debug.WriteLine($"Presrever constructor: '{text}' [{selectionStart}, {selectionLength}]");
            }
        }

        static int CountWithMax<T>(IEnumerable<T> xs, Predicate<T> predicate, int maxCount)
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

        void Unselect()
        {
            //var textBox = EditableTextBox;
            //textBox.Select(textBox.SelectionStart + textBox.SelectionLength, 0);
        }

        void UpdateFilter(Predicate<object> filter)
        {
            using (new TextBoxStatePreserver(EditableTextBox))
            using (Items.DeferRefresh())
            {
                // Can empty the text box. I don't why.
                Items.Filter = filter;
            }
        }

        void OpenDropDown(Predicate<object> filter)
        {
            using (new TextBoxStatePreserver(EditableTextBox))
            {
                UpdateFilter(filter);
                IsDropDownOpen = true;
                Unselect();
            }
        }

        void OpenDropDown()
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

        void UpdateSuggestionList()
        {
            var text = TextWithoutAutocomplete;

            if (text == previousText) return;
            previousText = text;

            if (string.IsNullOrEmpty(Text))
            {
                Debug.WriteLine("Update suggestion list - clearing selection, resetting filter");
                IsDropDownOpen = false;
                SelectedItem = null;

                using (Items.DeferRefresh())
                {
                    Items.Filter = defaultItemsFilter;
                }
            }
            else if (false && SelectedItem != null && TextFromItem(SelectedItem) == text)
            {
                // It seems the user selected an item.
                // Do nothing.
            }
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
                    var count = CountWithMax(ItemsSource?.Cast<object>() ?? Enumerable.Empty<object>(), filter, maxCount);
                    UpdateFilter(filter);

                    if (1 < count && count <= maxCount)
                    {
                        using (new TextBoxStatePreserver(EditableTextBox))
                        {
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

        void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_eventDepth > 0) return;

            Debug.WriteLine($"OnTextChanged. Open: {IsDropDownOpen} Text: '{Text}' Selection: [{EditableTextBox.SelectionStart}, {EditableTextBox.SelectionLength}], Selected item: {SelectedItem != null}");
            var id = unchecked(++revisionId);
            var setting = SettingOrDefault;

            if (setting.Delay <= TimeSpan.Zero)
            {
                UpdateSuggestionList();
                return;
            }

            disposable.Content =
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
         * Selecting an item from dropdown (keyboard or mouse) resets filter.  Shouldn't until dropdown is closed
         * Selecting an item from dropdown: keyboard doesn't work (sometimes?), mouse resets filter (ok) and doesn't clear (ok)
         * Keeping autosearch item doesn't reset filter.  Even if closing dropdown is made to reset filter, keeping autosearch may never open the dropdown if there's only 1 suggestion.
         * Closing dropdown should probably reset filter.
         * Opening dropdown by the user should reset filter? unless non-empty text selection as after an autocomplete
         */

        void ComboBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.Space)
            {
                OpenDropDown();
                e.Handled = true;
            }
            else if (TextWithoutAutocomplete == string.Empty && (Keyboard.Modifiers & ~ModifierKeys.Shift) == ModifierKeys.None && e.IsDown && e.Key is >= Key.D0 and <= Key.Z)
            {
                Debug.WriteLine("Key preview - Clearing selection, resetting filter");
                IsDropDownOpen = false;
                SelectedItem = null;

                using (Items.DeferRefresh())
                {
                    Items.Filter = defaultItemsFilter;
                }
            }
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

        Predicate<object> GetFilter()
        {
            var filter = SettingOrDefault.GetFilter(TextWithoutAutocomplete, TextFromItem);

            return defaultItemsFilter != null
                ? i => defaultItemsFilter(i) && filter(i)
                : filter;
        }

        public AutoCompleteComboBox()
        {
            InitializeComponent();

            AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler(OnTextChanged));
        }
    }
}
using System;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osuTK;
using osuTK.Input;

namespace OsuXaml.Controls
{
    /// <summary>
    /// A drop-down list, like WPF's ComboBox. Items come from <c>ItemsSource</c> (shown through <c>ItemTemplate</c> or
    /// <c>DisplayMemberPath</c>) or from <c>&lt;ComboBoxItem&gt;</c> / <c>&lt;x:String&gt;</c> children. The closed box shows
    /// the selected item through the same template. <see cref="Selector.SelectedItem"/>, <see cref="Selector.SelectedIndex"/>,
    /// <see cref="SelectedValue"/>, <see cref="Text"/> and <see cref="IsDropDownOpen"/> bind two-way.
    /// When focused, Up/Down change the selection; Enter, Space, F4 or Alt+Down open the list, where Up/Down move the
    /// highlight, Enter picks and Escape closes.
    /// </summary>
    public partial class ComboBox : Selector
    {
        private const float chevron_width = 32;
        private const float min_width = 120;
        private const float min_height = 36;

        private readonly Container face;
        private readonly Box faceBackground;
        private readonly Container selectionArea;
        private readonly Container selectionClip;
        private readonly ContentPresenter selectionPresenter;
        private readonly TextBlock placeholder;
        private readonly SpriteIcon chevron;
        private readonly Popup popup;
        private readonly PopupMenuBorder dropDown;

        private ScrollViewer scroll;
        private EditableTextBox textBox;

        private bool isEnabled = true;
        private bool isEditable;
        private bool hovered;
        private int highlightedIndex = -1;
        private int scrollFrames = -1;

        private object presentedContent;
        private DataTemplate presentedTemplate;
        private Colour4 foreground = Theme.Text;
        private Colour4 borderBrush = Theme.Border;
        private Colour4 background = Theme.Surface;
        private MarginPadding padding = new MarginPadding { Left = 10 };

        private bool updatingText;
        private bool updatingValue;

        public ComboBox()
        {
            AddInternal(popup = new Popup
            {
                PlacementTarget = this,
                MatchTargetWidth = true,
                Child = new Container
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Padding = new MarginPadding { Vertical = 3 },
                    Child = dropDown = new PopupMenuBorder { RelativeSizeAxes = Axes.X, MaxHeight = 300 },
                },
            });

            // The list lives in the popup layer and keeps updating while the box itself is hidden.
            popup.Child.OnUpdate += _ => closeIfHidden();

            face = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 6,
                BorderThickness = 1,
                BorderColour = borderBrush,
                Children = new Drawable[]
                {
                    faceBackground = new Box { RelativeSizeAxes = Axes.Both, Colour = background },
                    selectionArea = new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Padding = new MarginPadding { Left = padding.Left, Right = chevron_width },
                        // A second container clips, because masking ignores the padding of the masked container itself.
                        Child = selectionClip = new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Masking = true,
                            Children = new Drawable[]
                            {
                                placeholder = new TextBlock { Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft, Colour = Theme.SubtleText, Alpha = 0 },
                                selectionPresenter = new ContentPresenter { Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft },
                            },
                        },
                    },
                    chevron = new SpriteIcon
                    {
                        Icon = FontAwesome.Solid.ChevronDown,
                        Size = new Vector2(11),
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.Centre,
                        X = -chevron_width / 2,
                        Colour = Theme.SubtleText,
                    },
                },
            };

            IsDropDownOpen.ValueChanged += dropDownChanged;
            Text.ValueChanged += textChanged;
            SelectedValue.ValueChanged += _ => selectedValueChanged();
        }

        /// <summary>Whether the drop-down list is showing. Binds two-way.</summary>
        public Bindable<bool> IsDropDownOpen => popup.IsOpen;

        /// <summary>The tallest the drop-down list grows before it scrolls.</summary>
        public float MaxDropDownHeight
        {
            get => dropDown.MaxHeight;
            set => dropDown.MaxHeight = value;
        }

        /// <summary>Shown in the box while nothing is selected.</summary>
        public LocalisableString PlaceholderText
        {
            get => placeholder.Text;
            set => placeholder.Text = value;
        }

        /// <summary>
        /// The text of the selected item. With <see cref="IsEditable"/> the user types it; typing an item's text selects
        /// that item, other text clears the selection. Setting it selects the item with that text.
        /// </summary>
        public Bindable<string> Text { get; } = new Bindable<string>(string.Empty);

        /// <summary>Replaces the selection box with a text box, so the user can type a value.</summary>
        public bool IsEditable
        {
            get => isEditable;
            set
            {
                if (isEditable == value)
                    return;

                isEditable = value;

                if (value)
                {
                    selectionClip.Add(textBox = new EditableTextBox(this) { RelativeSizeAxes = Axes.Both });
                    textBox.Current.Value = Text.Value;
                    textBox.Current.ValueChanged += e =>
                    {
                        if (!textBox.Relayouting && !updatingText)
                            Text.Value = e.NewValue;
                    };
                    selectionArea.Padding = new MarginPadding { Left = 4, Right = chevron_width };
                }
                else
                {
                    selectionClip.Remove(textBox, true);
                    textBox = null;
                    selectionArea.Padding = new MarginPadding { Left = padding.Left, Right = chevron_width };
                }

                refreshSelectionBox(true);
            }
        }

        /// <summary>
        /// The member of each item that <see cref="SelectedValue"/> reads, e.g. <c>SelectedValuePath="Id"</c>.
        /// XAML compiles it into a typed accessor for the ItemsSource item type.
        /// </summary>
        public ItemBinding SelectedValuePath { get; set; }

        /// <summary>The <see cref="SelectedValuePath"/> member of the selected item. Setting it selects the matching item.</summary>
        public Bindable<object> SelectedValue { get; } = new Bindable<object>();

        public bool IsEnabled
        {
            get => isEnabled;
            set
            {
                isEnabled = value;
                this.FadeTo(value ? 1 : 0.4f, 150, Easing.OutQuint);

                if (!value)
                    IsDropDownOpen.Value = false;
            }
        }

        public Colour4 Background
        {
            get => background;
            set
            {
                background = value;
                updateFace();
            }
        }

        public Colour4 BorderBrush
        {
            get => borderBrush;
            set
            {
                borderBrush = value;
                updateFace();
            }
        }

        public new float BorderThickness
        {
            get => face.BorderThickness;
            set => face.BorderThickness = value;
        }

        public new float CornerRadius
        {
            get => face.CornerRadius;
            set => face.CornerRadius = value;
        }

        /// <summary>Colour of the selected item's text.</summary>
        public Colour4 Foreground
        {
            get => foreground;
            set
            {
                foreground = value;
                selectionPresenter.Foreground = value;
            }
        }

        /// <summary>Space around the selected item inside the box. Only the left edge is used; the chevron takes the right.</summary>
        public new MarginPadding Padding
        {
            get => padding;
            set
            {
                padding = value;
                if (!isEditable)
                    selectionArea.Padding = new MarginPadding { Left = value.Left, Right = chevron_width };
            }
        }

        public event Action DropDownOpened;

        public event Action DropDownClosed;

        public override bool AcceptsFocus => isEnabled;

        /// <summary>The item the keyboard or mouse highlights in the open list, or -1.</summary>
        public int HighlightedIndex => highlightedIndex;

        /// <summary>The text an item shows: its member through DisplayMemberPath, a ComboBoxItem's text content, or ToString().</summary>
        public string GetItemText(object item)
        {
            switch (item)
            {
                case null:
                    return string.Empty;

                case ComboBoxItem comboBoxItem:
                    return comboBoxItem.Content is Drawable d ? textOf(d) : comboBoxItem.Content?.ToString() ?? string.Empty;

                case Drawable drawable:
                    return textOf(drawable);
            }

            if (ItemTemplate != null && ItemTemplate.CanBuild(item))
            {
                // A text template (DisplayMemberPath) knows the text; building it once is cheaper than reflecting over the item.
                var scope = new BindingScope();
                string text = textOf(ItemTemplate.Build(item, scope));
                scope.UnbindAll();

                if (text != null)
                    return text;
            }

            return item.ToString();
        }

        protected override Drawable CreateItemsHost(Panel panel)
        {
            // The items live in the drop-down; the control itself shows the selection box.
            panel.RelativeSizeAxes = Axes.X;
            panel.Width = 1;
            panel.Height = 0;

            dropDown.Child = scroll = new ScrollViewer { Child = panel };
            return face;
        }

        protected override bool IsItemItsOwnContainer(object item) => item is ComboBoxItem;

        protected override Drawable CreateContainer() => new ComboBoxItem();

        protected override void PrepareContainer(Drawable container, object item)
        {
            if (container is ComboBoxItem comboBoxItem)
                comboBoxItem.Owner = this;

            base.PrepareContainer(container, item);

            // Also runs when ItemTemplate changes, which changes how the selection looks.
            if (item != null && Equals(item, SelectedItem.Value))
                refreshSelectionBox(true);
        }

        protected override void OnItemsChanged()
        {
            // A value or text that arrived before the items (bindings run before load) selects its item now.
            if (SelectedItem.Value == null && Items.Count > 0)
            {
                object pending = itemForValue(SelectedValue.Value) ?? (string.IsNullOrEmpty(Text.Value) ? null : itemForText(Text.Value));
                if (pending != null)
                    SelectedItem.Value = pending;
            }

            base.OnItemsChanged();

            // The highlight stays on its item when items are inserted or removed before it.
            int follow = -1;

            for (int i = 0; i < Items.Count && follow < 0; i++)
            {
                if (ContainerFromIndex(i) is ComboBoxItem { IsHighlighted: true })
                    follow = i;
            }

            if (follow >= 0)
                highlightedIndex = follow;
            else
            {
                // The highlighted item went away: highlight whatever now sits at its place.
                int index = Math.Min(highlightedIndex, Items.Count - 1);
                highlightedIndex = -1;
                setHighlight(index);
            }
        }

        protected override void OnSelectionChanged()
        {
            base.OnSelectionChanged();
            refreshSelectionBox(false);

            object item = SelectedItem.Value;

            // Before the items exist a missing selection says nothing, so values bound early are kept.
            if (item == null && Items.Count == 0)
                return;

            if (!updatingText)
            {
                updatingText = true;
                if (item != null || !isEditable)
                    Text.Value = GetItemText(item);
                updatingText = false;
            }

            if (!updatingValue && SelectedValuePath != null)
            {
                updatingValue = true;
                SelectedValue.Value = item == null ? null : SelectedValuePath.GetValue(item);
                updatingValue = false;
            }
        }

        public override void SelectContainer(Drawable container, UIEvent e = null)
        {
            base.SelectContainer(container, e);
            IsDropDownOpen.Value = false;
            focusSelf();
        }

        /// <summary>Highlights the item a container shows, as hovering it does.</summary>
        public void HighlightContainer(Drawable container) => setHighlight(IndexFromContainer(container));

        public override Vector2 Measure(Vector2 availableSize)
        {
            Vector2 content;

            if (isEditable)
                content = new Vector2(80, 20);
            else if (selectionPresenter.Content != null)
                content = selectionPresenter.DrawSize;
            else
                content = placeholder.Alpha > 0 ? placeholder.DrawSize : Vector2.Zero;

            return new Vector2(
                Math.Max(min_width, selectionArea.Padding.Left + content.X + chevron_width),
                Math.Max(min_height, content.Y + 12));
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            refreshSelectionBox(true);
            updateFace();
        }

        protected override void Update()
        {
            base.Update();

            // The list lays out a frame after it opens; scroll to the selection once it has.
            if (scrollFrames >= 0 && IsDropDownOpen.Value)
            {
                var container = ContainerFromIndex(highlightedIndex);

                if (container == null)
                    scrollFrames = -1;
                else if (container.IsLoaded && ++scrollFrames >= 2)
                {
                    scroll.ScrollIntoView(container, false);
                    scrollFrames = -1;
                }
            }
        }

        protected override bool OnHover(HoverEvent e)
        {
            hovered = true;
            updateFace();
            return true;
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            hovered = false;
            updateFace();
        }

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            if (e.Button != MouseButton.Left || !isEnabled)
                return false;

            // Like WPF the list opens on press. While it is open, the popup layer takes this press and closes it.
            IsDropDownOpen.Value = !IsDropDownOpen.Value;
            focusSelf();
            return true;
        }

        // Handling the click keeps the framework from moving focus to nothing when the button is released.
        protected override bool OnClick(ClickEvent e) => isEnabled;

        protected override void OnFocus(FocusEvent e)
        {
            base.OnFocus(e);
            updateFace();
        }

        protected override void OnFocusLost(FocusLostEvent e)
        {
            base.OnFocusLost(e);

            // Clicking in the list drops focus to nothing; focus moving to another control closes the list.
            if (e.NextFocused != null && e.NextFocused != textBox)
                IsDropDownOpen.Value = false;

            updateFace();
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            if (!isEnabled || !(HasFocus || textBox?.HasFocus == true))
                return base.OnKeyDown(e);

            return handleKey(e) || base.OnKeyDown(e);
        }

        private bool handleKey(KeyDownEvent e)
        {
            bool open = IsDropDownOpen.Value;

            switch (e.Key)
            {
                case Key.Down when e.AltPressed:
                case Key.F4:
                    IsDropDownOpen.Value = !open;
                    return true;

                case Key.Up when e.AltPressed:
                    IsDropDownOpen.Value = false;
                    return true;

                case Key.Up:
                case Key.Down:
                case Key.Home:
                case Key.End:
                case Key.PageUp:
                case Key.PageDown:
                {
                    if (Items.Count == 0)
                        return true;

                    int current = open ? highlightedIndex : SelectedIndex.Value;
                    int next = navigate(current, e.Key);

                    if (open)
                    {
                        setHighlight(next);
                        scrollFrames = 1;
                    }
                    else
                        SelectedIndex.Value = next;

                    return true;
                }

                case Key.Enter:
                case Key.KeypadEnter:
                case Key.Space when !isEditable:
                    if (!open)
                        IsDropDownOpen.Value = true;
                    else
                    {
                        if (highlightedIndex >= 0)
                            SelectedIndex.Value = highlightedIndex;
                        IsDropDownOpen.Value = false;
                    }

                    return true;

                case Key.Escape when open:
                    IsDropDownOpen.Value = false;
                    return true;

                case Key.Tab when open:
                    IsDropDownOpen.Value = false;
                    return false;
            }

            return false;
        }

        private int navigate(int current, Key key)
        {
            int last = Items.Count - 1;
            const int page = 8;

            switch (key)
            {
                case Key.Up:
                    return current <= 0 ? 0 : current - 1;

                case Key.Down:
                    return current < 0 ? 0 : Math.Min(last, current + 1);

                case Key.Home:
                    return 0;

                case Key.End:
                    return last;

                case Key.PageUp:
                    return Math.Max(0, current - page);

                default:
                    return Math.Min(last, Math.Max(0, current) + page);
            }
        }

        private void dropDownChanged(ValueChangedEvent<bool> e)
        {
            if (IsLoaded)
                chevron.RotateTo(e.NewValue ? 180 : 0, 200, Easing.OutQuint);
            else
                chevron.Rotation = e.NewValue ? 180 : 0;

            if (e.NewValue)
            {
                setHighlight(SelectedIndex.Value);
                scrollFrames = 0;
                DropDownOpened?.Invoke();
            }
            else
            {
                setHighlight(-1);
                scrollFrames = -1;
                DropDownClosed?.Invoke();
            }

            updateFace();
        }

        /// <summary>Closes the list once the box (or anything around it) is hidden, as WPF closes it when the box loses focus.</summary>
        private void closeIfHidden()
        {
            if (!IsDropDownOpen.Value)
                return;

            for (Drawable d = this; d != null; d = d.Parent)
            {
                // A popup's content fades in from nothing, so a combo box inside another popup isn't hidden by that.
                if (!d.IsPresent && !(d is PopupRoot))
                {
                    IsDropDownOpen.Value = false;
                    return;
                }
            }
        }

        private void setHighlight(int index)
        {
            if (highlightedIndex == index)
                return;

            if (ContainerFromIndex(highlightedIndex) is ComboBoxItem previous)
                previous.IsHighlighted = false;

            highlightedIndex = index;

            if (ContainerFromIndex(index) is ComboBoxItem current)
                current.IsHighlighted = true;
        }

        private void focusSelf() => GetContainingFocusManager()?.ChangeFocus((Drawable)textBox ?? this);

        private void refreshSelectionBox(bool force)
        {
            object item = SelectedItem.Value;
            object content;
            DataTemplate template = null;

            switch (item)
            {
                case null:
                    content = null;
                    break;

                case ComboBoxItem comboBoxItem:
                    // The item's own drawable can't be in two places, so a drawable is shown by its text.
                    content = comboBoxItem.Content is Drawable d ? textOf(d) : comboBoxItem.Content;
                    template = comboBoxItem.ContentTemplate;
                    break;

                case Drawable drawable:
                    content = textOf(drawable);
                    break;

                default:
                    content = item;
                    template = ItemTemplate;
                    break;
            }

            if (isEditable)
                content = null;

            placeholder.Alpha = content == null && !isEditable ? 1 : 0;

            if (!force && Equals(content, presentedContent) && template == presentedTemplate)
                return;

            presentedContent = content;
            presentedTemplate = template;

            if (selectionPresenter.ContentTemplate != template)
            {
                selectionPresenter.Content = null;
                selectionPresenter.ContentTemplate = template;
            }

            if (force)
                selectionPresenter.Content = null;

            selectionPresenter.Foreground = foreground;
            selectionPresenter.Content = content;
        }

        private void textChanged(ValueChangedEvent<string> e)
        {
            if (textBox != null && textBox.Current.Value != e.NewValue)
            {
                updatingText = true;
                textBox.Current.Value = e.NewValue ?? string.Empty;
                updatingText = false;
            }

            if (updatingText)
                return;

            // Text set from outside (binding or typing) selects the item with that text.
            object item = itemForText(e.NewValue);

            updatingText = true;

            if (item != null)
                SelectedItem.Value = item;
            else if (isEditable && Items.Count > 0)
                SelectedItem.Value = null;

            updatingText = false;
        }

        private void selectedValueChanged()
        {
            if (updatingValue || SelectedValuePath == null || Items.Count == 0)
                return;

            updatingValue = true;
            SelectedItem.Value = itemForValue(SelectedValue.Value);
            updatingValue = false;
        }

        private object itemForText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return null;

            foreach (object item in Items)
            {
                if (string.Equals(GetItemText(item), text, StringComparison.OrdinalIgnoreCase))
                    return item;
            }

            return null;
        }

        private object itemForValue(object value)
        {
            if (value == null || SelectedValuePath == null)
                return null;

            foreach (object item in Items)
            {
                if (Equals(SelectedValuePath.GetValue(item), value))
                    return item;
            }

            return null;
        }

        private void updateFace()
        {
            bool active = IsDropDownOpen.Value || HasFocus || textBox?.HasFocus == true;

            faceBackground.FadeColour(hovered && isEnabled ? Theme.Hover : background, 100, Easing.OutQuint);
            face.BorderColour = active ? Theme.Accent : borderBrush;
        }

        /// <summary>The first text inside a drawable, for showing drawable items in the selection box.</summary>
        private static string textOf(Drawable drawable)
        {
            switch (drawable)
            {
                case null:
                    return null;

                case SpriteText text:
                    return text.Text.ToString();

                case ContentControl content:
                    return content.Content is Drawable inner ? textOf(inner) : content.Content?.ToString();

                case Panel panel:
                    foreach (var child in panel.Children)
                    {
                        string found = textOf(child);
                        if (found != null)
                            return found;
                    }

                    return null;

                case IContainerEnumerable<Drawable> container:
                    foreach (var child in container.Children)
                    {
                        string found = textOf(child);
                        if (found != null)
                            return found;
                    }

                    return null;

                default:
                    return null;
            }
        }

        /// <summary>A borderless text box for <see cref="IsEditable"/> that re-lays out its text on resize without reporting edits.</summary>
        private partial class EditableTextBox : BasicTextBox
        {
            private readonly ComboBox owner;
            private float laidOutWidth = -1;

            public EditableTextBox(ComboBox owner)
            {
                this.owner = owner;
                BackgroundUnfocused = Colour4.Transparent;
                BackgroundFocused = Colour4.Transparent;
                BackgroundCommit = Colour4.Transparent;
            }

            /// <summary>True while the text is re-applied for layout, so the owner ignores the change.</summary>
            public bool Relayouting { get; private set; }

            // The focused text box sees keys first and would take Enter (commit) and Escape (unfocus); the combo box's keys win.
            protected override bool OnKeyDown(KeyDownEvent e) => (owner.isEnabled && owner.handleKey(e)) || base.OnKeyDown(e);

            protected override void Update()
            {
                base.Update();

                // Same as TextBox: text set before the box had its width stays scrolled out of view until re-applied.
                if (DrawWidth != laidOutWidth && !HasFocus)
                {
                    laidOutWidth = DrawWidth;
                    Relayouting = true;

                    string value = Text;
                    Text = string.Empty;
                    Text = value;

                    Relayouting = false;
                }
            }
        }
    }

    /// <summary>One entry of a <see cref="ComboBox"/> drop-down, with hover, keyboard highlight and selection feedback.</summary>
    public partial class ComboBoxItem : ContentControl, ISelectableItem
    {
        private readonly Box highlight;
        private bool isSelected;
        private bool isHighlighted;

        public ComboBoxItem()
        {
            CornerRadius = 4;
            Padding = new MarginPadding { Horizontal = 10, Vertical = 6 };
            Chrome.Add(highlight = new Box { RelativeSizeAxes = Axes.Both, Colour = Theme.Text, Alpha = 0, Depth = 1 });
        }

        /// <summary>The combo box this item belongs to; set by the combo box.</summary>
        public ComboBox Owner { get; set; }

        public bool IsSelected
        {
            get => isSelected;
            set
            {
                if (isSelected == value)
                    return;

                isSelected = value;
                updateState();
            }
        }

        /// <summary>Whether the mouse or keyboard points at this item in the open list.</summary>
        public bool IsHighlighted
        {
            get => isHighlighted;
            set
            {
                if (isHighlighted == value)
                    return;

                isHighlighted = value;
                updateState();
            }
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            updateState();
        }

        protected override bool OnHover(HoverEvent e)
        {
            if (Owner != null)
                Owner.HighlightContainer(this);
            else
                IsHighlighted = true;

            return true;
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            if (Owner == null)
                IsHighlighted = false;
        }

        protected override bool OnClick(ClickEvent e)
        {
            Owner?.SelectContainer(this, e);
            return true;
        }

        private void updateState()
        {
            if (!IsLoaded)
                return;

            highlight.FadeColour(isSelected ? Theme.Accent : Theme.Text, 100);
            highlight.FadeTo(isSelected ? (isHighlighted ? 0.36f : 0.28f) : isHighlighted ? 0.08f : 0, 100, Easing.OutQuint);
        }
    }
}

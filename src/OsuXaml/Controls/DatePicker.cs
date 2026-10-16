using System;
using System.Globalization;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;

namespace OsuXaml.Controls
{
    /// <summary>How a <see cref="DatePicker"/> writes its date: the culture's short ("d") or long ("D") date pattern.</summary>
    public enum DatePickerFormat
    {
        Long,
        Short,
    }

    /// <summary>
    /// A text box for a date with a button that opens a <see cref="Calendar"/> below it, like WPF's DatePicker.
    /// <see cref="SelectedDate"/> binds two-way. Picking a day (or pressing Enter in the calendar) closes the calendar;
    /// a date typed into the box is read when Enter is pressed or the box loses focus, and text that isn't a date is
    /// put back and reported through <see cref="DateValidationError"/>. <see cref="PlaceholderText"/> shows while no date is selected.
    /// </summary>
    public partial class DatePicker : CompositeDrawable, ILayoutElement
    {
        private const float default_width = 200;

        private readonly TextBox textBox;
        private readonly CalendarDropButton button;
        private readonly Popup popup;

        private DatePickerFormat selectedDateFormat = DatePickerFormat.Short;
        private string dateFormat;
        private bool focusCalendar;

        public DatePicker()
        {
            Height = 36;

            Calendar = new Calendar();
            popup = new Popup { Placement = PlacementMode.Bottom, VerticalOffset = 4, Child = Calendar };

            InternalChildren = new Drawable[]
            {
                textBox = new TextBox
                {
                    RelativeSizeAxes = Axes.Both,
                    Size = Vector2.One,
                    CommitOnFocusLost = true,
                    PlaceholderText = "Select a date",
                },
                button = new CalendarDropButton
                {
                    Anchor = Anchor.CentreRight,
                    Origin = Anchor.CentreRight,
                    X = -4,
                    Action = () => IsDropDownOpen.Value = !IsDropDownOpen.Value,
                },
                popup,
            };

            popup.PlacementTarget = this;
            popup.IsOpen.BindTo(IsDropDownOpen);
            Calendar.SelectedDate.BindTo(SelectedDate);

            IsDropDownOpen.BindValueChanged(dropDownChanged);
            SelectedDate.BindValueChanged(e =>
            {
                showDate();
                SelectedDateChanged?.Invoke(e.NewValue);
            }, true);

            textBox.OnCommit += (_, _) => readText();
            Calendar.DatePicked += _ => IsDropDownOpen.Value = false;
        }

        /// <summary>The selected date, or null. Binds two-way with a <c>Bindable&lt;DateTime?&gt;</c>.</summary>
        public Bindable<DateTime?> SelectedDate { get; } = new Bindable<DateTime?>();

        /// <summary>Whether the calendar is open. Binds two-way.</summary>
        public Bindable<bool> IsDropDownOpen { get; } = new Bindable<bool>();

        /// <summary>The calendar shown in the drop-down.</summary>
        public Calendar Calendar { get; }

        /// <summary>The text in the box.</summary>
        public string Text => textBox.Text.Value;

        /// <summary>The hint shown while no date is selected.</summary>
        public string PlaceholderText
        {
            get => textBox.PlaceholderText.ToString();
            set => textBox.PlaceholderText = value;
        }

        /// <summary>Short (the default) or long date pattern of the current culture.</summary>
        public DatePickerFormat SelectedDateFormat
        {
            get => selectedDateFormat;
            set
            {
                selectedDateFormat = value;
                showDate();
            }
        }

        /// <summary>A .NET date format such as <c>yyyy-MM-dd</c>, used instead of <see cref="SelectedDateFormat"/> for showing and reading dates.</summary>
        public string DateFormat
        {
            get => dateFormat;
            set
            {
                dateFormat = string.IsNullOrEmpty(value) ? null : value;
                showDate();
            }
        }

        /// <summary>The month the calendar opens on when no date is selected.</summary>
        public Bindable<DateTime> DisplayDate => Calendar.DisplayDate;

        public DateTime? DisplayDateStart
        {
            get => Calendar.DisplayDateStart;
            set => Calendar.DisplayDateStart = value;
        }

        public DateTime? DisplayDateEnd
        {
            get => Calendar.DisplayDateEnd;
            set => Calendar.DisplayDateEnd = value;
        }

        public DayOfWeek FirstDayOfWeek
        {
            get => Calendar.FirstDayOfWeek;
            set => Calendar.FirstDayOfWeek = value;
        }

        public bool IsTodayHighlighted
        {
            get => Calendar.IsTodayHighlighted;
            set => Calendar.IsTodayHighlighted = value;
        }

        /// <summary>Raised when <see cref="SelectedDate"/> changes, with the new date.</summary>
        public event Action<DateTime?> SelectedDateChanged;

        public event Action CalendarOpened;

        public event Action CalendarClosed;

        /// <summary>Raised with the typed text when it isn't a date (or is outside the allowed range).</summary>
        public event Action<string> DateValidationError;

        private static CultureInfo culture => CultureInfo.CurrentCulture;

        private string pattern => dateFormat ?? (selectedDateFormat == DatePickerFormat.Long ? "D" : "d");

        private string format(DateTime date) => date.ToString(pattern, culture);

        private void showDate() => textBox.Text.Value = SelectedDate.Value is DateTime date ? format(date) : string.Empty;

        private void readText()
        {
            string text = textBox.Text.Value?.Trim() ?? string.Empty;

            if (text.Length == 0)
                SelectedDate.Value = null;
            else if (tryParse(text, out var date) && (DisplayDateStart == null || date >= DisplayDateStart) && (DisplayDateEnd == null || date <= DisplayDateEnd))
                SelectedDate.Value = date;
            else
                DateValidationError?.Invoke(text);

            // Shows the date in the picker's own format, or puts back the last good date.
            showDate();
        }

        private bool tryParse(string text, out DateTime date)
        {
            // The picker's own format first, so what it shows always reads back; then anything the culture understands.
            if (DateTime.TryParseExact(text, pattern, culture, DateTimeStyles.AllowWhiteSpaces, out date)
                || DateTime.TryParse(text, culture, DateTimeStyles.AllowWhiteSpaces, out date))
            {
                date = date.Date;
                return true;
            }

            return false;
        }

        private void dropDownChanged(ValueChangedEvent<bool> e)
        {
            button.IsActive = e.NewValue;

            if (!e.NewValue)
            {
                CalendarClosed?.Invoke();
                return;
            }

            // Open on the selected month with the month view, and take keyboard focus so the arrows move the selection.
            Calendar.DisplayMode = CalendarMode.Month;
            Calendar.DisplayDate.Value = SelectedDate.Value ?? DateTime.Today;
            focusCalendar = true;

            CalendarOpened?.Invoke();
        }

        protected override void Update()
        {
            base.Update();

            // Focus only sticks once the popup has started fading in, so keep trying while it opens.
            if (focusCalendar)
            {
                if (!IsDropDownOpen.Value || Calendar.HasFocus)
                    focusCalendar = false;
                else if (Calendar.IsLoaded && GetContainingFocusManager()?.ChangeFocus(Calendar) == true)
                    focusCalendar = false;
            }
        }

        public Vector2 Measure(Vector2 availableSize) => new Vector2(default_width, Height);

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // Outside a panel, a picker without a width gets a usable one.
            if (!(Parent is Slot) && !RelativeSizeAxes.HasFlag(Axes.X) && Width == 0)
                Width = default_width;
        }

        /// <summary>The calendar icon at the right of the box.</summary>
        private partial class CalendarDropButton : CompositeDrawable
        {
            private readonly Box hover;
            private readonly SpriteIcon icon;
            private bool isActive;

            public CalendarDropButton()
            {
                Size = new Vector2(28);
                Masking = true;
                CornerRadius = 6;

                InternalChildren = new Drawable[]
                {
                    hover = new Box { RelativeSizeAxes = Axes.Both, Colour = Theme.Text, Alpha = 0 },
                    icon = new SpriteIcon
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Size = new Vector2(14),
                        Icon = FontAwesome.Regular.CalendarAlt,
                        Colour = Theme.SubtleText,
                    },
                };
            }

            public Action Action { get; set; }

            /// <summary>Lit while the calendar is open.</summary>
            public bool IsActive
            {
                set
                {
                    isActive = value;
                    icon.FadeColour(value ? Theme.Accent : Theme.SubtleText, 150, Easing.OutQuint);
                }
            }

            protected override bool OnHover(HoverEvent e)
            {
                hover.FadeTo(0.1f, 100, Easing.OutQuint);
                icon.FadeColour(Theme.Text, 100, Easing.OutQuint);
                return true;
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                hover.FadeOut(250, Easing.OutQuint);
                icon.FadeColour(isActive ? Theme.Accent : Theme.SubtleText, 250, Easing.OutQuint);
            }

            protected override bool OnClick(ClickEvent e)
            {
                hover.FadeTo(0.25f).FadeTo(0.1f, 300, Easing.OutQuint);
                Action?.Invoke();
                return true;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Input;

namespace OsuXaml.Controls
{
    /// <summary>What a <see cref="Calendar"/> shows: the days of a month, the months of a year, or the years of a decade.</summary>
    public enum CalendarMode
    {
        Month,
        Year,
        Decade,
    }

    /// <summary>How many dates a <see cref="Calendar"/> lets the user select, like WPF's CalendarSelectionMode.</summary>
    public enum CalendarSelectionMode
    {
        /// <summary>One date.</summary>
        SingleDate,

        /// <summary>One run of dates: drag across them, or Shift+click (Shift+arrows) to extend from the last date picked.</summary>
        SingleRange,

        /// <summary>Several runs: as SingleRange, and Ctrl+click toggles a date (Ctrl+drag adds a run) without clearing the rest.</summary>
        MultipleRange,

        /// <summary>The user can't select dates; code and bindings still can.</summary>
        None,
    }

    /// <summary>
    /// A month grid for picking a date, like WPF's Calendar: a weekday header, previous/next buttons, today's date
    /// ringed and the selected date filled. <see cref="SelectedDate"/> binds two-way; <see cref="DisplayDate"/> is the month shown.
    /// Clicking the title zooms out to the months of the year and then to a decade. Once focused, the arrow keys move the
    /// selection (Page Up/Down by month, Home/End to the month's ends, Ctrl+Up zooms out, Enter zooms back in).
    /// <see cref="SelectionMode"/> allows a range or several ranges of dates, held in <see cref="SelectedDates"/>.
    /// Like WPF it keeps its natural size and centres itself in a larger slot.
    /// </summary>
    public partial class Calendar : CompositeDrawable
    {
        private const float padding = 8;
        private const float header_height = 32;
        private const float header_gap = 4;
        private const float weekday_height = 24;
        private const float cell_width = 34;
        private const float cell_height = 32;

        /// <summary>The size the calendar is drawn at.</summary>
        public static readonly Vector2 NaturalSize = new Vector2(
            padding * 2 + cell_width * 7,
            padding * 2 + header_height + header_gap + weekday_height + cell_height * 6);

        private readonly Container viewHost;
        private readonly CalendarHeaderButton previous;
        private readonly CalendarHeaderButton next;
        private readonly CalendarHeaderButton header;

        private readonly List<CalendarButton> cells = new List<CalendarButton>();

        private Container view;
        private bool viewValid;
        private DateTime shownPeriod;
        private CalendarMode shownMode;

        private CalendarMode displayMode;
        private DayOfWeek firstDayOfWeek = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        private bool isTodayHighlighted = true;
        private DateTime? displayDateStart;
        private DateTime? displayDateEnd;

        private CalendarSelectionMode selectionMode;
        private HashSet<DateTime> selectedSet = new HashSet<DateTime>();
        private bool updatingSelection;

        // Where Shift ranges start, and the keyboard position, in the range modes.
        private DateTime? anchor;
        private DateTime? currentDate;

        // The selection a drag adds its run to (empty unless Ctrl+drag in MultipleRange); null when not dragging.
        private List<DateTime> dragBase;

        public Calendar()
        {
            Size = NaturalSize;

            InternalChild = new Container
            {
                Size = NaturalSize,
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                Masking = true,
                CornerRadius = 8,
                BorderColour = Theme.Border,
                BorderThickness = 1,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = Theme.Mantle },
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Padding = new MarginPadding(padding),
                        Children = new Drawable[]
                        {
                            new Container
                            {
                                RelativeSizeAxes = Axes.X,
                                Height = header_height,
                                Children = new Drawable[]
                                {
                                    previous = new CalendarHeaderButton(FontAwesome.Solid.ChevronLeft)
                                    {
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        Action = () => step(-1),
                                    },
                                    header = new CalendarHeaderButton
                                    {
                                        Anchor = Anchor.Centre,
                                        Origin = Anchor.Centre,
                                        Action = zoomOut,
                                    },
                                    next = new CalendarHeaderButton(FontAwesome.Solid.ChevronRight)
                                    {
                                        Anchor = Anchor.CentreRight,
                                        Origin = Anchor.CentreRight,
                                        Action = () => step(1),
                                    },
                                },
                            },
                            viewHost = new Container
                            {
                                RelativeSizeAxes = Axes.Both,
                                Padding = new MarginPadding { Top = header_height + header_gap },
                            },
                        },
                    },
                },
            };

            SelectedDate.BindValueChanged(onSelectedDateChanged);
            SelectedDates.BindCollectionChanged((_, _) => onSelectedDatesChanged());
            DisplayDate.BindValueChanged(onDisplayDateChanged);

            refreshView();
        }

        /// <summary>
        /// The selected date, or null. Binds two-way: <c>SelectedDate="{Bind Birthday}"</c> with a <c>Bindable&lt;DateTime?&gt;</c>.
        /// With several dates selected it is the first of <see cref="SelectedDates"/>; setting it to another date selects only that date.
        /// </summary>
        public Bindable<DateTime?> SelectedDate { get; } = new Bindable<DateTime?>();

        /// <summary>
        /// Every selected date, in the order picked (a range runs from where it started). Binds two-way to a view model's
        /// <c>BindableList&lt;DateTime&gt;</c>: <c>SelectedDates="{Bind Days}"</c>.
        /// </summary>
        public BindableList<DateTime> SelectedDates { get; } = new BindableList<DateTime>();

        /// <summary>
        /// Whether the user picks one date (the default), one range, several ranges, or none. Switching to SingleDate keeps only
        /// the first selected date.
        /// </summary>
        public CalendarSelectionMode SelectionMode
        {
            get => selectionMode;
            set
            {
                if (selectionMode == value)
                    return;

                selectionMode = value;

                if (value == CalendarSelectionMode.SingleDate && SelectedDates.Count > 1)
                    setSelection(new List<DateTime> { SelectedDates[0] });

                refreshStates();
            }
        }

        /// <summary>A date in the month (or year, or decade) being shown. Selecting a date in another month moves it there.</summary>
        public Bindable<DateTime> DisplayDate { get; } = new Bindable<DateTime>(DateTime.Today);

        /// <summary>Raised when the selection changes, with the new <see cref="SelectedDate"/>.</summary>
        public event Action<DateTime?> SelectedDatesChanged;

        /// <summary>Raised when the shown month, year or decade changes.</summary>
        public event Action<DateTime> DisplayDateChanged;

        /// <summary>Raised when <see cref="DisplayMode"/> changes.</summary>
        public event Action<CalendarMode> DisplayModeChanged;

        /// <summary>A date was picked by clicking it or pressing Enter, as opposed to moving the selection with the arrow keys.</summary>
        internal event Action<DateTime> DatePicked;

        public CalendarMode DisplayMode
        {
            get => displayMode;
            set
            {
                if (displayMode == value)
                    return;

                displayMode = value;
                refreshView();
                DisplayModeChanged?.Invoke(value);
            }
        }

        /// <summary>The first column of the month grid. Defaults to the current culture's first day of the week.</summary>
        public DayOfWeek FirstDayOfWeek
        {
            get => firstDayOfWeek;
            set
            {
                firstDayOfWeek = value;
                viewValid = false;
                refreshView();
            }
        }

        /// <summary>Whether today's date is ringed.</summary>
        public bool IsTodayHighlighted
        {
            get => isTodayHighlighted;
            set
            {
                isTodayHighlighted = value;
                refreshStates();
            }
        }

        /// <summary>The first date that can be shown and selected.</summary>
        public DateTime? DisplayDateStart
        {
            get => displayDateStart;
            set
            {
                displayDateStart = value?.Date;
                DisplayDate.Value = clamp(DisplayDate.Value);
                refreshStates();
            }
        }

        /// <summary>The last date that can be shown and selected.</summary>
        public DateTime? DisplayDateEnd
        {
            get => displayDateEnd;
            set
            {
                displayDateEnd = value?.Date;
                DisplayDate.Value = clamp(DisplayDate.Value);
                refreshStates();
            }
        }

        /// <summary>The header's parts, for tests and automation.</summary>
        public Drawable PreviousButton => previous;

        public Drawable NextButton => next;

        /// <summary>The title; clicking it zooms out from month to year to decade.</summary>
        public Drawable HeaderButton => header;

        /// <summary>The title text currently shown, e.g. "October 2026".</summary>
        public string HeaderText => header.Text;

        public override bool AcceptsFocus => true;

        private static CultureInfo culture => CultureInfo.CurrentCulture;

        private bool isSelectable(DateTime date) => (displayDateStart == null || date >= displayDateStart) && (displayDateEnd == null || date <= displayDateEnd);

        private bool selectsRanges => selectionMode == CalendarSelectionMode.SingleRange || selectionMode == CalendarSelectionMode.MultipleRange;

        private DateTime clamp(DateTime date)
        {
            if (displayDateStart is DateTime start && date < start)
                return start;

            if (displayDateEnd is DateTime end && date > end)
                return end;

            return date;
        }

        private void onSelectedDateChanged(ValueChangedEvent<DateTime?> e)
        {
            // Times are dropped, as in WPF; the date part comes back through this handler.
            if (e.NewValue is DateTime withTime && withTime.TimeOfDay != TimeSpan.Zero)
            {
                SelectedDate.Value = withTime.Date;
                return;
            }

            if (updatingSelection)
                return;

            // Set from outside: as in WPF it selects just that date, unless it already is the first selected one.
            if (e.NewValue is DateTime date)
            {
                anchor = currentDate = date;

                if (SelectedDates.Count == 0 || SelectedDates[0] != date)
                    setSelection(new List<DateTime> { date });
            }
            else
                setSelection(new List<DateTime>());
        }

        /// <summary>SelectedDates was changed from outside (code, or a bound view model list): SelectedDate follows its first date.</summary>
        private void onSelectedDatesChanged()
        {
            if (updatingSelection)
                return;

            updatingSelection = true;
            SelectedDate.Value = SelectedDates.Count > 0 ? SelectedDates[0].Date : null;
            updatingSelection = false;

            selectionChanged(true);
        }

        /// <summary>
        /// Makes <paramref name="dates"/> the selection in one change, with SelectedDate its first date. A selection set from
        /// outside brings that date into view; <paramref name="showFirst"/> is false for the user's own clicks and keys, which
        /// show the date they reached instead, so removing the first date of a run doesn't jump to another month.
        /// </summary>
        private void setSelection(List<DateTime> dates, bool showFirst = true)
        {
            if (dates.SequenceEqual(SelectedDates))
                return;

            updatingSelection = true;
            SelectedDates.ReplaceRange(0, SelectedDates.Count, dates);
            SelectedDate.Value = dates.Count > 0 ? dates[0] : null;
            updatingSelection = false;

            selectionChanged(showFirst);
        }

        private void selectionChanged(bool showFirst)
        {
            selectedSet = new HashSet<DateTime>(SelectedDates.Select(d => d.Date));

            // Show the selected date's month (or year, or decade).
            if (showFirst && SelectedDate.Value is DateTime date && !samePeriod(date, DisplayDate.Value, displayMode))
                DisplayDate.Value = date;

            refreshView();
            SelectedDatesChanged?.Invoke(SelectedDate.Value);
        }

        /// <summary>The selectable dates from <paramref name="from"/> to <paramref name="to"/>, starting at <paramref name="from"/>.</summary>
        private List<DateTime> run(DateTime from, DateTime to)
        {
            var dates = new List<DateTime>();
            int step = to >= from ? 1 : -1;

            for (var d = from; step > 0 ? d <= to : d >= to; d = d.AddDays(step))
            {
                if (isSelectable(d))
                    dates.Add(d);
            }

            return dates;
        }

        /// <summary><paramref name="first"/> followed by the dates of <paramref name="second"/> it doesn't have.</summary>
        private static List<DateTime> union(IEnumerable<DateTime> first, IEnumerable<DateTime> second)
        {
            var dates = first.ToList();
            var seen = new HashSet<DateTime>(dates);
            dates.AddRange(second.Where(seen.Add));
            return dates;
        }

        /// <summary>A day was clicked: select it (or extend, toggle or add a range, by mode and modifier keys).</summary>
        private void dayClicked(DateTime date, UIEvent e)
        {
            if (!isSelectable(date))
                return;

            bool shift = e?.ShiftPressed == true;
            bool control = e?.ControlPressed == true;

            switch (selectionMode)
            {
                case CalendarSelectionMode.None:
                    return;

                case CalendarSelectionMode.SingleDate:
                    pick(date);
                    return;

                case CalendarSelectionMode.SingleRange when shift && anchor != null:
                    setSelection(run(anchor.Value, date), false);
                    break;

                case CalendarSelectionMode.MultipleRange when shift && anchor != null:
                    setSelection(control ? union(SelectedDates, run(anchor.Value, date)) : run(anchor.Value, date), false);
                    break;

                case CalendarSelectionMode.MultipleRange when control:
                    anchor = date;
                    setSelection(selectedSet.Contains(date) ? SelectedDates.Where(d => d.Date != date).ToList() : union(SelectedDates, new[] { date }), false);
                    break;

                default:
                    anchor = date;
                    setSelection(new List<DateTime> { date }, false);
                    break;
            }

            currentDate = date;

            // A day of the next or previous month opens that month, as in single-date mode.
            if (!sameMonth(date, DisplayDate.Value))
                DisplayDate.Value = date;

            refreshStates();
            DatePicked?.Invoke(date);
        }

        /// <summary>The day cell under <paramref name="screenSpacePosition"/> in the month view.</summary>
        private CalendarDayButton dayAt(osuTK.Vector2 screenSpacePosition)
            => cells.OfType<CalendarDayButton>().FirstOrDefault(c => c.ReceivePositionalInputAt(screenSpacePosition));

        protected override bool OnDragStart(DragStartEvent e)
        {
            if (!selectsRanges || displayMode != CalendarMode.Month || e.Button != MouseButton.Left)
                return false;

            var start = dayAt(e.ScreenSpaceMouseDownPosition);
            if (start == null || !isSelectable(start.Date))
                return false;

            dragBase = selectionMode == CalendarSelectionMode.MultipleRange && e.ControlPressed ? SelectedDates.ToList() : new List<DateTime>();
            anchor = currentDate = start.Date;
            dragTo(start.Date);
            return true;
        }

        protected override void OnDrag(DragEvent e)
        {
            // The month view doesn't change during a drag, so a pointer past its edges keeps the last day it was over.
            if (dragBase != null && dayAt(e.ScreenSpaceMousePosition) is CalendarDayButton day)
                dragTo(day.Date);
        }

        protected override void OnDragEnd(DragEndEvent e) => dragBase = null;

        private void dragTo(DateTime date)
        {
            currentDate = date;
            setSelection(union(dragBase, run(anchor.Value, date)), false);
            refreshStates();
        }

        protected override void OnFocus(FocusEvent e)
        {
            base.OnFocus(e);
            refreshStates();
        }

        protected override void OnFocusLost(FocusLostEvent e)
        {
            base.OnFocusLost(e);
            refreshStates();
        }

        private void onDisplayDateChanged(ValueChangedEvent<DateTime> e)
        {
            refreshView();

            if (!samePeriod(e.OldValue, e.NewValue, displayMode))
                DisplayDateChanged?.Invoke(e.NewValue);
        }

        private static bool sameMonth(DateTime a, DateTime b) => a.Year == b.Year && a.Month == b.Month;

        private static bool samePeriod(DateTime a, DateTime b, CalendarMode mode) => periodStart(a, mode) == periodStart(b, mode);

        private static DateTime periodStart(DateTime date, CalendarMode mode) => mode switch
        {
            CalendarMode.Year => new DateTime(date.Year, 1, 1),
            CalendarMode.Decade => new DateTime(date.Year - date.Year % 10, 1, 1),
            _ => new DateTime(date.Year, date.Month, 1),
        };

        private static DateTime periodEnd(DateTime date, CalendarMode mode) => mode switch
        {
            CalendarMode.Year => periodStart(date, mode).AddYears(1).AddDays(-1),
            CalendarMode.Decade => periodStart(date, mode).AddYears(10).AddDays(-1),
            _ => periodStart(date, mode).AddMonths(1).AddDays(-1),
        };

        private void step(int direction)
        {
            var date = DisplayDate.Value;

            date = displayMode switch
            {
                CalendarMode.Year => date.AddYears(direction),
                CalendarMode.Decade => date.AddYears(10 * direction),
                _ => date.AddMonths(direction),
            };

            DisplayDate.Value = clamp(date);
        }

        private void zoomOut()
        {
            if (displayMode != CalendarMode.Decade)
                DisplayMode = displayMode + 1;
        }

        /// <summary>Opens the month (from a year view) or year (from a decade view) that <paramref name="date"/> is in.</summary>
        private void zoomIn(DateTime date)
        {
            if (displayMode == CalendarMode.Month)
                return;

            DisplayDate.Value = clamp(date);
            DisplayMode = displayMode - 1;
        }

        private void pick(DateTime date)
        {
            if (!isSelectable(date))
                return;

            SelectedDate.Value = date;
            DatePicked?.Invoke(date);
        }

        private void refreshView()
        {
            var period = periodStart(DisplayDate.Value, displayMode);

            if (viewValid && period == shownPeriod && displayMode == shownMode)
            {
                refreshStates();
                return;
            }

            int direction = viewValid && shownMode == displayMode ? period.CompareTo(shownPeriod) : 0;
            bool zoomed = viewValid && shownMode != displayMode;

            viewValid = true;
            shownPeriod = period;
            shownMode = displayMode;

            viewHost.Clear();
            cells.Clear();
            viewHost.Add(view = displayMode == CalendarMode.Month ? createMonthView(period) : createGridView(period));

            header.Text = displayMode switch
            {
                CalendarMode.Year => period.Year.ToString(culture),
                CalendarMode.Decade => $"{period.Year}-{period.Year + 9}",
                _ => period.ToString("MMMM yyyy", culture),
            };

            refreshStates();

            if (!IsLoaded)
                return;

            // The new page slides in from the side it comes from; zooming fades it in from slightly smaller or larger.
            if (direction != 0)
                view.MoveToX(direction * 24).MoveToX(0, 250, Easing.OutQuint);
            else if (zoomed)
                view.ScaleTo(displayMode > CalendarMode.Month ? 1.1f : 0.9f).ScaleTo(1, 250, Easing.OutQuint);

            view.FadeInFromZero(200, Easing.OutQuint);
        }

        private Container createMonthView(DateTime month)
        {
            var names = culture.DateTimeFormat.ShortestDayNames;
            var weekdays = new Container { RelativeSizeAxes = Axes.X, Height = weekday_height };

            for (int i = 0; i < 7; i++)
            {
                weekdays.Add(new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    RelativePositionAxes = Axes.X,
                    Width = 1f / 7,
                    X = i / 7f,
                    Child = new TextBlock
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Text = names[((int)firstDayOfWeek + i) % 7],
                        FontSize = 12,
                        FontWeight = FontWeight.Bold,
                        Colour = Theme.SubtleText,
                    },
                });
            }

            var days = new Container { RelativeSizeAxes = Axes.Both, Padding = new MarginPadding { Top = weekday_height } };
            int offset = ((int)month.DayOfWeek - (int)firstDayOfWeek + 7) % 7;
            var start = month.AddDays(-offset);

            for (int i = 0; i < 42; i++)
            {
                var date = start.AddDays(i);
                days.Add(place(new CalendarDayButton(date, dayClicked), i % 7, i / 7, 7, 6));
            }

            return new Container
            {
                RelativeSizeAxes = Axes.Both,
                Children = new Drawable[] { weekdays, days },
            };
        }

        /// <summary>The 4×3 grid of months (year view) or years (decade view, with the neighbouring years dimmed at the ends).</summary>
        private Container createGridView(DateTime period)
        {
            var grid = new Container { RelativeSizeAxes = Axes.Both };

            for (int i = 0; i < 12; i++)
            {
                var cell = displayMode == CalendarMode.Year
                    ? new CalendarButton(period.AddMonths(i), culture.DateTimeFormat.AbbreviatedMonthNames[i], CalendarMode.Year, (d, _) => zoomIn(d))
                    : new CalendarButton(period.AddYears(i - 1), (period.Year + i - 1).ToString(culture), CalendarMode.Decade, (d, _) => zoomIn(d));

                grid.Add(place(cell, i % 4, i / 4, 4, 3));
            }

            return grid;
        }

        private Drawable place(CalendarButton cell, int column, int row, int columns, int rows)
        {
            cells.Add(cell);

            cell.RelativeSizeAxes = Axes.Both;
            cell.RelativePositionAxes = Axes.Both;
            cell.Size = new Vector2(1f / columns, 1f / rows);
            cell.Position = new Vector2((float)column / columns, (float)row / rows);
            return cell;
        }

        /// <summary>Re-applies selection, today, range and keyboard highlight to the cells shown.</summary>
        private void refreshStates()
        {
            var today = DateTime.Today;
            var display = DisplayDate.Value;

            foreach (var cell in cells)
            {
                if (cell is CalendarDayButton day)
                {
                    day.IsSelected = selectedSet.Contains(day.Date);
                    day.IsToday = isTodayHighlighted && day.Date == today;
                    day.IsInactive = !sameMonth(day.Date, shownPeriod);
                    day.IsEnabled = isSelectable(day.Date);
                    // With ranges the keyboard position isn't always a selected date, so it gets the ring the other views use.
                    day.IsHighlighted = selectsRanges && HasFocus && day.Date == currentDate;
                    continue;
                }

                var first = periodStart(cell.Date, cell.Mode == CalendarMode.Year ? CalendarMode.Month : CalendarMode.Year);
                var last = periodEnd(cell.Date, cell.Mode == CalendarMode.Year ? CalendarMode.Month : CalendarMode.Year);

                cell.IsSelected = selectedSet.Any(selected => selected >= first && selected <= last);
                cell.IsToday = isTodayHighlighted && today >= first && today <= last;
                cell.IsInactive = cell.Mode == CalendarMode.Decade && !samePeriod(cell.Date, shownPeriod, CalendarMode.Decade);
                cell.IsEnabled = (displayDateStart == null || last >= displayDateStart) && (displayDateEnd == null || first <= displayDateEnd);
                cell.IsHighlighted = display >= first && display <= last;
            }

            previous.IsEnabled = displayDateStart == null || periodStart(DisplayDate.Value, displayMode) > displayDateStart;
            next.IsEnabled = displayDateEnd == null || periodEnd(DisplayDate.Value, displayMode) < displayDateEnd;
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            // Alt+arrows belong to a DatePicker hosting this calendar, which closes on them.
            if (!HasFocus || e.AltPressed)
                return false;

            if (displayMode == CalendarMode.Month)
                return monthKey(e);

            return gridKey(e);
        }

        private bool monthKey(KeyDownEvent e)
        {
            var cursor = (selectionMode == CalendarSelectionMode.SingleDate ? SelectedDate.Value : currentDate ?? SelectedDate.Value) ?? DisplayDate.Value.Date;

            switch (e.Key)
            {
                case Key.Up when e.ControlPressed:
                    zoomOut();
                    return true;

                case Key.Left:
                    cursor = cursor.AddDays(-1);
                    break;

                case Key.Right:
                    cursor = cursor.AddDays(1);
                    break;

                case Key.Up:
                    cursor = cursor.AddDays(-7);
                    break;

                case Key.Down:
                    cursor = cursor.AddDays(7);
                    break;

                case Key.PageUp:
                    cursor = cursor.AddMonths(-1);
                    break;

                case Key.PageDown:
                    cursor = cursor.AddMonths(1);
                    break;

                case Key.Home:
                    cursor = periodStart(cursor, CalendarMode.Month);
                    break;

                case Key.End:
                    cursor = periodEnd(cursor, CalendarMode.Month);
                    break;

                case Key.Enter:
                case Key.KeypadEnter:
                case Key.Space:
                    // Ctrl+Space toggles the date in MultipleRange; otherwise it is picked as a plain click would.
                    dayClicked(cursor, e.ControlPressed && selectionMode == CalendarSelectionMode.MultipleRange ? e : null);
                    return true;

                default:
                    return false;
            }

            cursor = clamp(cursor);

            switch (selectionMode)
            {
                case CalendarSelectionMode.SingleDate:
                    SelectedDate.Value = cursor;
                    return true;

                case CalendarSelectionMode.None:
                    break;

                // Shift+arrows extend from where the range started; plain arrows move a one-date selection.
                case CalendarSelectionMode.SingleRange:
                case CalendarSelectionMode.MultipleRange:
                    if (e.ShiftPressed)
                    {
                        anchor ??= currentDate ?? cursor;
                        setSelection(run(anchor.Value, cursor), false);
                    }
                    else
                    {
                        anchor = cursor;
                        setSelection(new List<DateTime> { cursor }, false);
                    }

                    break;
            }

            currentDate = cursor;

            if (!sameMonth(cursor, DisplayDate.Value))
                DisplayDate.Value = cursor;

            refreshStates();
            return true;
        }

        private bool gridKey(KeyDownEvent e)
        {
            var cursor = DisplayDate.Value;
            bool year = displayMode == CalendarMode.Year;

            switch (e.Key)
            {
                case Key.Up when e.ControlPressed:
                    zoomOut();
                    return true;

                case Key.Down when e.ControlPressed:
                case Key.Enter:
                case Key.KeypadEnter:
                case Key.Space:
                    zoomIn(cursor);
                    return true;

                case Key.Left:
                    cursor = year ? cursor.AddMonths(-1) : cursor.AddYears(-1);
                    break;

                case Key.Right:
                    cursor = year ? cursor.AddMonths(1) : cursor.AddYears(1);
                    break;

                case Key.Up:
                    cursor = year ? cursor.AddMonths(-4) : cursor.AddYears(-4);
                    break;

                case Key.Down:
                    cursor = year ? cursor.AddMonths(4) : cursor.AddYears(4);
                    break;

                case Key.PageUp:
                    cursor = cursor.AddYears(year ? -1 : -10);
                    break;

                case Key.PageDown:
                    cursor = cursor.AddYears(year ? 1 : 10);
                    break;

                default:
                    return false;
            }

            DisplayDate.Value = clamp(cursor);
            return true;
        }
    }

    /// <summary>
    /// A month (in a calendar's year view) or a year (in its decade view). Clicking it opens that month or year.
    /// <see cref="CalendarDayButton"/> is the same for one day.
    /// </summary>
    public partial class CalendarButton : CompositeDrawable
    {
        private readonly Container face;
        private readonly Box fill;
        private readonly TextBlock text;
        private readonly Action<DateTime, UIEvent> clicked;

        private bool isSelected;
        private bool isToday;
        private bool isInactive;
        private bool isEnabled = true;
        private bool isHighlighted;

        internal CalendarButton(DateTime date, string label, CalendarMode mode, Action<DateTime, UIEvent> clicked)
        {
            Date = date;
            Mode = mode;
            this.clicked = clicked;

            Padding = new MarginPadding(2);
            InternalChild = face = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 6,
                BorderColour = Theme.Accent,
                Children = new Drawable[]
                {
                    fill = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0 },
                    text = new TextBlock { Anchor = Anchor.Centre, Origin = Anchor.Centre, Text = label, FontSize = 14 },
                },
            };
        }

        /// <summary>The day, or the first day of the month or year, this cell stands for.</summary>
        public DateTime Date { get; }

        /// <summary>The view the cell belongs to.</summary>
        public CalendarMode Mode { get; }

        public bool IsSelected
        {
            get => isSelected;
            set => setState(ref isSelected, value);
        }

        /// <summary>Contains today's date.</summary>
        public bool IsToday
        {
            get => isToday;
            set => setState(ref isToday, value);
        }

        /// <summary>Belongs to a neighbouring month or decade and is drawn dimmed.</summary>
        public bool IsInactive
        {
            get => isInactive;
            set => setState(ref isInactive, value);
        }

        /// <summary>False outside the calendar's DisplayDateStart/DisplayDateEnd.</summary>
        public bool IsEnabled
        {
            get => isEnabled;
            set => setState(ref isEnabled, value);
        }

        /// <summary>The keyboard position in year and decade views.</summary>
        public bool IsHighlighted
        {
            get => isHighlighted;
            set => setState(ref isHighlighted, value);
        }

        private void setState(ref bool field, bool value)
        {
            if (field == value)
                return;

            field = value;
            updateState(IsLoaded ? 100 : 0);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            updateState(0);
        }

        protected override bool OnHover(HoverEvent e)
        {
            updateState(100);
            return true;
        }

        protected override void OnHoverLost(HoverLostEvent e) => updateState(250);

        protected override bool OnClick(ClickEvent e)
        {
            if (isEnabled)
                clicked?.Invoke(Date, e);

            return true;
        }

        private void updateState(double duration)
        {
            bool hovered = IsHovered && isEnabled;

            fill.FadeColour(isSelected ? Theme.Accent : Theme.Text, duration, Easing.OutQuint);
            fill.FadeTo(isSelected ? 1 : hovered ? 0.1f : 0, duration, Easing.OutQuint);

            text.FadeColour(isSelected ? Theme.AccentText : isInactive ? Theme.Overlay : Theme.Text, duration, Easing.OutQuint);

            face.BorderColour = isToday ? Theme.Accent : Theme.SubtleText;
            face.BorderThickness = isToday ? 1.5f : isHighlighted ? 1 : 0;

            this.FadeTo(isEnabled ? 1 : 0.3f, duration, Easing.OutQuint);
        }
    }

    /// <summary>One day in a <see cref="Calendar"/>'s month view.</summary>
    public partial class CalendarDayButton : CalendarButton
    {
        internal CalendarDayButton(DateTime date, Action<DateTime, UIEvent> clicked)
            : base(date, date.Day.ToString(CultureInfo.CurrentCulture), CalendarMode.Month, clicked)
        {
        }
    }

    /// <summary>A flat button in the calendar header: an arrow icon, or the title text.</summary>
    internal partial class CalendarHeaderButton : CompositeDrawable
    {
        private readonly Box hover;
        private readonly TextBlock label;
        private bool isEnabled = true;

        public CalendarHeaderButton(IconUsage? icon = null)
        {
            Height = 28;
            Masking = true;
            CornerRadius = 6;

            Drawable content;

            if (icon is IconUsage i)
            {
                Width = 28;
                content = new SpriteIcon { Anchor = Anchor.Centre, Origin = Anchor.Centre, Size = new Vector2(12), Icon = i, Colour = Theme.SubtleText };
            }
            else
            {
                AutoSizeAxes = Axes.X;
                content = label = new TextBlock { Anchor = Anchor.Centre, Origin = Anchor.Centre, FontWeight = FontWeight.Bold, Margin = new MarginPadding { Horizontal = 10 } };
            }

            InternalChildren = new[]
            {
                hover = new Box { RelativeSizeAxes = Axes.Both, Colour = Theme.Text, Alpha = 0 },
                content,
            };
        }

        public Action Action { get; set; }

        public string Text
        {
            get => label?.Text.ToString();
            set
            {
                if (label != null)
                    label.Text = value;
            }
        }

        public bool IsEnabled
        {
            get => isEnabled;
            set
            {
                isEnabled = value;
                this.FadeTo(value ? 1 : 0.3f, 100, Easing.OutQuint);
            }
        }

        protected override bool OnHover(HoverEvent e)
        {
            if (isEnabled)
                hover.FadeTo(0.1f, 100, Easing.OutQuint);
            return true;
        }

        protected override void OnHoverLost(HoverLostEvent e) => hover.FadeOut(250, Easing.OutQuint);

        protected override bool OnClick(ClickEvent e)
        {
            if (!isEnabled)
                return true;

            hover.FadeTo(0.25f).FadeTo(0.1f, 300, Easing.OutQuint);
            Action?.Invoke();
            return true;
        }
    }
}

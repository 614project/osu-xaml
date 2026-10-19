using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Testing;
using OsuXaml.Controls;
using osuTK;
using osuTK.Input;

namespace OsuXaml.Tests
{
    /// <summary>Calendar and DatePicker driven with real mouse and keyboard input.</summary>
    [HeadlessTest]
    public partial class CalendarTests : ManualInputManagerTestScene
    {
        private static readonly DateTime october16 = new DateTime(2026, 10, 16);

        private CalendarDayButton day(Drawable owner, DateTime date) => owner.ChildrenOfType<CalendarDayButton>().Single(d => d.Date == date);

        private void click(Drawable drawable)
        {
            InputManager.MoveMouseTo(drawable);
            InputManager.Click(MouseButton.Left);
        }

        [Test]
        public void MonthGridStartsOnTheFirstDayOfWeek()
        {
            Calendar calendar = null;

            AddStep("create", () => Child = calendar = new Calendar { FirstDayOfWeek = DayOfWeek.Sunday, DisplayDate = { Value = october16 } });

            AddAssert("six weeks of days", () => calendar.ChildrenOfType<CalendarDayButton>().Count() == 42);
            AddAssert("starts on the Sunday before the 1st", () => calendar.ChildrenOfType<CalendarDayButton>().First().Date == new DateTime(2026, 9, 27));
            AddAssert("neighbouring months dimmed", () => day(calendar, new DateTime(2026, 9, 30)).IsInactive && !day(calendar, october16).IsInactive);

            AddStep("monday first", () => calendar.FirstDayOfWeek = DayOfWeek.Monday);
            AddAssert("starts on the Monday", () => calendar.ChildrenOfType<CalendarDayButton>().First().Date == new DateTime(2026, 9, 28));

            AddStep("show this month", () => calendar.DisplayDate.Value = DateTime.Today);
            AddAssert("today ringed", () => calendar.ChildrenOfType<CalendarDayButton>().Single(d => d.IsToday).Date == DateTime.Today);
        }

        [Test]
        public void ClickingADaySelectsItTwoWay()
        {
            var birthday = new Bindable<DateTime?>();
            Calendar calendar = null;
            DateTime? reported = null;

            AddStep("create", () =>
            {
                calendar = new Calendar { DisplayDate = { Value = october16 } };
                XamlBinding.TwoWay(calendar.SelectedDate, birthday);
                calendar.SelectedDatesChanged += d => reported = d;
                Child = calendar;
            });

            AddStep("click the 20th", () => click(day(calendar, new DateTime(2026, 10, 20))));
            AddAssert("selected and reported", () => birthday.Value == new DateTime(2026, 10, 20) && reported == birthday.Value);
            AddAssert("cell shows it", () => day(calendar, new DateTime(2026, 10, 20)).IsSelected);

            AddStep("click a November day in the last row", () => click(calendar.ChildrenOfType<CalendarDayButton>().Last()));
            AddAssert("moves to November", () => birthday.Value?.Month == 11 && calendar.DisplayDate.Value.Month == 11);

            AddStep("view model picks a time in March", () => birthday.Value = new DateTime(2027, 3, 3, 15, 30, 0));
            AddAssert("time dropped, calendar follows", () => birthday.Value == new DateTime(2027, 3, 3) && calendar.DisplayDate.Value.Month == 3
                                                             && day(calendar, new DateTime(2027, 3, 3)).IsSelected);
        }

        private static DateTime oct(int day) => new DateTime(2026, 10, day);

        private static string dates(System.Collections.Generic.IEnumerable<DateTime> list) => string.Join(",", list.Select(d => d.Day));

        private void clickWith(Key modifier, Drawable drawable)
        {
            InputManager.PressKey(modifier);
            click(drawable);
            InputManager.ReleaseKey(modifier);
        }

        private void drag(Drawable from, Drawable to, Key? modifier = null)
        {
            if (modifier is Key key)
                InputManager.PressKey(key);

            InputManager.MoveMouseTo(from);
            InputManager.PressButton(MouseButton.Left);
            InputManager.MoveMouseTo(to);
            InputManager.ReleaseButton(MouseButton.Left);

            if (modifier is Key held)
                InputManager.ReleaseKey(held);
        }

        [Test]
        public void SingleRangeByShiftClickAndDrag()
        {
            var days = new BindableList<DateTime>();
            Calendar calendar = null;
            int reports = 0;

            AddStep("create", () =>
            {
                reports = 0;
                days.Clear();
                calendar = new Calendar { SelectionMode = CalendarSelectionMode.SingleRange, DisplayDate = { Value = october16 } };
                XamlBinding.TwoWayList(calendar.SelectedDates, days);
                calendar.SelectedDatesChanged += _ => reports++;
                Child = calendar;
            });

            AddStep("click the 5th", () => click(day(calendar, oct(5))));
            AddStep("shift+click the 8th", () => clickWith(Key.ShiftLeft, day(calendar, oct(8))));
            AddAssert("5th to 8th, bound list follows", () => dates(calendar.SelectedDates) == "5,6,7,8" && dates(days) == "5,6,7,8"
                                                            && calendar.SelectedDate.Value == oct(5) && reports == 2);
            AddAssert("cells show the run", () => calendar.ChildrenOfType<CalendarDayButton>().Count(d => d.IsSelected) == 4 && day(calendar, oct(7)).IsSelected);

            AddStep("shift+click the 3rd", () => clickWith(Key.ShiftLeft, day(calendar, oct(3))));
            AddAssert("runs back from the 5th", () => dates(calendar.SelectedDates) == "5,4,3");

            AddStep("drag from the 12th to the 14th", () => drag(day(calendar, oct(12)), day(calendar, oct(14))));
            AddAssert("12th to 14th", () => dates(calendar.SelectedDates) == "12,13,14" && calendar.SelectedDate.Value == oct(12));

            AddStep("view model adds the 20th", () => days.Add(oct(20)));
            AddAssert("shown, SelectedDate unchanged", () => day(calendar, oct(20)).IsSelected && calendar.SelectedDate.Value == oct(12));

            AddStep("set SelectedDate", () => calendar.SelectedDate.Value = oct(1));
            AddAssert("only that date, as in WPF", () => dates(calendar.SelectedDates) == "1" && dates(days) == "1");

            AddStep("view model clears", () => days.Clear());
            AddAssert("nothing selected", () => calendar.SelectedDate.Value == null && !calendar.ChildrenOfType<CalendarDayButton>().Any(d => d.IsSelected));
        }

        [Test]
        public void MultipleRangeTogglesAndAddsRuns()
        {
            Calendar calendar = null;

            AddStep("create", () => Child = calendar = new Calendar
            {
                SelectionMode = CalendarSelectionMode.MultipleRange,
                DisplayDate = { Value = october16 },
                DisplayDateEnd = oct(28),
            });

            AddStep("click the 5th, ctrl+click the 7th and 9th", () =>
            {
                click(day(calendar, oct(5)));
                clickWith(Key.ControlLeft, day(calendar, oct(7)));
                clickWith(Key.ControlLeft, day(calendar, oct(9)));
            });
            AddAssert("three dates", () => dates(calendar.SelectedDates) == "5,7,9");

            AddStep("ctrl+click the 7th again", () => clickWith(Key.ControlLeft, day(calendar, oct(7))));
            AddAssert("toggled off", () => dates(calendar.SelectedDates) == "5,9");

            AddStep("ctrl+drag the 12th to the 14th", () => drag(day(calendar, oct(12)), day(calendar, oct(14)), Key.ControlLeft));
            AddAssert("run added", () => dates(calendar.SelectedDates) == "5,9,12,13,14");

            AddStep("ctrl+shift+click the 16th", () =>
            {
                InputManager.PressKey(Key.ControlLeft);
                clickWith(Key.ShiftLeft, day(calendar, oct(16)));
                InputManager.ReleaseKey(Key.ControlLeft);
            });
            AddAssert("run from the 12th added", () => dates(calendar.SelectedDates) == "5,9,12,13,14,15,16");

            AddStep("drag past DisplayDateEnd", () => drag(day(calendar, oct(26)), day(calendar, oct(30))));
            AddAssert("stops at the end", () => dates(calendar.SelectedDates) == "26,27,28");

            AddStep("plain click", () => click(day(calendar, oct(20))));
            AddAssert("just the 20th", () => dates(calendar.SelectedDates) == "20" && calendar.HasFocus);
            AddAssert("keyboard position ringed", () => day(calendar, oct(20)).IsHighlighted);

            AddStep("shift+right twice", () =>
            {
                InputManager.PressKey(Key.ShiftLeft);
                InputManager.Key(Key.Right);
                InputManager.Key(Key.Right);
                InputManager.ReleaseKey(Key.ShiftLeft);
            });
            AddAssert("extended", () => dates(calendar.SelectedDates) == "20,21,22" && day(calendar, oct(22)).IsHighlighted);

            AddStep("right", () => InputManager.Key(Key.Right));
            AddAssert("moves a one-date selection", () => dates(calendar.SelectedDates) == "23");

            AddStep("ctrl+space on the 23rd", () =>
            {
                InputManager.PressKey(Key.ControlLeft);
                InputManager.Key(Key.Space);
                InputManager.ReleaseKey(Key.ControlLeft);
            });
            AddAssert("toggled off", () => calendar.SelectedDates.Count == 0 && calendar.SelectedDate.Value == null);

            AddStep("back to single date", () =>
            {
                calendar.SelectedDates.AddRange(new[] { oct(2), oct(3) });
                calendar.SelectionMode = CalendarSelectionMode.SingleDate;
            });
            AddAssert("keeps the first", () => dates(calendar.SelectedDates) == "2");

            AddStep("no selection", () => calendar.SelectionMode = CalendarSelectionMode.None);
            AddStep("click the 9th", () => click(day(calendar, oct(9))));
            AddAssert("ignored", () => dates(calendar.SelectedDates) == "2");
        }

        [Test]
        public void NavigationButtonsZoomAndRange()
        {
            Calendar calendar = null;

            AddStep("create", () => Child = calendar = new Calendar
            {
                DisplayDate = { Value = october16 },
                DisplayDateStart = new DateTime(2026, 9, 10),
            });

            AddStep("next", () => click(calendar.NextButton));
            AddAssert("November", () => calendar.DisplayDate.Value.Month == 11);
            AddStep("previous", () => click(calendar.PreviousButton));
            AddStep("previous", () => click(calendar.PreviousButton));
            AddAssert("September", () => calendar.DisplayDate.Value.Month == 9);
            AddStep("previous again", () => click(calendar.PreviousButton));
            AddAssert("stops at DisplayDateStart", () => calendar.DisplayDate.Value.Month == 9);
            AddUntilStep("previous button dimmed", () => calendar.PreviousButton.Alpha < 0.5f);
            AddAssert("days before the start disabled", () => !day(calendar, new DateTime(2026, 9, 9)).IsEnabled && day(calendar, new DateTime(2026, 9, 10)).IsEnabled);

            AddStep("click the title", () => click(calendar.HeaderButton));
            AddAssert("year view", () => calendar.DisplayMode == CalendarMode.Year && calendar.ChildrenOfType<CalendarButton>().Count() == 12);
            AddStep("click the title again", () => click(calendar.HeaderButton));
            AddAssert("decade view", () => calendar.DisplayMode == CalendarMode.Decade && calendar.HeaderText == "2020-2029");
            AddStep("click 2027", () => click(calendar.ChildrenOfType<CalendarButton>().Single(b => b.Date.Year == 2027)));
            AddAssert("year view of 2027", () => calendar.DisplayMode == CalendarMode.Year && calendar.DisplayDate.Value.Year == 2027);
            AddStep("click March", () => click(calendar.ChildrenOfType<CalendarButton>().Single(b => b.Date.Month == 3)));
            AddAssert("March 2027", () => calendar.DisplayMode == CalendarMode.Month && calendar.DisplayDate.Value == new DateTime(2027, 3, 1)
                                          && calendar.ChildrenOfType<CalendarDayButton>().Count() == 42);
        }

        [Test]
        public void ArrowKeysMoveTheSelection()
        {
            Calendar calendar = null;

            AddStep("create", () => Child = calendar = new Calendar { DisplayDate = { Value = october16 } });
            AddStep("click the 16th", () => click(day(calendar, october16)));
            AddAssert("focused", () => calendar.HasFocus);

            AddStep("right", () => InputManager.Key(Key.Right));
            AddAssert("17th", () => calendar.SelectedDate.Value == new DateTime(2026, 10, 17));
            AddStep("down", () => InputManager.Key(Key.Down));
            AddAssert("a week later", () => calendar.SelectedDate.Value == new DateTime(2026, 10, 24));
            AddStep("page down", () => InputManager.Key(Key.PageDown));
            AddAssert("a month later, shown", () => calendar.SelectedDate.Value == new DateTime(2026, 11, 24) && calendar.DisplayDate.Value.Month == 11);
            AddStep("end", () => InputManager.Key(Key.End));
            AddAssert("end of month", () => calendar.SelectedDate.Value == new DateTime(2026, 11, 30));

            AddStep("ctrl+up", () =>
            {
                InputManager.PressKey(Key.ControlLeft);
                InputManager.Key(Key.Up);
                InputManager.ReleaseKey(Key.ControlLeft);
            });
            AddAssert("year view", () => calendar.DisplayMode == CalendarMode.Year);
            AddStep("left, enter", () =>
            {
                InputManager.Key(Key.Left);
                InputManager.Key(Key.Enter);
            });
            AddAssert("October again", () => calendar.DisplayMode == CalendarMode.Month && calendar.DisplayDate.Value.Month == 10);
        }

        [Test]
        public void DatePickerOpensPicksAndCloses()
        {
            var due = new Bindable<DateTime?>();
            Window window = null;
            DatePicker picker = null;
            int opened = 0, closed = 0;

            AddStep("create", () =>
            {
                var panel = new StackPanel { Width = 300, Margin = new MarginPadding(20) };
                panel.Add(picker = new DatePicker { DateFormat = "yyyy-MM-dd", DisplayDate = { Value = october16 } });
                XamlBinding.TwoWay(picker.SelectedDate, due);
                picker.CalendarOpened += () => opened++;
                picker.CalendarClosed += () => closed++;
                Child = window = new Window { Child = panel };
            });

            AddAssert("empty with a hint", () => picker.Text == string.Empty && picker.PlaceholderText == "Select a date");
            AddUntilStep("stretched in its panel", () => picker.DrawWidth == 300 && picker.DrawHeight == 36);

            AddStep("click the calendar button", () => click(picker.ChildrenOfType<osu.Framework.Graphics.Sprites.SpriteIcon>().Single().Parent));
            AddAssert("open in the window's popup layer", () => picker.IsDropDownOpen.Value && opened == 1 && window.PopupLayer.OpenPopups.Count == 1);
            AddUntilStep("calendar below the box", () => picker.Calendar.IsPresent
                                                         && Math.Abs(picker.Calendar.ScreenSpaceDrawQuad.TopLeft.Y - picker.ScreenSpaceDrawQuad.BottomLeft.Y - 4) < 1);

            AddStep("click the 3rd", () => click(day(picker.Calendar, new DateTime(2026, 10, 3))));
            AddAssert("picked, closed, shown", () => due.Value == new DateTime(2026, 10, 3) && !picker.IsDropDownOpen.Value && closed == 1
                                                     && picker.Text == "2026-10-03");

            AddStep("reopen", () => click(picker.ChildrenOfType<osu.Framework.Graphics.Sprites.SpriteIcon>().Single().Parent));
            AddUntilStep("calendar has focus", () => picker.Calendar.HasFocus);
            AddAssert("opened on the selected month", () => picker.Calendar.DisplayDate.Value.Month == 10);
            AddStep("right arrow", () => InputManager.Key(Key.Right));
            AddAssert("selection moved, still open", () => due.Value == new DateTime(2026, 10, 4) && picker.IsDropDownOpen.Value);
            AddStep("enter", () => InputManager.Key(Key.Enter));
            AddAssert("closed by enter", () => !picker.IsDropDownOpen.Value && picker.Text == "2026-10-04");

            AddStep("view model clears the date", () => due.Value = null);
            AddAssert("box empty", () => picker.Text == string.Empty);
        }

        [Test]
        public void DatePickerReadsTypedDates()
        {
            DatePicker picker = null;
            string rejected = null;
            OsuXaml.Controls.TextBox box = null;

            AddStep("create", () =>
            {
                picker = new DatePicker { Width = 250, DateFormat = "yyyy-MM-dd", SelectedDate = { Value = october16 } };
                picker.DateValidationError += t => rejected = t;
                Child = new Window { Child = picker };
                box = picker.ChildrenOfType<OsuXaml.Controls.TextBox>().Single();
            });

            AddAssert("shows the date", () => picker.Text == "2026-10-16");

            AddStep("focus the box", () => click(box));
            AddStep("type a date and press enter", () =>
            {
                box.Text.Value = "2027-01-05";
                InputManager.Key(Key.Enter);
            });
            AddAssert("read", () => picker.SelectedDate.Value == new DateTime(2027, 1, 5) && picker.Text == "2027-01-05");

            AddStep("focus the box", () => click(box));
            AddStep("type nonsense", () =>
            {
                box.Text.Value = "tomorrow-ish";
                InputManager.Key(Key.Enter);
            });
            AddAssert("rejected and put back", () => rejected == "tomorrow-ish" && picker.SelectedDate.Value == new DateTime(2027, 1, 5) && picker.Text == "2027-01-05");

            AddStep("focus the box", () => click(box));
            AddStep("clear and press enter", () =>
            {
                box.Text.Value = string.Empty;
                InputManager.Key(Key.Enter);
            });
            AddAssert("no date", () => picker.SelectedDate.Value == null);

            AddStep("long format", () =>
            {
                picker.DateFormat = null;
                picker.SelectedDateFormat = DatePickerFormat.Long;
                picker.SelectedDate.Value = october16;
            });
            AddAssert("culture's long date", () => picker.Text == october16.ToString("D", System.Globalization.CultureInfo.CurrentCulture));
        }

        [Test]
        public void DatePickerOpensFromTheKeyboard()
        {
            DatePicker picker = null;
            OsuXaml.Controls.TextBox box = null;

            AddStep("create", () =>
            {
                picker = new DatePicker { Width = 250, SelectedDate = { Value = october16 } };
                Child = new Window { Child = picker };
                box = picker.ChildrenOfType<OsuXaml.Controls.TextBox>().Single();
            });

            AddStep("focus the box", () => click(box));
            AddAssert("box focused", () => box.HasFocus);
            AddStep("F4", () => InputManager.Key(Key.F4));
            AddAssert("open", () => picker.IsDropDownOpen.Value);
            AddUntilStep("calendar has focus", () => picker.Calendar.HasFocus);
            AddStep("F4 again", () => InputManager.Key(Key.F4));
            AddAssert("closed", () => !picker.IsDropDownOpen.Value);

            AddStep("focus the box", () => click(box));
            AddStep("alt+down", () =>
            {
                InputManager.PressKey(Key.AltLeft);
                InputManager.Key(Key.Down);
                InputManager.ReleaseKey(Key.AltLeft);
            });
            AddAssert("open", () => picker.IsDropDownOpen.Value);
            AddUntilStep("calendar has focus", () => picker.Calendar.HasFocus);
            AddStep("alt+up", () =>
            {
                InputManager.PressKey(Key.AltLeft);
                InputManager.Key(Key.Up);
                InputManager.ReleaseKey(Key.AltLeft);
            });
            AddAssert("closed, date kept", () => !picker.IsDropDownOpen.Value && picker.SelectedDate.Value == october16);

            AddStep("F4", () =>
            {
                click(box);
                InputManager.Key(Key.F4);
            });
            AddAssert("open", () => picker.IsDropDownOpen.Value);
            AddStep("escape", () => InputManager.Key(Key.Escape));
            AddAssert("closed by escape", () => !picker.IsDropDownOpen.Value);
        }

        [Test]
        public void DatePickerWithoutAPanelGetsAWidth()
        {
            DatePicker picker = null;

            AddStep("create", () => Child = new Window { Child = new osu.Framework.Graphics.Containers.Container { AutoSizeAxes = Axes.Both, Child = picker = new DatePicker() } });
            AddUntilStep("default width", () => picker.DrawWidth == 200 && picker.DrawHeight == 36);
            AddAssert("measures like a text box", () => picker.Measure(new Vector2(float.PositiveInfinity)) == new Vector2(200, 36));
        }
    }
}

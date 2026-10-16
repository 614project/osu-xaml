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

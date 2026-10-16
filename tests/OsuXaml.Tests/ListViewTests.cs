using System;
using System.ComponentModel;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using OsuXaml.Controls;
using osuTK;
using Visibility = OsuXaml.Controls.Visibility;

namespace OsuXaml.Tests
{
    /// <summary>Models and XAML scaffolding shared by the ListView, DataGrid and TreeView tests.</summary>
    public static class ListsTestData
    {
        public sealed class Person
        {
            public readonly Bindable<string> Name = new Bindable<string>();
            public readonly BindableInt Age = new BindableInt();
            public readonly BindableBool Active = new BindableBool();
            public string Email = "";

            public Person(string name, int age, bool active = false)
            {
                Name.Value = name;
                Age.Value = age;
                Active.Value = active;
                Email = name.ToLowerInvariant() + "@example.com";
            }

            public override string ToString() => Name.Value;
        }

        public sealed class Node
        {
            public readonly string Label;
            public readonly BindableList<Node> Children = new BindableList<Node>();
            public readonly BindableBool IsExpanded = new BindableBool();

            public Node(string label, params Node[] children)
            {
                Label = label;
                Children.AddRange(children);
            }

            public override string ToString() => Label;
        }

        public static BindableList<Person> People() => new BindableList<Person>(new[]
        {
            new Person("Cid", 41, true),
            new Person("Ann", 29),
            new Person("Bob", 35, true),
        });

        public static readonly ItemBinding<Person, string> NameBinding = new ItemBinding<Person, string>("Name", p => p.Name, p => p.Name.Value);
        public static readonly ItemBinding<Person, int> AgeBinding = new ItemBinding<Person, int>("Age", p => p.Age, p => p.Age.Value);
        public static readonly ItemBinding<Person, bool> ActiveBinding = new ItemBinding<Person, bool>("Active", p => p.Active, p => p.Active.Value);
        public static readonly ItemBinding<Person, string> EmailBinding = new ItemBinding<Person, string>("Email", p => p.Email, p => p.Email);

        /// <summary>A text template like the one <c>DisplayMemberBinding="{Bind Name}"</c> compiles to.</summary>
        public static DataTemplate<Person> Text(Func<Person, IBindable<string>> member) => new DataTemplate<Person>((p, scope) =>
        {
            var text = new TextBlock();
            XamlBinding.OneWay(scope, member(p), (v, _) => text.Text = v);
            return text;
        });

        public static string TextOf(Drawable drawable) => (drawable as TextBlock)?.Text.ToString() ?? ((drawable as ContentControl)?.PresentedContent as TextBlock)?.Text.ToString();

        public static bool Near(float a, float b) => Math.Abs(a - b) < 0.5f;

        /// <summary>View model and code-behind for generator tests.</summary>
        public const string CODE_BEHIND = @"
using osu.Framework.Bindables;
using osu.Framework.Graphics.Containers;
using OsuXaml.Controls;

namespace Demo
{
    public class Vm
    {
        public BindableList<Person> People { get; } = new BindableList<Person>();
        public Bindable<Person> Selected { get; } = new Bindable<Person>();
        public BindableInt SelectedIndex { get; } = new BindableInt();
        public BindableList<Node> Roots { get; } = new BindableList<Node>();
        public Bindable<Node> SelectedNode { get; } = new Bindable<Node>();
        public BindableBool ReadOnly { get; } = new BindableBool();
        public Bindable<string> Title { get; } = new Bindable<string>(""Name"");
        public void Remove(Person person) { }
        public void Refresh() { }
    }

    public class Person
    {
        public Bindable<string> Name { get; } = new Bindable<string>();
        public BindableInt Age { get; } = new BindableInt();
        public BindableBool Active { get; } = new BindableBool();
        public string Email { get; set; }
    }

    public class Node
    {
        public string Label { get; set; }
        public BindableList<Node> Children { get; } = new BindableList<Node>();
        public BindableBool IsExpanded { get; } = new BindableBool();
    }

    public partial class View : CompositeDrawable
    {
        private void OnHeaderClick(GridViewColumn column) { }
        private void OnSelected(object item) { }
        private void OnSorting() { }
    }
}";

        public static string Page(string content) => @"<CompositeDrawable xmlns=""osu-xaml"" xmlns:x=""osu-xaml:x"" xmlns:local=""clr-namespace:Demo""
    xmlns:sys=""clr-namespace:System"" x:Class=""Demo.View"" x:DataType=""local:Vm"">
" + content + @"
</CompositeDrawable>";

        public static string Compiles(string content) => GeneratorRunner.Compiles(CODE_BEHIND, Page(content));
    }

    /// <summary>ListView written the way a WPF developer would, checked through the generator.</summary>
    [TestFixture]
    public class ListViewGeneratorTests
    {
        [Test]
        public void GridViewWithColumnsTemplatesAndHeaders()
        {
            string source = ListsTestData.Compiles(@"
  <ListView ItemsSource=""{Binding People}"" SelectedItem=""{Binding Selected}"" SelectionMode=""Extended"" ColumnHeaderClick=""OnHeaderClick"">
    <ListView.View>
      <GridView>
        <GridViewColumn Header=""Name"" DisplayMemberBinding=""{Binding Name}"" Width=""160"" SortMemberPath=""Name"" />
        <GridViewColumn Header=""{Bind Title}"" Width=""*"" DisplayMemberBinding=""{Binding Age, StringFormat='{0} yrs'}"" SortMemberPath=""{Bind Age}"" />
        <GridViewColumn Width=""Auto"" MinWidth=""40"">
          <GridViewColumn.Header>
            <StackPanel Orientation=""Horizontal"" Spacing=""4"">
              <TextBlock Text=""Active"" FontWeight=""Bold"" />
            </StackPanel>
          </GridViewColumn.Header>
          <GridViewColumn.CellTemplate>
            <DataTemplate>
              <CheckBox IsChecked=""{Binding Active}"" />
            </DataTemplate>
          </GridViewColumn.CellTemplate>
        </GridViewColumn>
        <GridViewColumn Header=""Email"" DisplayMemberBinding=""Email"" Visibility=""Collapsed"" />
      </GridView>
    </ListView.View>
  </ListView>");

            Assert.That(source, Does.Contain("new global::OsuXaml.Controls.GridView()"));
            Assert.That(source, Does.Contain(".View = e"), "the GridView is assigned to View");
            Assert.That(source, Does.Contain("GridLength.Parse(\"160\")"));
            Assert.That(source, Does.Contain("GridLength.Parse(\"*\")"));
            Assert.That(source, Does.Contain(".DisplayMemberBinding = t"), "DisplayMemberBinding compiles to a text template");
            Assert.That(source, Does.Contain("new global::OsuXaml.DataTemplate<global::Demo.Person>("), "the cell template takes the ListView's item type");
            Assert.That(source, Does.Contain("new global::OsuXaml.ItemBinding<global::Demo.Person, int>(\"Age\""));
            Assert.That(source, Does.Contain("XamlBinding.TwoWayObject(this.xamlBindings, e0.SelectedItem, dataContext.Selected)"));
            Assert.That(source, Does.Contain(".ColumnHeaderClick += (a0) => this.OnHeaderClick(a0)"));
        }

        [Test]
        public void ColumnsPropertyElementAndPlainListView()
        {
            string source = ListsTestData.Compiles(@"
  <StackPanel>
    <ListView ItemsSource=""{Bind People}"" Height=""200"">
      <ListView.View>
        <GridView>
          <GridView.Columns>
            <GridViewColumn Header=""Name"" DisplayMemberBinding=""{Bind Name}"" SortDirection=""Ascending"" SortMemberPath=""Name"" />
          </GridView.Columns>
        </GridView>
      </ListView.View>
    </ListView>
    <ListView ItemsSource=""{Bind People}"" DisplayMemberPath=""Name"" />
    <ListView>
      <ListViewItem Content=""One"" />
      <x:String>Two</x:String>
    </ListView>
  </StackPanel>");

            Assert.That(source, Does.Contain(".Columns.Add(e"));
            Assert.That(source, Does.Contain("SortDirection = global::System.ComponentModel.ListSortDirection.Ascending"));
        }
    }

    /// <summary>ListView and GridView at runtime: shared column widths, header alignment, selection and sorting.</summary>
    [HeadlessTest]
    public partial class ListViewTests : TestScene
    {
        private GridViewColumn nameColumn, ageColumn, notesColumn;

        private ListView createGridList(BindableList<ListsTestData.Person> people, BindingScope scope)
        {
            nameColumn = new GridViewColumn
            {
                Header = "Name",
                Width = GridLength.Parse("120"),
                DisplayMemberBinding = ListsTestData.Text(p => p.Name),
                SortMemberPath = ListsTestData.NameBinding,
            };
            ageColumn = new GridViewColumn
            {
                Header = "Age",
                CellTemplate = new DataTemplate<ListsTestData.Person>((p, s) =>
                {
                    var text = new TextBlock();
                    XamlBinding.OneWay(s, p.Age, (v, _) => text.Text = v + " years");
                    return text;
                }),
                SortMemberPath = ListsTestData.AgeBinding,
            };
            notesColumn = new GridViewColumn { Header = "Notes", Width = GridLength.Parse("*") };

            var view = new GridView();
            view.Add(nameColumn);
            view.Add(ageColumn);
            view.Columns.Add(notesColumn);

            return new ListView
            {
                Size = new Vector2(400, 200),
                ItemsSource = XamlBinding.ListCopy(scope, people),
                View = view,
            };
        }

        [Test]
        public void RowsShareColumnWidthsWithTheHeader()
        {
            var people = ListsTestData.People();
            var scope = new BindingScope();
            ListView list = null;

            AddStep("create", () => Child = list = createGridList(people, scope));
            AddUntilStep("widths solved", () => notesColumn.ActualWidth > 0);

            AddAssert("pixel column", () => nameColumn.ActualWidth == 120);
            AddAssert("auto column fits its widest cell", () =>
            {
                float widest = people.Max(p => ListsTestData.TextOf(list.RowFromItem(p).CellAt(1)) == null ? 0 : list.RowFromItem(p).CellAt(1).DrawWidth);
                return ageColumn.ActualWidth >= widest + 16 && ageColumn.ActualWidth < widest + 17;
            });
            AddAssert("star column takes the rest", () => ListsTestData.Near(notesColumn.ActualWidth, 400 - 120 - ageColumn.ActualWidth));

            AddAssert("cells line up under their headers", () =>
            {
                var headers = list.ColumnHeaders.Headers.ToArray();

                return people.All(p =>
                {
                    var row = list.RowFromItem(p);
                    return Enumerable.Range(0, 2).All(i =>
                        ListsTestData.Near(row.CellAt(i).ScreenSpaceDrawQuad.TopLeft.X, headers[i].PresentedContent.ScreenSpaceDrawQuad.TopLeft.X));
                });
            });

            AddAssert("templates show the item", () => ListsTestData.TextOf(list.RowFromItem(people[1]).CellAt(0)) == "Ann"
                                                       && ListsTestData.TextOf(list.RowFromItem(people[1]).CellAt(1)) == "29 years");
            AddAssert("no template shows the item text", () => ListsTestData.TextOf(list.RowFromItem(people[2]).CellAt(2)) == "Bob");

            AddStep("rename", () => people[1].Name.Value = "Annabelle");
            AddAssert("cell follows", () => ListsTestData.TextOf(list.RowFromItem(people[1]).CellAt(0)) == "Annabelle");

            AddStep("resize name column", () => nameColumn.Width = GridLength.Parse("200"));
            AddUntilStep("star column shrinks", () => ListsTestData.Near(notesColumn.ActualWidth, 400 - 200 - ageColumn.ActualWidth));
            AddAssert("header follows", () => ListsTestData.Near(list.ColumnHeaders.Headers.ElementAt(1).ScreenSpaceDrawQuad.TopLeft.X - list.ScreenSpaceDrawQuad.TopLeft.X, 200));
        }

        [Test]
        public void RowsKeepListBoxSelection()
        {
            var people = ListsTestData.People();
            var selected = new Bindable<ListsTestData.Person>();
            var scope = new BindingScope();
            ListView list = null;

            AddStep("create", () =>
            {
                list = createGridList(people, scope);
                XamlBinding.TwoWayObject(scope, list.SelectedItem, selected);
                Child = list;
            });

            AddStep("click second row", () => list.ContainerFromIndex(1).TriggerClick());
            AddAssert("selected", () => selected.Value == people[1] && ((ListViewItem)list.ContainerFromIndex(1)).IsSelected);

            AddStep("view model selects Bob", () => selected.Value = people[2]);
            AddAssert("row follows", () => list.SelectedIndex.Value == 2 && ((ListViewItem)list.ContainerFromIndex(2)).IsSelected && !((ListViewItem)list.ContainerFromIndex(1)).IsSelected);
        }

        [Test]
        public void HeaderClickSortsWithoutTouchingTheSource()
        {
            var people = ListsTestData.People();
            var scope = new BindingScope();
            ListView list = null;
            GridViewColumn clicked = null;

            string order() => string.Join(",", list.Items.Cast<ListsTestData.Person>().Select(p => p.Name.Value));

            AddStep("create", () =>
            {
                Child = list = createGridList(people, scope);
                list.ColumnHeaderClick += c => clicked = c;
                list.SelectedItem.Value = people[0];
            });

            AddAssert("source order", () => order() == "Cid,Ann,Bob");

            AddStep("click Name header", () => list.ColumnHeaders.Headers.First().TriggerClick());
            AddAssert("event raised", () => clicked == nameColumn);
            AddAssert("sorted ascending", () => order() == "Ann,Bob,Cid" && nameColumn.SortDirection == ListSortDirection.Ascending);
            AddAssert("rows follow the order", () => ListsTestData.TextOf(((ListViewItem)list.ContainerFromIndex(0)).RowPresenter.CellAt(0)) == "Ann");
            AddAssert("view model list untouched", () => string.Join(",", people.Select(p => p.Name.Value)) == "Cid,Ann,Bob");
            AddAssert("selection kept", () => list.SelectedItem.Value == people[0] && list.SelectedIndex.Value == 2);

            AddStep("click again", () => list.ColumnHeaders.Headers.First().TriggerClick());
            AddAssert("descending", () => order() == "Cid,Bob,Ann" && nameColumn.SortDirection == ListSortDirection.Descending);

            AddStep("add Dee to the source", () => people.Add(new ListsTestData.Person("Dee", 20)));
            AddAssert("inserted in sort position", () => order() == "Dee,Cid,Bob,Ann");

            AddStep("sort by age", () => list.ColumnHeaders.Headers.ElementAt(1).TriggerClick());
            AddAssert("by age, name unsorted", () => order() == "Dee,Ann,Bob,Cid" && nameColumn.SortDirection == null);

            AddStep("clear sort", () => list.Sort(ageColumn, null));
            AddAssert("source order back", () => order() == "Cid,Ann,Bob,Dee");

            AddStep("notes header isn't sortable", () => list.ColumnHeaders.Headers.ElementAt(2).TriggerClick());
            AddAssert("still source order", () => order() == "Cid,Ann,Bob,Dee" && clicked == notesColumn);
        }

        [Test]
        public void ColumnDeclaredSortedStartsSorted()
        {
            var people = ListsTestData.People();
            var scope = new BindingScope();
            ListView list = null;

            AddStep("create", () =>
            {
                list = createGridList(people, scope);
                ageColumn.SortDirection = ListSortDirection.Descending;
                Child = list;
            });

            AddAssert("rows sorted at load", () => string.Join(",", list.Items.Cast<ListsTestData.Person>().Select(p => p.Age.Value)) == "41,35,29");
        }

        [Test]
        public void ColumnsCanChangeAfterLoad()
        {
            var people = ListsTestData.People();
            var scope = new BindingScope();
            ListView list = null;

            AddStep("create", () => Child = list = createGridList(people, scope));
            AddStep("add email column", () => ((GridView)list.View).Columns.Insert(0, new GridViewColumn
            {
                Header = "Email",
                DisplayMemberBinding = new DataTemplate<ListsTestData.Person>((p, _) => new TextBlock { Text = p.Email }),
            }));

            AddAssert("rows rebuilt", () => ListsTestData.TextOf(list.RowFromItem(people[0]).CellAt(0)) == "cid@example.com"
                                           && ListsTestData.TextOf(list.RowFromItem(people[0]).CellAt(1)) == "Cid");
            AddAssert("header rebuilt", () => ListsTestData.TextOf(list.ColumnHeaders.Headers.First()) == "Email" && list.ColumnHeaders.Headers.Count() == 4);

            AddStep("collapse it", () => ((GridView)list.View).Columns[0].Visibility = Visibility.Collapsed);
            AddUntilStep("takes no space", () => ((GridView)list.View).Columns[0].ActualWidth == 0
                                                 && ListsTestData.Near(list.RowFromItem(people[0]).CellAt(1).ScreenSpaceDrawQuad.TopLeft.X - list.ScreenSpaceDrawQuad.TopLeft.X, 8));
        }

        [Test]
        public void SizesToContentOutsideAPanel()
        {
            var people = ListsTestData.People();
            var scope = new BindingScope();
            ListView list = null;

            AddStep("create", () =>
            {
                list = createGridList(people, scope);
                list.Size = Vector2.Zero;
                Child = list;
            });

            AddUntilStep("sized to its columns", () => list.DrawWidth > 120 && ListsTestData.Near(list.DrawWidth, nameColumn.ActualWidth + ageColumn.ActualWidth + notesColumn.ActualWidth));
            AddAssert("star column sized to its content", () => notesColumn.ActualWidth > 20);
            AddAssert("tall enough for header and rows", () => list.DrawHeight > list.ColumnHeaders.DrawHeight + 3 * 20);
        }

        [Test]
        public void WithoutAViewItBehavesLikeAListBox()
        {
            ListView list = null;

            AddStep("create", () =>
            {
                list = new ListView { Size = new Vector2(200, 200), DisplayMemberPath = ListsTestData.Text(p => p.Name) };
                foreach (var p in ListsTestData.People())
                    list.Add(p);
                Child = list;
            });

            AddAssert("list items", () => list.ContainerFromIndex(0) is ListViewItem item && item.RowPresenter == null && ListsTestData.TextOf(item) == "Cid");
            AddAssert("no header", () => list.ColumnHeaders == null);
            AddStep("select", () => list.ContainerFromIndex(2).TriggerClick());
            AddAssert("selected", () => list.SelectedIndex.Value == 2);
        }

        [Test]
        public void LongListsScroll()
        {
            ListBox list = null;

            AddStep("create", () =>
            {
                list = new ListBox { Size = new Vector2(200, 100) };
                for (int i = 0; i < 30; i++)
                    list.Add("item " + i);
                Child = list;
            });

            AddUntilStep("content taller than the viewport", () => list.ChildrenOfType<ScrollViewer>().Single().ScrollableHeight > 500);
            AddStep("scroll to the last item", () => list.ScrollIntoView("item 29"));
            AddUntilStep("scrolled", () => list.ChildrenOfType<ScrollViewer>().Single().VerticalOffset > 500);
        }
    }
}

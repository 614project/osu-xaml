using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Testing;
using OsuXaml.Controls;
using osuTK;
using osuTK.Input;
using static OsuXaml.Tests.ListsTestData;

namespace OsuXaml.Tests
{
    /// <summary>TreeView written the way a WPF developer would, checked through the generator.</summary>
    [TestFixture]
    public class TreeViewGeneratorTests
    {
        [Test]
        public void BoundTreeWithHierarchicalTemplate()
        {
            string source = Compiles(@"
<TreeView ItemsSource=""{Bind Roots}"" SelectedItem=""{Bind SelectedNode}"" IsExpandedBinding=""{Bind IsExpanded}""
          SelectedItemChanged=""OnSelected"" Width=""240"" Height=""300"">
    <TreeView.ItemTemplate>
        <HierarchicalDataTemplate ItemsSource=""{Bind Children}"">
            <TextBlock Text=""{Bind Label}"" />
        </HierarchicalDataTemplate>
    </TreeView.ItemTemplate>
</TreeView>");

            Assert.That(source, Does.Contain("new global::OsuXaml.HierarchicalDataTemplate<global::Demo.Node>("));
            Assert.That(source, Does.Contain("TwoWayObject"));
        }

        [Test]
        public void DeclaredItemsNest()
        {
            string source = Compiles(@"
<TreeView>
    <TreeViewItem Header=""Documents"" IsExpanded=""True"">
        <TreeViewItem Header=""Report.docx"" />
        <TreeViewItem>
            <TreeViewItem.Header>
                <StackPanel Orientation=""Horizontal"" Spacing=""4"">
                    <TextBlock Text=""Notes"" />
                </StackPanel>
            </TreeViewItem.Header>
        </TreeViewItem>
    </TreeViewItem>
    <TreeViewItem Header=""Pictures"" />
</TreeView>");

            Assert.That(source, Does.Contain("global::OsuXaml.Controls.TreeViewItem"));
        }
    }

    /// <summary>The TreeView at runtime: expansion, selection, keyboard and live children.</summary>
    [HeadlessTest]
    public partial class TreeViewTests : ManualInputManagerTestScene
    {
        private static readonly HierarchicalDataTemplate<Node> node_template = new HierarchicalDataTemplate<Node>(
            (n, _) => new TextBlock { Text = n.Label },
            (n, scope) => XamlBinding.ListCopy(scope, n.Children));

        private static readonly ItemBinding<Node, bool> expanded_binding = new ItemBinding<Node, bool>("IsExpanded", n => n.IsExpanded, n => n.IsExpanded.Value);

        private BindableList<Node> roots;
        private Node docs, report, notes, pics;
        private Bindable<Node> selected;
        private TreeView tree;
        private List<object> changes;

        [SetUpSteps]
        public void SetUpSteps()
        {
            // Built while the steps are being added, so the step helpers capture these instances.
            notes = new Node("notes.txt");
            report = new Node("report.docx");
            docs = new Node("Documents", report, new Node("Drafts", notes));
            pics = new Node("Pictures");
            roots = new BindableList<Node>(new[] { docs, pics });
            selected = new Bindable<Node>();
            changes = new List<object>();

            var scope = new BindingScope();
            tree = new TreeView
            {
                Size = new Vector2(260, 300),
                ItemTemplate = node_template,
                IsExpandedBinding = expanded_binding,
                ItemsSource = XamlBinding.ListCopy(scope, roots),
            };
            tree.SelectedItemChanged += item => changes.Add(item);
            XamlBinding.TwoWayObject(scope, tree.SelectedItem, selected);

            AddStep("create", () => Child = tree);
            AddUntilStep("loaded", () => tree.IsLoaded && tree.VisibleItems.Count() == 2);
        }

        private TreeViewItem node(Node data) => tree.ContainerFromDataItem(data);

        private string visibleLabels => string.Join(",", tree.VisibleItems.Select(n => ((Node)n.DataItem).Label));

        [Test]
        public void ExpandingShowsChildrenIndentedAndAnimated()
        {
            AddAssert("collapsed nodes show only roots", () => visibleLabels == "Documents,Pictures");
            AddAssert("chevron only where there are children", () => node(docs).HasItems && !node(pics).HasItems);

            float collapsedHeight = 0;
            AddStep("expand Documents", () =>
            {
                collapsedHeight = node(docs).DrawHeight;
                docs.IsExpanded.Value = true;
            });
            AddAssert("view model drives IsExpanded", () => node(docs).IsExpanded.Value);
            AddAssert("children created on first expand", () => visibleLabels == "Documents,report.docx,Drafts,Pictures");
            AddUntilStep("height animates open", () => node(docs).DrawHeight > collapsedHeight * 2.5f);
            AddAssert("children indented one level", () => node(report).Level == 1
                                                         && node(report).HeaderRow.ChildrenOfType<ContentPresenter>().First().ScreenSpaceDrawQuad.TopLeft.X
                                                         > node(docs).HeaderRow.ChildrenOfType<ContentPresenter>().First().ScreenSpaceDrawQuad.TopLeft.X + 10);
            AddAssert("Pictures moved below the children", () => node(pics).ScreenSpaceDrawQuad.TopLeft.Y >= node(report).ScreenSpaceDrawQuad.BottomLeft.Y - 1);

            AddStep("collapse from the node", () => node(docs).IsExpanded.Value = false);
            AddAssert("view model follows", () => !docs.IsExpanded.Value && visibleLabels == "Documents,Pictures");
            AddUntilStep("height animates closed", () => Near(node(docs).DrawHeight, collapsedHeight));
        }

        [Test]
        public void ChildrenListsStayInSync()
        {
            AddStep("expand", () => docs.IsExpanded.Value = true);
            AddStep("add a child", () => docs.Children.Insert(0, new Node("archive.zip")));
            AddAssert("new child shows first", () => visibleLabels == "Documents,archive.zip,report.docx,Drafts,Pictures");
            AddStep("give Pictures a child", () => pics.Children.Add(new Node("cat.png")));
            AddUntilStep("Pictures gets a chevron", () => node(pics).HasItems);
            AddStep("remove a root", () => roots.Remove(pics));
            AddAssert("root gone", () => visibleLabels == "Documents,archive.zip,report.docx,Drafts");
        }

        [Test]
        public void SelectionBindsTwoWayAndFollowsCollapseAndRemoval()
        {
            AddStep("select report from the view model", () =>
            {
                docs.IsExpanded.Value = true;
                selected.Value = report;
            });
            AddAssert("node selected", () => node(report).IsSelected.Value && tree.SelectedContainer == node(report) && changes.Last() == report);

            AddStep("click Pictures", () =>
            {
                InputManager.MoveMouseTo(node(pics).HeaderRow);
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("view model follows the click", () => selected.Value == pics && !node(report).IsSelected.Value);

            AddStep("select notes before it exists", () => selected.Value = notes);
            AddAssert("nothing to select yet", () => tree.SelectedContainer == null);
            AddStep("expand Drafts", () => docs.Children[1].IsExpanded.Value = true);
            AddAssert("picked up when created", () => tree.SelectedContainer == node(notes));

            AddStep("collapse Documents", () => docs.IsExpanded.Value = false);
            AddAssert("selection moves to the collapsed ancestor", () => selected.Value == docs);

            AddStep("remove Documents", () => roots.Remove(docs));
            AddAssert("selection cleared", () => selected.Value == null && tree.SelectedContainer == null);
        }

        [Test]
        public void KeyboardNavigates()
        {
            AddStep("click Documents", () =>
            {
                InputManager.MoveMouseTo(node(docs).HeaderRow);
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("focused and selected", () => tree.HasFocus && selected.Value == docs);

            AddStep("Right expands", () => InputManager.Key(Key.Right));
            AddAssert("expanded, still on Documents", () => docs.IsExpanded.Value && selected.Value == docs);
            AddStep("Right enters", () => InputManager.Key(Key.Right));
            AddAssert("first child", () => selected.Value == report);
            AddStep("Down", () => InputManager.Key(Key.Down));
            AddAssert("Drafts", () => selected.Value == docs.Children[1]);
            AddStep("Left goes to the parent", () => InputManager.Key(Key.Left));
            AddAssert("Documents", () => selected.Value == docs);
            AddStep("Left collapses", () => InputManager.Key(Key.Left));
            AddAssert("collapsed", () => !docs.IsExpanded.Value);
            AddStep("End", () => InputManager.Key(Key.End));
            AddAssert("last visible", () => selected.Value == pics);
        }

        [Test]
        public void DoubleClickTogglesAndDeclaredItemsWork()
        {
            AddStep("double-click Documents", () =>
            {
                InputManager.MoveMouseTo(node(docs).HeaderRow);
                InputManager.Click(MouseButton.Left);
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("expanded", () => docs.IsExpanded.Value);

            TreeView declared = null;
            TreeViewItem music = null, song = null;
            AddStep("declared items", () =>
            {
                declared = new TreeView { Size = new Vector2(200, 200) };
                music = new TreeViewItem { Header = "Music" };
                music.Add(song = new TreeViewItem { Header = "song.mp3" });
                declared.Add(music);
                declared.Add(new TreeViewItem { Header = "Videos" });
                Child = declared;
            });
            AddUntilStep("loaded", () => declared.IsLoaded && declared.VisibleItems.Count() == 2);
            AddStep("expand and select the song", () =>
            {
                music.IsExpanded.Value = true;
                song.IsSelected.Value = true;
            });
            AddAssert("selected item is the TreeViewItem", () => declared.SelectedItem.Value == song && song.Level == 1);
        }
    }
}

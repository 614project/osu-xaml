using System.Linq;
using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Testing;
using TextBox = OsuXaml.Controls.TextBox;
using PasswordBox = OsuXaml.Controls.PasswordBox;

namespace OsuXaml.Tests
{
    /// <summary>PasswordBox masking and binding, and the WPF-style TextBox additions.</summary>
    [HeadlessTest]
    public partial class PasswordBoxTests : TestScene
    {
        /// <summary>Types the way text input does, and shows what the box draws.</summary>
        private partial class TypingPasswordBox : PasswordBox
        {
            public void Type(string text) => InsertString(text);

            public string Drawn => string.Concat(TextFlow.ChildrenOfType<SpriteText>().Select(t => t.Text.ToString()));
        }

        private partial class TypingTextBox : TextBox
        {
            public void Type(string text) => InsertString(text);

            public SpriteText PlaceholderSprite => Placeholder;
        }

        [Test]
        public void PasswordBindsTwoWayAndOnlyMaskCharactersAreDrawn()
        {
            var password = new Bindable<string>(string.Empty);
            TypingPasswordBox box = null;
            int changes = 0;

            AddStep("create", () =>
            {
                box = new TypingPasswordBox { Width = 200 };
                XamlBinding.TwoWay(box.Password, password);
                box.PasswordChanged += () => changes++;
                Child = box;
            });

            AddStep("type", () => box.Type("hunter2"));
            AddAssert("view model has the password", () => password.Value == "hunter2" && changes > 0);
            AddUntilStep("bullets only", () => box.Drawn == new string('•', 7));

            AddStep("view model sets the password", () => password.Value = "abc");
            AddAssert("box follows", () => box.Password.Value == "abc");
            AddUntilStep("three bullets", () => box.Drawn == "•••");

            AddStep("star mask", () => box.PasswordChar = '*');
            AddUntilStep("redrawn with stars", () => box.Drawn == "***");
            AddAssert("password unchanged by the redraw", () => password.Value == "abc");
        }

        [Test]
        public void MaxLengthReadOnlyAndPlaceholder()
        {
            TypingTextBox box = null;
            string lastText = null;
            int changes = 0;

            AddStep("create", () =>
            {
                box = new TypingTextBox { Width = 200, MaxLength = 3, PlaceholderText = "Name" };
                box.TextChanged += t =>
                {
                    lastText = t;
                    changes++;
                };
                Child = box;
            });

            AddAssert("placeholder shown while empty", () => box.PlaceholderSprite.Text.ToString() == "Name" && box.PlaceholderSprite.Alpha > 0);
            AddStep("type past the limit", () => box.Type("abcdef"));
            AddAssert("cut at MaxLength", () => box.Text.Value == "abc" && lastText == "abc" && changes == 1);

            AddStep("resize", () => box.Width = 300);
            AddWaitStep("relayout", 2);
            AddAssert("re-laying out the text raises no TextChanged", () => changes == 1 && box.Text.Value == "abc");

            AddStep("read only", () => box.IsReadOnly = true);
            AddAssert("maps to ReadOnly", () => ((BasicTextBox)box).ReadOnly);
            AddStep("no limit", () => box.MaxLength = 0);
            AddAssert("limit cleared", () => box.LengthLimit == null);
        }
    }
}

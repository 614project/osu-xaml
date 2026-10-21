using NUnit.Framework;

namespace OsuXaml.Tests
{
    /// <summary>Generic elements (<c>x:TypeArguments</c>) and generic type names (<c>local:Pair(x:String, x:Int32)</c>).</summary>
    [TestFixture]
    public class GenericGeneratorTests
    {
        private const string code_behind = @"
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;

namespace Demo
{
    public class Vm
    {
        public Bindable<string> Choice { get; } = new Bindable<string>(""a"");
        public Bindable<string> Title { get; } = new Bindable<string>(""hi"");
    }

    public class Pair<TFirst, TSecond>
    {
        public TFirst First { get; set; }
        public Bindable<TSecond> Second { get; } = new Bindable<TSecond>();
    }

    public partial class Holder<T> : CompositeDrawable
    {
        public T Value { get; set; }
    }

    public partial class DrawableHolder<T> : CompositeDrawable where T : Drawable, new()
    {
        public T Inner { get; } = new T();
    }

    public partial class View : CompositeDrawable
    {
    }

    public partial class Flow : FillFlowContainer<osu.Framework.Graphics.Sprites.SpriteText>
    {
    }

    public partial class PairView : CompositeDrawable
    {
    }
}";

        private const string header = @"<CompositeDrawable xmlns=""osu-xaml"" xmlns:x=""osu-xaml:x"" xmlns:local=""clr-namespace:Demo""
    x:Class=""Demo.View"" x:DataType=""local:Vm"">";

        [Test]
        public void GenericElementsTakeTypeArguments()
        {
            string source = GeneratorRunner.Compiles(code_behind, header + @"
  <FillFlowContainer x:TypeArguments=""SpriteText"" x:Name=""texts"" Direction=""Vertical"">
    <SpriteText Text=""{Bind Title}"" />
    <SpriteText Text=""second"" />
  </FillFlowContainer>
  <BasicDropdown x:TypeArguments=""x:String"" x:Name=""drop"" Width=""200"" Current=""{Bind Choice}"" />
  <SpriteText Text=""{Bind Current, ElementName=drop}"" />
  <local:Holder x:TypeArguments=""x:Int32"" Value=""5"" />
  <local:Holder x:TypeArguments=""local:Pair(x:String, x:Int32)"" />
</CompositeDrawable>");

            Assert.That(source, Does.Contain("new global::osu.Framework.Graphics.Containers.FillFlowContainer<global::osu.Framework.Graphics.Sprites.SpriteText>()"));
            Assert.That(source, Does.Contain("private global::osu.Framework.Graphics.Containers.FillFlowContainer<global::osu.Framework.Graphics.Sprites.SpriteText> texts;"));
            Assert.That(source, Does.Contain("new global::osu.Framework.Graphics.UserInterface.BasicDropdown<string>()"));
            Assert.That(source, Does.Contain("new global::Demo.Holder<int>()"));
            Assert.That(source, Does.Contain(".Value = 5;"));
            Assert.That(source, Does.Contain("new global::Demo.Holder<global::Demo.Pair<string, int>>()"));
        }

        [Test]
        public void ChildrenAreCheckedAgainstTheTypeArgument()
        {
            var diagnostic = GeneratorRunner.SingleError(code_behind, header + @"
  <FillFlowContainer x:TypeArguments=""SpriteText"">
    <Box />
  </FillFlowContainer>
</CompositeDrawable>");

            Assert.That(diagnostic.Id, Is.EqualTo("OX0007"));
            Assert.That(diagnostic.GetMessage(), Does.Contain("SpriteText"));
        }

        [Test]
        public void PropertyElementsAndStylesNameTheGenericType()
        {
            GeneratorRunner.Compiles(code_behind, header + @"
  <CompositeDrawable.Resources>
    <Style TargetType=""local:Holder(x:Int32)"">
      <Setter Property=""Value"" Value=""3"" />
    </Style>
  </CompositeDrawable.Resources>
  <local:Holder x:TypeArguments=""x:Int32"" />
  <local:Holder x:TypeArguments=""x:String"">
    <local:Holder.Value>text</local:Holder.Value>
  </local:Holder>
  <local:DrawableHolder x:TypeArguments=""Box"" local:DrawableHolder.Alpha=""0.5"" />
</CompositeDrawable>");
        }

        [Test]
        public void ImplicitStyleForAGenericTypeAppliesOnlyToThoseArguments()
        {
            string source = GeneratorRunner.Compiles(code_behind, header + @"
  <CompositeDrawable.Resources>
    <Style TargetType=""local:Holder(x:Int32)"">
      <Setter Property=""Value"" Value=""3"" />
    </Style>
  </CompositeDrawable.Resources>
  <local:Holder x:TypeArguments=""x:Int32"" />
  <local:Holder x:TypeArguments=""x:Single"" />
</CompositeDrawable>");

            Assert.That(source, Does.Contain(".Value = 3;"));
            Assert.That(source, Does.Not.Contain(".Value = 3f;"));
        }

        [Test]
        public void GenericDataTypes()
        {
            GeneratorRunner.Compiles(code_behind, @"<CompositeDrawable xmlns=""osu-xaml"" xmlns:x=""osu-xaml:x"" xmlns:local=""clr-namespace:Demo""
    x:Class=""Demo.PairView"" x:DataType=""local:Pair(x:String, x:Int32)"">
  <SpriteText Text=""{Bind First}"" />
  <SpriteText Text=""{Bind Second, StringFormat='{0} left'}"" />
</CompositeDrawable>");
        }

        [Test]
        public void RootElementCanBeGeneric()
        {
            GeneratorRunner.Compiles(code_behind, @"<FillFlowContainer xmlns=""osu-xaml"" xmlns:x=""osu-xaml:x"" x:Class=""Demo.Flow"" x:TypeArguments=""SpriteText"">
  <SpriteText Text=""one"" />
</FillFlowContainer>");
        }

        [Test]
        public void MissingTypeArgumentsSayHowToAddThem()
        {
            var diagnostic = GeneratorRunner.SingleError(code_behind, header + @"
  <BasicDropdown />
</CompositeDrawable>");

            Assert.That(diagnostic.Id, Is.EqualTo("OX0003"));
            Assert.That(diagnostic.GetMessage(), Does.Contain("x:TypeArguments"));
            Assert.That(diagnostic.GetMessage(), Does.Contain("1 type argument"));
        }

        [Test]
        public void WrongNumberOfTypeArgumentsIsReported()
        {
            var diagnostic = GeneratorRunner.SingleError(code_behind, header + @"
  <BasicDropdown x:TypeArguments=""x:String, x:Int32"" />
</CompositeDrawable>");

            Assert.That(diagnostic.Id, Is.EqualTo("OX0003"));
            Assert.That(diagnostic.GetMessage(), Does.Contain("takes 1"));
        }

        [Test]
        public void UnknownTypeArgumentIsReported()
        {
            var diagnostic = GeneratorRunner.SingleError(code_behind, header + @"
  <local:Holder x:TypeArguments=""local:Nope"" />
</CompositeDrawable>");

            Assert.That(diagnostic.Id, Is.EqualTo("OX0003"));
            Assert.That(diagnostic.GetMessage(), Does.Contain("local:Nope"));
        }

        [Test]
        public void ConstraintsAreChecked()
        {
            var notDrawable = GeneratorRunner.SingleError(code_behind, header + @"
  <FillFlowContainer x:TypeArguments=""x:String"" />
</CompositeDrawable>");

            Assert.That(notDrawable.Id, Is.EqualTo("OX0013"));
            Assert.That(notDrawable.GetMessage(), Does.Contain("Drawable"));

            var abstractType = GeneratorRunner.SingleError(code_behind, header + @"
  <local:DrawableHolder x:TypeArguments=""Drawable"" />
</CompositeDrawable>");

            Assert.That(abstractType.Id, Is.EqualTo("OX0013"));
            Assert.That(abstractType.GetMessage(), Does.Contain("parameterless constructor"));
        }

        [Test]
        public void NonGenericNameStillMeansTheNonGenericType()
        {
            // FillFlowContainer is also a plain class; without x:TypeArguments it takes any drawable.
            string source = GeneratorRunner.Compiles(code_behind, header + @"
  <FillFlowContainer><Box /></FillFlowContainer>
</CompositeDrawable>");

            Assert.That(source, Does.Contain("new global::osu.Framework.Graphics.Containers.FillFlowContainer()"));
        }
    }
}

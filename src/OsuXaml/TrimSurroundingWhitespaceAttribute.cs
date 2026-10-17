using System;

namespace OsuXaml
{
    /// <summary>
    /// Drops the whitespace written next to this element in XAML text content, like WPF's attribute of the same name:
    /// <c>one&lt;LineBreak /&gt; two</c> reads "one", a line break, then "two", without a space starting the second line.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = true)]
    public sealed class TrimSurroundingWhitespaceAttribute : Attribute
    {
    }
}

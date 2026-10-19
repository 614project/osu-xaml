using System;

namespace OsuXaml
{
    /// <summary>
    /// Marks a string property that names a FontAwesome icon the way <see cref="Controls.Icon.Kind"/> does
    /// (<c>Solid.Check</c>, <c>check-circle</c>). A name written in XAML that osu!framework's <c>FontAwesome</c> class
    /// doesn't have is then a build error instead of a question mark at runtime. Bound values are still looked up at runtime.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class IconNameAttribute : Attribute
    {
    }
}

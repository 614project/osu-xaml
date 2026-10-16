using System;

namespace OsuXaml
{
    /// <summary>
    /// Registers a static class whose <c>SetName(Drawable, value)</c> methods every XAML element accepts without a prefix,
    /// like <c>HorizontalAlignment="Center"</c> or <c>ToolTip="Save"</c>. Apply it to the assembly:
    /// <c>[assembly: XamlGlobalProperties(typeof(MyProperties))]</c>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class XamlGlobalPropertiesAttribute : Attribute
    {
        public Type Type { get; }

        public XamlGlobalPropertiesAttribute(Type type)
        {
            Type = type;
        }
    }
}

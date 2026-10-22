using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using osu.Framework.Graphics;
using osu.Framework.Testing;

namespace OsuXaml.Controls
{
    /// <summary>
    /// Text properties that flow down the tree, like WPF's inherited <c>TextElement</c> properties: set on any element, they reach
    /// every <see cref="TextBlock"/> (and the colour every <see cref="Icon"/>) inside it that doesn't set its own.
    /// <code>
    /// &lt;StackPanel TextElement.Foreground="#a6adc8" TextElement.FontSize="13"&gt;
    ///   &lt;TextBlock Text="Takes both" /&gt;
    ///   &lt;TextBlock Text="Keeps its own size" FontSize="20" /&gt;
    /// &lt;/StackPanel&gt;
    /// </code>
    /// The nearest element that sets a property wins, and a TextBlock or Icon that sets one on itself counts as the nearest. Controls
    /// set them on themselves to give their content a colour or font (a button's text colour that changes on hover, say); the text
    /// follows every change. The text a control makes for its content (a Button's caption) takes the control's own
    /// <see cref="ContentControl.Foreground"/> when the control sets one, rather than a Foreground from further out.
    /// </summary>
    public static class TextElement
    {
        private static readonly ConditionalWeakTable<Drawable, InheritedTextProperties> entries = new ConditionalWeakTable<Drawable, InheritedTextProperties>();

        public static void SetForeground(Drawable element, Colour4 value) => set(element, p => p.Foreground = value);

        public static void SetFontFamily(Drawable element, string value) => set(element, p => p.FontFamily = TextBlock.ParseFontFamily(value));

        /// <summary>The em size in pixels, as <see cref="TextBlock.FontSize"/> takes it.</summary>
        public static void SetFontSize(Drawable element, float value) => set(element, p => p.FontSize = value);

        public static void SetFontWeight(Drawable element, FontWeight value) => set(element, p => p.FontWeight = value);

        public static Colour4? GetForeground(Drawable element) => entries.TryGetValue(element, out var p) ? p.Foreground : null;

        public static string GetFontFamily(Drawable element) => entries.TryGetValue(element, out var p) ? p.FontFamily : null;

        public static float? GetFontSize(Drawable element) => entries.TryGetValue(element, out var p) ? p.FontSize : null;

        public static FontWeight? GetFontWeight(Drawable element) => entries.TryGetValue(element, out var p) ? p.FontWeight : null;

        /// <summary>Stops <paramref name="element"/> giving its content a foreground, so the content takes the one from further out.</summary>
        public static void ClearForeground(Drawable element) => clear(element, p => p.Foreground = null);

        /// <summary>Stops <paramref name="element"/> giving its content a font family.</summary>
        public static void ClearFontFamily(Drawable element) => clear(element, p => p.FontFamily = null);

        /// <summary>Stops <paramref name="element"/> giving its content a font size.</summary>
        public static void ClearFontSize(Drawable element) => clear(element, p => p.FontSize = null);

        /// <summary>Stops <paramref name="element"/> giving its content a font weight.</summary>
        public static void ClearFontWeight(Drawable element) => clear(element, p => p.FontWeight = null);

        /// <summary>
        /// The properties <paramref name="element"/> sets for its content, or null when it sets none. Changing them is the same as
        /// calling the setters here, except that an element without any needs one of those first.
        /// </summary>
        public static InheritedTextProperties Get(Drawable element) => entries.TryGetValue(element, out var p) ? p : null;

        private static void set(Drawable element, Action<InheritedTextProperties> change)
        {
            bool created = !entries.TryGetValue(element, out var properties);

            if (created)
            {
                properties = new InheritedTextProperties();
                entries.Add(element, properties);
            }

            // Text already following the element hears of the change from the properties themselves.
            change(properties);

            if (created && element.IsLoaded)
            {
                // Text that loaded before this element set anything (the element itself among it) isn't listening yet.
                foreach (var listener in element.ChildrenOfType<IInheritsTextProperties>())
                    listener.InheritedTextChanged();
            }
        }

        private static void clear(Drawable element, Action<InheritedTextProperties> change)
        {
            if (entries.TryGetValue(element, out var properties))
                change(properties);
        }

        /// <summary>
        /// The properties the elements around <paramref name="element"/> give it (not counting its own), nearest first. A control that
        /// draws text of its own reads them through an <see cref="InheritedTextWatcher"/>.
        /// </summary>
        public static InheritedText Resolve(Drawable element)
        {
            var result = new InheritedText();

            for (var d = element.Parent; d != null; d = d.Parent)
            {
                if (entries.TryGetValue(d, out var p))
                    result = result.Fill(p);
            }

            return result;
        }
    }

    /// <summary>
    /// The text properties one element gives its content; null ones it leaves to the elements further out. Text following the
    /// element hears of every change.
    /// </summary>
    public sealed class InheritedTextProperties
    {
        private Colour4? foreground;
        private string fontFamily;
        private float? fontSize;
        private FontWeight? fontWeight;

        public Colour4? Foreground
        {
            get => foreground;
            set => setValue(ref foreground, value);
        }

        public string FontFamily
        {
            get => fontFamily;
            set => setValue(ref fontFamily, value);
        }

        public float? FontSize
        {
            get => fontSize;
            set => setValue(ref fontSize, value);
        }

        public FontWeight? FontWeight
        {
            get => fontWeight;
            set => setValue(ref fontWeight, value);
        }

        /// <summary>Raised after a property changes.</summary>
        public event Action Changed;

        private void setValue<T>(ref T field, T value)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return;

            field = value;
            Changed?.Invoke();
        }
    }

    /// <summary>The text properties that reach an element: for each, the value of the nearest element around it that sets one.</summary>
    public readonly struct InheritedText
    {
        public readonly Colour4? Foreground;
        public readonly string FontFamily;
        public readonly float? FontSize;
        public readonly FontWeight? FontWeight;

        public InheritedText(Colour4? foreground, string fontFamily, float? fontSize, FontWeight? fontWeight)
        {
            Foreground = foreground;
            FontFamily = fontFamily;
            FontSize = fontSize;
            FontWeight = fontWeight;
        }

        /// <summary>This, with what it doesn't have yet taken from <paramref name="outer"/>.</summary>
        internal InheritedText Fill(InheritedTextProperties outer)
            => new InheritedText(Foreground ?? outer.Foreground, FontFamily ?? outer.FontFamily, FontSize ?? outer.FontSize, FontWeight ?? outer.FontWeight);

        /// <summary>The same as <see cref="Fill"/>, leaving out the foreground.</summary>
        internal InheritedText FillFont(InheritedTextProperties outer)
            => new InheritedText(Foreground, FontFamily ?? outer.FontFamily, FontSize ?? outer.FontSize, FontWeight ?? outer.FontWeight);
    }

    /// <summary>Something inside the tree that takes inherited text properties and wants to hear when an element around it starts setting some.</summary>
    public interface IInheritsTextProperties
    {
        void InheritedTextChanged();
    }

    /// <summary>
    /// Follows the inherited text properties around a drawable: <see cref="Attach"/> once it is loaded (its parents are known then),
    /// and <paramref name="apply"/> runs with the current values then and after every change. <see cref="Detach"/> stops it.
    /// </summary>
    public sealed class InheritedTextWatcher
    {
        private readonly Drawable owner;
        private readonly Action<InheritedText> apply;

        // Nearest first, with whether the foreground they set reaches the drawable.
        private readonly List<(InheritedTextProperties Properties, bool Foreground)> watched = new List<(InheritedTextProperties, bool)>();

        public InheritedTextWatcher(Drawable owner, Action<InheritedText> apply)
        {
            this.owner = owner;
            this.apply = apply;
        }

        /// <summary>The values that reach the drawable now.</summary>
        public InheritedText Current { get; private set; }

        /// <summary>
        /// Whether the properties the drawable sets on itself count, as the nearest, as they do for text (a TextBlock with
        /// <c>TextElement.Foreground</c> takes it). False by default: a control's own properties are for its content.
        /// </summary>
        public bool IncludesOwner { get; set; }

        /// <summary>
        /// When set, the foreground comes only from this element and those between it and the drawable: what the elements further out
        /// set doesn't reach. Fonts still do. For text a control makes, which takes the control's own colour. Read on <see cref="Attach"/>.
        /// </summary>
        public Drawable ForegroundBoundary { get; set; }

        /// <summary>Starts (or restarts, after the drawable moved) following the elements around the drawable.</summary>
        public void Attach()
        {
            Detach();

            bool foreground = true;

            for (var d = IncludesOwner ? owner : owner.Parent; d != null; d = d.Parent)
            {
                if (TextElement.Get(d) is InheritedTextProperties properties)
                {
                    watched.Add((properties, foreground));
                    properties.Changed += onChanged;
                }

                if (d == ForegroundBoundary)
                    foreground = false;
            }

            onChanged();
        }

        public void Detach()
        {
            foreach (var (properties, _) in watched)
                properties.Changed -= onChanged;

            watched.Clear();
        }

        private void onChanged()
        {
            var result = new InheritedText();

            foreach (var (properties, foreground) in watched)
                result = foreground ? result.Fill(properties) : result.FillFont(properties);

            Current = result;
            apply(result);
        }
    }
}

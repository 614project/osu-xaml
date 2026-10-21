using System.Collections.Generic;
using System.Runtime.CompilerServices;
using osu.Framework.Graphics;

[assembly: OsuXaml.XamlGlobalProperties(typeof(OsuXaml.Controls.CursorService))]

namespace OsuXaml.Controls
{
    /// <summary>The mouse cursor shapes a <see cref="Cursor"/> can have, named as in WPF.</summary>
    public enum CursorType
    {
        /// <summary>No cursor: the pointer is hidden.</summary>
        None,

        /// <summary>A circle with a slash, for actions that aren't allowed.</summary>
        No,

        /// <summary>The usual arrow.</summary>
        Arrow,

        /// <summary>An arrow with a busy indicator: the app is working, but still takes input.</summary>
        AppStarting,

        /// <summary>A crosshair, for picking a precise point.</summary>
        Cross,

        /// <summary>An arrow with a question mark.</summary>
        Help,

        /// <summary>The text cursor.</summary>
        IBeam,

        /// <summary>Arrows in four directions, for moving something.</summary>
        SizeAll,

        /// <summary>A diagonal double arrow from top right to bottom left.</summary>
        SizeNESW,

        /// <summary>A vertical double arrow, for resizing heights (horizontal <see cref="GridSplitter"/>).</summary>
        SizeNS,

        /// <summary>A diagonal double arrow from top left to bottom right.</summary>
        SizeNWSE,

        /// <summary>A horizontal double arrow, for resizing widths (vertical <see cref="GridSplitter"/>, column edges).</summary>
        SizeWE,

        /// <summary>The busy cursor.</summary>
        Wait,

        /// <summary>A pointing hand, for links (<see cref="Hyperlink"/>).</summary>
        Hand,

        /// <summary>Arrows in four directions around a dot, for panning or scrolling in any direction.</summary>
        ScrollAll,
    }

    /// <summary>
    /// A mouse cursor shape, like WPF's Cursor. Use the ready-made ones in <see cref="Cursors"/>;
    /// in XAML, <c>Cursor="Hand"</c> names one of them and a misspelt name is a build error.
    /// </summary>
    public sealed class Cursor
    {
        internal Cursor(CursorType type)
        {
            CursorType = type;
        }

        public CursorType CursorType { get; }

        public override string ToString() => CursorType.ToString();
    }

    /// <summary>The system cursor shapes, like WPF's Cursors: <c>CursorService.SetCursor(element, Cursors.Hand)</c>.</summary>
    public static class Cursors
    {
        public static Cursor None { get; } = new Cursor(CursorType.None);
        public static Cursor No { get; } = new Cursor(CursorType.No);
        public static Cursor Arrow { get; } = new Cursor(CursorType.Arrow);
        public static Cursor AppStarting { get; } = new Cursor(CursorType.AppStarting);
        public static Cursor Cross { get; } = new Cursor(CursorType.Cross);
        public static Cursor Help { get; } = new Cursor(CursorType.Help);
        public static Cursor IBeam { get; } = new Cursor(CursorType.IBeam);
        public static Cursor SizeAll { get; } = new Cursor(CursorType.SizeAll);
        public static Cursor SizeNESW { get; } = new Cursor(CursorType.SizeNESW);
        public static Cursor SizeNS { get; } = new Cursor(CursorType.SizeNS);
        public static Cursor SizeNWSE { get; } = new Cursor(CursorType.SizeNWSE);
        public static Cursor SizeWE { get; } = new Cursor(CursorType.SizeWE);
        public static Cursor Wait { get; } = new Cursor(CursorType.Wait);
        public static Cursor Hand { get; } = new Cursor(CursorType.Hand);
        public static Cursor ScrollAll { get; } = new Cursor(CursorType.ScrollAll);
    }

    /// <summary>
    /// A control with a cursor of its own, as WPF controls set one in their style: <see cref="Hyperlink"/> shows a hand,
    /// <see cref="TextBox"/> a text cursor. A cursor set on the element with <see cref="CursorService"/> wins over it.
    /// It has to take mouse input, because the cursor is asked for while the control (or something inside it) is hovered or dragged.
    /// </summary>
    internal interface IHasDefaultCursor
    {
        /// <summary>The cursor over this control in its current state, or null for whatever its parents show.</summary>
        Cursor DefaultCursor { get; }
    }

    /// <summary>
    /// The <c>Cursor</c> property every XAML element accepts, like WPF's FrameworkElement.Cursor: <c>Cursor="Hand"</c>
    /// or <c>Cursor="{Bind BusyCursor}"</c>. The pointer takes the shape of the deepest element under it that has a cursor
    /// (its own, or a control's built-in one such as a <see cref="Hyperlink"/>'s hand), and keeps the shape of the element
    /// being dragged until the drag ends. Elsewhere it is the usual arrow.
    /// </summary>
    /// <remarks>
    /// osu!framework has no cursor API, so the shape is set through SDL3 on the window's thread. With the SDL2 backend,
    /// or a game that hides the system cursor to draw its own, nothing changes on screen.
    /// </remarks>
    public static class CursorService
    {
        private static readonly ConditionalWeakTable<Drawable, Cursor> cursors = new ConditionalWeakTable<Drawable, Cursor>();

        /// <summary>
        /// A cursor shown over the whole app, whatever is under the pointer, like WPF's Mouse.OverrideCursor:
        /// <c>CursorService.OverrideCursor = Cursors.Wait</c> while loading. Null (the default) turns it off.
        /// </summary>
        public static Cursor OverrideCursor { get; set; }

        /// <summary>Gives <paramref name="element"/> a cursor; null clears it, so its parent's (or its built-in one) shows again.</summary>
        public static void SetCursor(Drawable element, Cursor cursor)
        {
            if (cursor == null)
            {
                cursors.Remove(element);
                return;
            }

            cursors.AddOrUpdate(element, cursor);
            PopupLayer.EnsureFor(element);
        }

        /// <summary>The cursor set on <paramref name="element"/>, or null. Built-in cursors of controls don't count.</summary>
        public static Cursor GetCursor(Drawable element) => cursors.TryGetValue(element, out var cursor) ? cursor : null;

        /// <summary>The cursor <paramref name="element"/> asks for: its own, otherwise its built-in one, otherwise null.</summary>
        internal static Cursor Resolve(Drawable element) => GetCursor(element) ?? (element as IHasDefaultCursor)?.DefaultCursor;

        /// <summary>Elements with a cursor of their own, for the popup layer's hit test.</summary>
        internal static IEnumerable<Drawable> Targets
        {
            get
            {
                foreach (var pair in (IEnumerable<KeyValuePair<Drawable, Cursor>>)cursors)
                    yield return pair.Key;
            }
        }
    }
}

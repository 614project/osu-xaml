using System;
using System.Collections.Generic;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using OsuXaml.Controls;

[assembly: MetadataUpdateHandler(typeof(OsuXaml.XamlHotReload))]

namespace OsuXaml
{
    /// <summary>Implemented by the generated half of every XAML view whose root is a drawable.</summary>
    public interface IXamlHotReloadable
    {
        /// <summary>Schedules a rebuild of the view from its (reloaded) <c>InitializeComponent</c> on its update thread.</summary>
        void ReloadXaml();
    }

    /// <summary>
    /// Connects XAML views to .NET hot reload (<c>dotnet watch</c>, or Hot Reload in Visual Studio and Rider).
    /// Saving a <c>.xaml</c> file reruns the source generator, the runtime swaps in the new <c>InitializeComponent</c>
    /// and calls <see cref="UpdateApplication"/>, the same hook osu!framework's own HotReloadCallbackReceiver uses.
    /// Every live view of an updated class then throws away what its last <c>InitializeComponent</c> built and runs it
    /// again with the same view model, so the view instance, its parent and the view model's state all stay.
    /// </summary>
    /// <remarks>
    /// Nothing is recorded unless the app runs with hot reload support (<see cref="MetadataUpdater.IsSupported"/>),
    /// so a normal run pays one boolean check per view.
    /// </remarks>
    public static class XamlHotReload
    {
        private static readonly object gate = new object();
        private static readonly List<WeakReference<Drawable>> views = new List<WeakReference<Drawable>>();
        private static readonly ConditionalWeakTable<Drawable, Entry> entries = new ConditionalWeakTable<Drawable, Entry>();

        [ThreadStatic]
        private static bool rebuilding;

        [ThreadStatic]
        private static bool tearingDown;

        /// <summary>Whether views record what they build so hot reload can rebuild them.</summary>
        public static bool IsEnabled { get; internal set; } = MetadataUpdater.IsSupported;

        /// <summary>
        /// True while a view's <c>InitializeComponent</c> runs again after a reload. Generated code skips what was set up on the
        /// view itself and can't be undone (its event handlers and collection items), since the first build already did it.
        /// </summary>
        public static bool IsRebuilding => rebuilding;

        /// <summary>True while a reloaded view disconnects its old bindings, so bound item lists also remove the rows they made.</summary>
        internal static bool IsTearingDown => tearingDown;

        /// <summary>Raised on the view's update thread after it was rebuilt.</summary>
        public static event Action<Drawable> ViewReloaded;

        /// <summary>
        /// Called at the end of a generated <c>InitializeComponent</c>: remembers the view model and the top-level children it
        /// created, which are what a rebuild removes.
        /// </summary>
        public static void Register(Drawable view, object dataContext, params object[] children)
        {
            lock (gate)
            {
                if (!entries.TryGetValue(view, out var entry))
                {
                    entries.Add(view, entry = new Entry());

                    views.RemoveAll(r => !r.TryGetTarget(out _));
                    views.Add(new WeakReference<Drawable>(view));
                }

                entry.DataContext = dataContext;
                entry.Children = children;
            }
        }

        /// <summary>The hot reload entry point: .NET calls it after applying an update.</summary>
        /// <param name="updatedTypes">The classes that changed, or null when that isn't known.</param>
        public static void UpdateApplication(Type[] updatedTypes)
        {
            var reload = new List<IXamlHotReloadable>();

            lock (gate)
            {
                foreach (var reference in views)
                {
                    if (reference.TryGetTarget(out var view) && view is IXamlHotReloadable reloadable
                                                             && (updatedTypes == null || Array.IndexOf(updatedTypes, view.GetType()) >= 0))
                        reload.Add(reloadable);
                }
            }

            foreach (var view in reload)
                view.ReloadXaml();
        }

        /// <summary>Called by .NET before <see cref="UpdateApplication"/>; nothing here caches anything.</summary>
        public static void ClearCache(Type[] updatedTypes)
        {
        }

        /// <summary>
        /// Rebuilds one view, on its update thread: disconnects its bindings, removes and disposes the children its last
        /// build made, then runs <paramref name="initializeComponent"/> (the reloaded one) with the same view model.
        /// </summary>
        public static void Rebuild(Drawable view, BindingScope bindings, Action<object> initializeComponent)
        {
            Entry entry;

            lock (gate)
            {
                if (!entries.TryGetValue(view, out entry))
                    return;
            }

            tearingDown = true;

            try
            {
                bindings.UnbindAll();

                var children = entry.Children;
                entry.Children = Array.Empty<object>();

                for (int i = children.Length - 1; i >= 0; i--)
                    remove(view, children[i]);
            }
            finally
            {
                tearingDown = false;
            }

            rebuilding = true;

            try
            {
                initializeComponent(entry.DataContext);
            }
            catch (Exception e)
            {
                Logger.Error(e, $"Reloading the XAML of {view.GetType().Name} failed");
                return;
            }
            finally
            {
                rebuilding = false;
            }

            ViewReloaded?.Invoke(view);
        }

        private static void remove(Drawable view, object child)
        {
            switch (view)
            {
                case ContentControl content when ReferenceEquals(content.Content, child):
                    content.Content = null;
                    break;

                case Panel panel when child is Drawable drawable && panel.Remove(drawable, true):
                    return;

                case ItemsControl items when items.ItemsSource == null:
                    items.RemoveItem(child);
                    break;
            }

            if (child is Drawable d)
            {
                if (d.Parent != null)
                    d.RemoveAndDisposeImmediately();
                else
                    d.Dispose();
            }
        }

        private sealed class Entry
        {
            public object DataContext;
            public object[] Children = Array.Empty<object>();
        }
    }
}

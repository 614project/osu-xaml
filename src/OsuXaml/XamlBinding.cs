using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;

namespace OsuXaml
{
    /// <summary>
    /// Runtime helpers called by code that the OsuXaml source generator emits.
    /// Everything here is strongly typed: the generator resolves binding paths at build time,
    /// so no reflection happens while the app runs.
    /// </summary>
    public static class XamlBinding
    {
        /// <summary>
        /// Pushes every value of <paramref name="source"/> into the target. The second argument of
        /// <paramref name="apply"/> is true for the initial value, so animated bindings can skip the transform.
        /// </summary>
        public static void OneWay<T>(BindingScope scope, IBindable<T> source, Action<T, bool> apply)
        {
            // The view model keeps only a weak reference to this copy; the scope keeps it alive and unbinds it with the view.
            var copy = source.GetBoundCopy();
            scope.Add(copy);

            bool initial = true;
            copy.BindValueChanged(e =>
            {
                apply(e.NewValue, initial);
                initial = false;
            }, true);
        }

        /// <summary>
        /// Links a control's own bindable (for example <c>Checkbox.Current</c>) to a view model bindable in both directions.
        /// </summary>
        public static void TwoWay<T>(Bindable<T> target, Bindable<T> source) => target.BindTo(source);

        /// <summary>
        /// Keeps the children of a WPF-style panel in sync with <paramref name="source"/>, inserting rows at their list index.
        /// </summary>
        public static void Items<T>(BindingScope scope, Controls.IItemsHost host, IBindableList<T> source, Func<T, BindingScope, Drawable> template)
            => sync(scope, source, template, (i, d) => host.Insert(i, d), d => host.Remove(d, true), null);

        /// <summary>
        /// Keeps the children of <paramref name="container"/> in sync with <paramref name="source"/>,
        /// creating one drawable per item from the compiled item template.
        /// </summary>
        public static void Items<T>(BindingScope scope, Container<Drawable> container, IBindableList<T> source, Func<T, BindingScope, Drawable> template)
            => sync(scope, source, template, (_, d) => container.Add(d), d => container.Remove(d, true), container as FlowContainer<Drawable>);

        private static void sync<T>(BindingScope scope, IBindableList<T> source, Func<T, BindingScope, Drawable> template,
                                    Action<int, Drawable> insertDrawable, Action<Drawable> removeDrawable, FlowContainer<Drawable> flow)
        {
            var copy = source.GetBoundCopy();
            scope.Add(copy);

            var items = new List<T>();
            var drawables = new List<Drawable>();
            var scopes = new List<BindingScope>();

            copy.BindCollectionChanged((_, e) =>
            {
                switch (e.Action)
                {
                    case NotifyCollectionChangedAction.Add:
                        for (int i = 0; i < e.NewItems.Count; i++)
                            insert(e.NewStartingIndex < 0 ? items.Count : e.NewStartingIndex + i, (T)e.NewItems[i]);
                        break;

                    case NotifyCollectionChangedAction.Remove:
                        // RemoveAll reports scattered items without a starting index, so match by item.
                        foreach (T item in e.OldItems)
                        {
                            int index = e.OldStartingIndex >= 0 ? e.OldStartingIndex : items.FindIndex(x => EqualityComparer<T>.Default.Equals(x, item));
                            if (index < 0)
                                continue;

                            removeAt(index);
                        }

                        break;

                    default:
                        while (items.Count > 0)
                            removeAt(items.Count - 1);

                        foreach (var item in copy)
                            insert(items.Count, item);

                        break;
                }

                // Flow containers lay children out by layout position, so keep it equal to the list index.
                if (flow != null)
                {
                    for (int i = 0; i < drawables.Count; i++)
                        flow.SetLayoutPosition(drawables[i], i);
                }
            }, true);

            void insert(int index, T item)
            {
                // Each item gets its own scope so removing the item also disconnects its bindings.
                var itemScope = new BindingScope();
                scope.Add(itemScope);

                var drawable = template(item, itemScope);
                items.Insert(index, item);
                drawables.Insert(index, drawable);
                scopes.Insert(index, itemScope);
                insertDrawable(index, drawable);
            }

            void removeAt(int index)
            {
                scopes[index].UnbindAll();
                scope.Remove(scopes[index]);
                removeDrawable(drawables[index]);

                items.RemoveAt(index);
                drawables.RemoveAt(index);
                scopes.RemoveAt(index);
            }
        }
    }
}

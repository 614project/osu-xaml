using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using osu.Framework.Bindables;
using osu.Framework.Development;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Localisation;
using OsuXaml.Controls;
using osuTK.Graphics;

namespace OsuXaml
{
    /// <summary>
    /// Supplies the values of <c>{ThemeResource Key}</c>, for a theme library: brushes, colours, sizes, anything a property takes.
    /// It is asked per element, so different parts of the tree can have different themes (from the nearest theme scope around the
    /// element, say). Register one with <see cref="ThemeResources.AddProvider"/>.
    /// </summary>
    public interface IThemeResourceProvider
    {
        /// <summary>
        /// The value of <paramref name="key"/> for <paramref name="element"/>, or false when this provider doesn't have it. Asked
        /// first while the element is being built (it has no parents yet), again once it has loaded, and after every
        /// <see cref="ResourcesChanged"/>; from then on on the update thread.
        /// </summary>
        bool TryGetResource(Drawable element, string key, out object value);

        /// <summary>
        /// Raised when values may have changed, e.g. the theme switched; every <c>{ThemeResource}</c> of a loaded element asks again.
        /// It may be raised on any thread.
        /// </summary>
        event Action ResourcesChanged;
    }

    /// <summary>
    /// Runs <c>{ThemeResource Key}</c>, WinUI's markup extension for values that follow the theme:
    /// <code>
    /// &lt;Border Background="{ThemeResource CardBackgroundFillColorDefaultBrush}" /&gt;
    /// &lt;TextBlock TextElement.Foreground="{ThemeResource TextFillColorSecondaryBrush}" /&gt;
    /// </code>
    /// Unlike <c>{StaticResource}</c>, the key isn't looked up at build time: the generated code binds the property with
    /// <see cref="Bind{T}"/>, and the providers that theme libraries add (<see cref="AddProvider"/>) supply the value at run time,
    /// converted to the property's type (<see cref="TryConvert{T}"/>). A key that no provider has, or a value that doesn't convert,
    /// leaves the property as it is.
    /// </summary>
    /// <remarks>
    /// Elements are held weakly, so a binding never keeps a removed element alive, and those that were disposed are dropped.
    /// Nothing polls: values are applied when the binding is made, when the element has loaded, and when a provider says they changed.
    /// </remarks>
    public static class ThemeResources
    {
        private static readonly object gate = new object();

        // Replaced rather than changed, so lookups read them without the lock. The last one wins.
        private static IThemeResourceProvider[] providers = Array.Empty<IThemeResourceProvider>();

        private static readonly ConditionalWeakTable<Drawable, Target> targets = new ConditionalWeakTable<Drawable, Target>();

        // Every element with bindings, for refreshing them all; collected and disposed ones are pruned as they're found.
        private static readonly List<WeakReference<Target>> tracked = new List<WeakReference<Target>>();
        private static int pruneAt = 64;

        private static readonly Action<Drawable> on_load_complete = element =>
        {
            if (targets.TryGetValue(element, out var target))
                target.Apply();
        };

        private static readonly Action on_resources_changed = () => Refresh();

        /// <summary>The providers asked for values, in the order they were added; later ones win.</summary>
        public static IReadOnlyList<IThemeResourceProvider> Providers => providers;

        /// <summary>
        /// Adds a provider, ahead of the ones added before it (adding one again moves it to the front), and applies its values to
        /// every loaded element with theme resources.
        /// </summary>
        public static void AddProvider(IThemeResourceProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);

            lock (gate)
            {
                var list = new List<IThemeResourceProvider>(providers);
                bool added = !list.Remove(provider);
                list.Add(provider);
                providers = list.ToArray();

                if (added)
                    provider.ResourcesChanged += on_resources_changed;
            }

            Refresh();
        }

        /// <summary>Removes a provider; elements take the values of the remaining ones (or keep theirs, when none has the key).</summary>
        public static void RemoveProvider(IThemeResourceProvider provider)
        {
            if (provider == null)
                return;

            lock (gate)
            {
                var list = new List<IThemeResourceProvider>(providers);
                if (!list.Remove(provider))
                    return;

                providers = list.ToArray();
                provider.ResourcesChanged -= on_resources_changed;
            }

            Refresh();
        }

        /// <summary>The value of <paramref name="key"/> for <paramref name="element"/> from the newest provider that has it.</summary>
        public static bool TryGetResource(Drawable element, string key, out object value)
        {
            var current = providers;

            for (int i = current.Length - 1; i >= 0; i--)
            {
                if (current[i].TryGetResource(element, key, out value))
                    return true;
            }

            value = null;
            return false;
        }

        /// <summary>
        /// Calls <paramref name="apply"/> with the value of <paramref name="key"/> for <paramref name="element"/>, converted to
        /// <typeparamref name="T"/>: now, again once the element has loaded (its theme scope is known then), and whenever a provider's
        /// values change, on the update thread. Disposing the result stops it; so does disposing the element.
        /// </summary>
        /// <remarks>
        /// <paramref name="apply"/> lives as long as the element. One that doesn't capture the element (or anything else) is
        /// cheaper; <see cref="Bind{TTarget, T}(Drawable, TTarget, string, Action{TTarget, T})"/> passes it in.
        /// </remarks>
        public static IDisposable Bind<T>(Drawable element, string key, Action<T> apply)
        {
            ArgumentNullException.ThrowIfNull(apply);
            return Bind<Action<T>, T>(element, apply, key, (a, value) => a(value));
        }

        /// <summary>
        /// The same as <see cref="Bind{T}"/>, for a property of <paramref name="target"/>: <paramref name="element"/> itself, or
        /// something it owns that isn't a drawable (a brush, an inline), whose themed values follow the element.
        /// <paramref name="apply"/> gets the target, so it needn't capture anything.
        /// </summary>
        public static IDisposable Bind<TTarget, T>(Drawable element, TTarget target, string key, Action<TTarget, T> apply)
        {
            ArgumentNullException.ThrowIfNull(element);
            ArgumentNullException.ThrowIfNull(key);
            ArgumentNullException.ThrowIfNull(apply);

            var registration = new Registration();
            var binding = new Binding<TTarget, T>(key, registration, target, apply);
            Target entry;

            lock (gate)
            {
                if (!targets.TryGetValue(element, out entry))
                {
                    entry = new Target(element);
                    targets.Add(element, entry);
                    tracked.Add(new WeakReference<Target>(entry));

                    if (tracked.Count >= pruneAt)
                    {
                        prune();
                        pruneAt = Math.Max(64, tracked.Count * 2);
                    }

                    if (!element.IsLoaded)
                        element.OnLoadComplete += on_load_complete;
                }

                entry.Add(binding);
            }

            // The first value, usually before the element is in the tree. A loaded element only changes on its update thread.
            if (element.IsLoaded && !ThreadSafety.IsUpdateThread)
                entry.ScheduleApply();
            else
                binding.Apply(element);

            return registration;
        }

        /// <summary>
        /// <see cref="Bind{TTarget, T}(Drawable, TTarget, string, Action{TTarget, T})"/> for generated code: the binding is added to
        /// <paramref name="scope"/>, so it stops with the view (or the template item), and a view that hot reload rebuilds binds afresh.
        /// </summary>
        public static void Bind<TTarget, T>(BindingScope scope, Drawable element, TTarget target, string key, Action<TTarget, T> apply)
        {
            ArgumentNullException.ThrowIfNull(scope);
            scope.Add((Registration)Bind(element, target, key, apply));
        }

        /// <summary>
        /// Applies the theme resources of every loaded element again (only those inside <paramref name="within"/>, when given), as a
        /// provider's <see cref="IThemeResourceProvider.ResourcesChanged"/> does. Off the update thread, each element does it on its own.
        /// </summary>
        public static void Refresh(Drawable within = null)
        {
            Target[] snapshot;
            int count;

            lock (gate)
            {
                prune();

                count = tracked.Count;
                snapshot = ArrayPool<Target>.Shared.Rent(Math.Max(1, count));
                int n = 0;

                foreach (var reference in tracked)
                {
                    if (reference.TryGetTarget(out var target))
                        snapshot[n++] = target;
                }

                count = n;
            }

            try
            {
                bool updateThread = ThreadSafety.IsUpdateThread;

                for (int i = 0; i < count; i++)
                {
                    var target = snapshot[i];

                    // Elements that haven't loaded yet take their values when they do.
                    if (!target.Element.IsLoaded || (within != null && !isWithin(target.Element, within)))
                        continue;

                    if (updateThread)
                        target.Apply();
                    else
                        target.ScheduleApply();
                }
            }
            finally
            {
                ArrayPool<Target>.Shared.Return(snapshot, true);
            }
        }

        /// <summary>The number of elements with live theme bindings, after dropping collected and disposed ones.</summary>
        internal static int TrackedElementCount
        {
            get
            {
                lock (gate)
                {
                    prune();
                    return tracked.Count;
                }
            }
        }

        private static bool isWithin(Drawable drawable, Drawable ancestor)
        {
            for (var d = drawable; d != null; d = d.Parent)
            {
                if (d == ancestor)
                    return true;
            }

            return false;
        }

        // Called with the lock held.
        private static void prune()
        {
            tracked.RemoveAll(reference =>
            {
                if (!reference.TryGetTarget(out var target))
                    return true;

                if (!FrameworkInternals.IsDisposed(target.Element))
                    return false;

                target.Clear();
                return true;
            });
        }

        /// <summary>
        /// Converts a resource to the type of the property it is for: as it is when it already has that type, a <see cref="Colour4"/>
        /// to and from a <see cref="SolidColorBrush"/> (and an osuTK <see cref="Color4"/>), a colour to a <see cref="ColourInfo"/>, one
        /// number type to another, a number to a uniform <see cref="MarginPadding"/>, text to a <see cref="LocalisableString"/> or an
        /// enum value, and any of these to a nullable property. Returns false for anything else.
        /// </summary>
        public static bool TryConvert<T>(object value, out T result)
        {
            if (value is T exact)
            {
                result = exact;
                return true;
            }

            if (value == null)
            {
                result = default;
                return default(T) == null;
            }

            if (tryConvert(value, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T), out object converted) && converted is T typed)
            {
                result = typed;
                return true;
            }

            result = default;
            return false;
        }

        private static bool tryConvert(object value, Type type, out object result)
        {
            result = null;

            if (type == typeof(Colour4))
            {
                if (!tryColour(value, out var colour))
                    return false;

                result = colour;
            }
            else if (type == typeof(Color4))
            {
                if (!tryColour(value, out var colour))
                    return false;

                result = (Color4)colour;
            }
            else if (type == typeof(ColourInfo))
            {
                if (!tryColour(value, out var colour))
                    return false;

                result = ColourInfo.SingleColour(colour);
            }
            else if (type.IsAssignableFrom(typeof(SolidColorBrush)) && type != typeof(object))
            {
                if (!tryColour(value, out var colour))
                    return false;

                result = new SolidColorBrush(colour);
            }
            else if (type == typeof(MarginPadding))
            {
                if (!tryNumber(value, out double number))
                    return false;

                result = new MarginPadding((float)number);
            }
            else if (type == typeof(LocalisableString))
            {
                if (!(value is string text))
                    return false;

                result = new LocalisableString(text);
            }
            else if (type.IsEnum)
            {
                if (!(value is string name) || !Enum.TryParse(type, name, true, out result))
                    return false;
            }
            else if (isNumber(Type.GetTypeCode(type)))
            {
                if (!tryNumber(value, out double number))
                    return false;

                try
                {
                    result = Convert.ChangeType(number, type, CultureInfo.InvariantCulture);
                }
                catch (OverflowException)
                {
                    return false;
                }
            }

            return result != null;
        }

        private static bool tryColour(object value, out Colour4 colour)
        {
            switch (value)
            {
                case Colour4 c:
                    colour = c;
                    return true;

                case Color4 c:
                    colour = c;
                    return true;

                case SolidColorBrush brush:
                    colour = brush.Color.MultiplyAlpha(brush.Opacity);
                    return true;

                case ColourInfo info when info.HasSingleColour:
                    colour = info;
                    return true;

                default:
                    colour = default;
                    return false;
            }
        }

        private static bool tryNumber(object value, out double number)
        {
            if (value is IConvertible convertible && isNumber(convertible.GetTypeCode()))
            {
                number = convertible.ToDouble(CultureInfo.InvariantCulture);
                return true;
            }

            number = 0;
            return false;
        }

        private static bool isNumber(TypeCode code) => code >= TypeCode.SByte && code <= TypeCode.Decimal;

        /// <summary>What <see cref="Bind{T}"/> returns, and generated code adds to its <see cref="BindingScope"/>.</summary>
        private sealed class Registration : IDisposable, IUnbindable
        {
            private volatile bool removed;

            public bool Removed => removed;

            public void Dispose() => removed = true;

            public void UnbindAll() => Dispose();

            public void UnbindEvents()
            {
            }

            public void UnbindBindings() => Dispose();

            void IUnbindable.UnbindFrom(IUnbindable them) => throw new NotSupportedException();
        }

        /// <summary>One themed property. It lives in its element's <see cref="Target"/>, so it lives as long as the element.</summary>
        private abstract class Binding
        {
            public readonly string Key;
            public readonly Registration Registration;

            protected Binding(string key, Registration registration)
            {
                Key = key;
                Registration = registration;
            }

            public abstract void Apply(Drawable element);
        }

        private sealed class Binding<TTarget, T> : Binding
        {
            private readonly TTarget target;
            private readonly Action<TTarget, T> apply;

            public Binding(string key, Registration registration, TTarget target, Action<TTarget, T> apply)
                : base(key, registration)
            {
                this.target = target;
                this.apply = apply;
            }

            public override void Apply(Drawable element)
            {
                if (TryGetResource(element, Key, out object value) && TryConvert(value, out T converted))
                    apply(target, converted);
            }
        }

        /// <summary>The bindings of one element. Only the element's entry in the weak table holds it.</summary>
        private sealed class Target
        {
            public readonly Drawable Element;

            // Replaced rather than changed (elements have few), so applying reads them without the lock.
            private Binding[] bindings = Array.Empty<Binding>();

            private readonly Action applyScheduled;
            private int scheduled;

            public Target(Drawable element)
            {
                Element = element;

                applyScheduled = () =>
                {
                    Interlocked.Exchange(ref scheduled, 0);
                    Apply();
                };
            }

            // Called with the lock held.
            public void Add(Binding binding)
            {
                var added = new Binding[bindings.Length + 1];
                bindings.CopyTo(added, 0);
                added[^1] = binding;
                bindings = added;
            }

            // Called with the lock held, for a disposed element: what the bindings refer to can go.
            public void Clear() => bindings = Array.Empty<Binding>();

            public void Apply()
            {
                var current = bindings;
                bool removed = false;

                foreach (var binding in current)
                {
                    if (binding.Registration.Removed)
                        removed = true;
                    else
                        binding.Apply(Element);
                }

                if (removed)
                {
                    lock (gate)
                        bindings = Array.FindAll(bindings, b => !b.Registration.Removed);
                }
            }

            /// <summary>Applies the bindings on the element's update thread, once however often this is called before that.</summary>
            public void ScheduleApply()
            {
                if (Interlocked.Exchange(ref scheduled, 1) == 0)
                    FrameworkInternals.Schedule(Element, applyScheduled);
            }
        }
    }
}

using System;
using System.Collections;
using osu.Framework.Bindables;
using osu.Framework.Graphics;

namespace OsuXaml
{
    /// <summary>
    /// Builds the drawable for one data item, like WPF's DataTemplate. XAML compiles
    /// <c>&lt;DataTemplate x:DataType="local:Person"&gt;</c> into a <see cref="DataTemplate{T}"/> whose
    /// body is plain C#, so building a row costs the same as hand-written code.
    /// </summary>
    public abstract class DataTemplate
    {
        /// <summary>The item type the template was written for.</summary>
        public abstract Type DataType { get; }

        /// <summary>Whether <paramref name="item"/> has the type this template expects.</summary>
        public abstract bool CanBuild(object item);

        /// <summary>
        /// Creates the drawable for <paramref name="item"/>. Bindings made by the template are added to
        /// <paramref name="scope"/>, which the caller unbinds when the drawable goes away.
        /// </summary>
        public abstract Drawable Build(object item, BindingScope scope);
    }

    /// <summary>A <see cref="DataTemplate"/> for items of type <typeparamref name="T"/>.</summary>
    public class DataTemplate<T> : DataTemplate
    {
        private readonly Func<T, BindingScope, Drawable> build;

        public DataTemplate(Func<T, BindingScope, Drawable> build)
        {
            this.build = build ?? throw new ArgumentNullException(nameof(build));
        }

        public override Type DataType => typeof(T);

        public override bool CanBuild(object item) => item is T || (item == null && default(T) == null);

        public override Drawable Build(object item, BindingScope scope) => build((T)item, scope);
    }

    /// <summary>A template whose items have children, such as tree nodes.</summary>
    public interface IHierarchicalTemplate
    {
        /// <summary>The children of <paramref name="item"/>. Observable collections keep the tree in sync.</summary>
        IEnumerable GetChildren(object item, BindingScope scope);
    }

    /// <summary>
    /// Like WPF's HierarchicalDataTemplate: <c>ItemsSource="{Bind Children}"</c> on the template names each item's children.
    /// </summary>
    public class HierarchicalDataTemplate<T> : DataTemplate<T>, IHierarchicalTemplate
    {
        private readonly Func<T, BindingScope, IEnumerable> children;

        public HierarchicalDataTemplate(Func<T, BindingScope, Drawable> build, Func<T, BindingScope, IEnumerable> children)
            : base(build)
        {
            this.children = children ?? throw new ArgumentNullException(nameof(children));
        }

        public IEnumerable GetChildren(object item, BindingScope scope) => item is T t ? children(t, scope) : null;
    }

    /// <summary>
    /// Replaces the look of a control, like WPF's ControlTemplate. Inside the template,
    /// <c>{TemplateBinding Background}</c> reads the control's own properties and
    /// <c>&lt;ContentPresenter /&gt;</c> shows the control's content.
    /// </summary>
    public abstract class ControlTemplate
    {
        public abstract Type TargetType { get; }

        public abstract Drawable Build(object control, BindingScope scope);
    }

    /// <summary>A <see cref="ControlTemplate"/> for controls of type <typeparamref name="T"/>.</summary>
    public class ControlTemplate<T> : ControlTemplate
    {
        private readonly Func<T, BindingScope, Drawable> build;

        public ControlTemplate(Func<T, BindingScope, Drawable> build)
        {
            this.build = build ?? throw new ArgumentNullException(nameof(build));
        }

        public override Type TargetType => typeof(T);

        public override Drawable Build(object control, BindingScope scope) => build((T)control, scope);
    }

    /// <summary>
    /// A compiled path into each item, for columns and other per-item settings: <c>Binding="{Bind Done}"</c> on a
    /// member of this type becomes <c>new ItemBinding&lt;Todo, bool&gt;("Done", x =&gt; x.Done, x =&gt; x.Done.Value)</c>.
    /// The item type comes from the nearest ItemsSource. No reflection is involved.
    /// </summary>
    public abstract class ItemBinding
    {
        protected ItemBinding(string path)
        {
            Path = path;
        }

        /// <summary>The path as written in XAML, e.g. for column headers or sorting.</summary>
        public string Path { get; }

        public abstract Type ItemType { get; }

        /// <summary>The value type; for a bindable member this is the bindable's T.</summary>
        public abstract Type ValueType { get; }

        /// <summary>The member itself: the view model's Bindable for bindable members (bind to it for editing), otherwise the value.</summary>
        public abstract object GetSource(object item);

        /// <summary>The member's current value.</summary>
        public abstract object GetValue(object item);

        /// <summary>
        /// Calls <paramref name="changed"/> with the current value and again whenever a bindable member changes.
        /// The bound copy this needs is added to <paramref name="scope"/>.
        /// </summary>
        public abstract void Observe(object item, BindingScope scope, Action<object> changed);
    }

    /// <summary>An <see cref="ItemBinding"/> for items of type <typeparamref name="TItem"/>.</summary>
    public sealed class ItemBinding<TItem, TValue> : ItemBinding
    {
        private readonly Func<TItem, object> source;
        private readonly Func<TItem, TValue> value;

        public ItemBinding(string path, Func<TItem, object> source, Func<TItem, TValue> value)
            : base(path)
        {
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            this.value = value ?? throw new ArgumentNullException(nameof(value));
        }

        public override Type ItemType => typeof(TItem);

        public override Type ValueType => typeof(TValue);

        public override object GetSource(object item) => item is TItem t ? source(t) : null;

        public override object GetValue(object item) => item is TItem t ? value(t) : null;

        /// <summary>The current value, typed.</summary>
        public TValue GetTypedValue(TItem item) => value(item);

        public override void Observe(object item, BindingScope scope, Action<object> changed)
        {
            if (!(item is TItem t))
            {
                changed(null);
                return;
            }

            if (source(t) is IBindable<TValue> bindable)
            {
                var copy = bindable.GetBoundCopy();
                scope.Add(copy);
                copy.BindValueChanged(e => changed(e.NewValue), true);
            }
            else
                changed(value(t));
        }
    }
}

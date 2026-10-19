using System.Collections.Generic;
using osu.Framework.Bindables;

namespace OsuXaml
{
    /// <summary>
    /// Owns the bound copies created by a XAML view (or by one item of an ItemsSource).
    /// The generator stores the view's scope in a field, so osu!framework's own disposal,
    /// which unbinds every <see cref="IUnbindable"/> field, also disconnects the view from its view model.
    /// </summary>
    public sealed class BindingScope : IUnbindable
    {
        private readonly List<IUnbindable> bindings = new List<IUnbindable>();

        public void Add(IUnbindable bindable) => bindings.Add(bindable);

        public void Remove(IUnbindable bindable) => bindings.Remove(bindable);

        public void UnbindAll()
        {
            // Unbinding can remove item scopes from this one (a bound list taking its rows down), so walk a copy.
            foreach (var b in bindings.ToArray())
                b.UnbindAll();

            bindings.Clear();
        }

        public void UnbindEvents()
        {
            foreach (var b in bindings)
                b.UnbindEvents();
        }

        public void UnbindBindings()
        {
            foreach (var b in bindings)
                b.UnbindBindings();
        }

        void IUnbindable.UnbindFrom(IUnbindable them) => throw new System.NotSupportedException("A binding scope is not itself bound to anything.");
    }
}

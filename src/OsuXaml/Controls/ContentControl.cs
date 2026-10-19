using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Localisation;
using osu.Framework.Platform;
using osuTK;

namespace OsuXaml.Controls
{
    /// <summary>Default colours for the controls; every one can be overridden per element.</summary>
    public static class Theme
    {
        /// <summary>Window background.</summary>
        public static Colour4 Base = Colour4.FromHex("1e1e2e");

        /// <summary>Slightly darker panels such as headers and status bars.</summary>
        public static Colour4 Mantle = Colour4.FromHex("181825");

        public static Colour4 Crust = Colour4.FromHex("11111b");
        public static Colour4 Text = Colour4.FromHex("cdd6f4");
        public static Colour4 SubtleText = Colour4.FromHex("a6adc8");

        /// <summary>Control backgrounds (text boxes, tracks, list items).</summary>
        public static Colour4 Surface = Colour4.FromHex("313244");

        /// <summary>Hovered control backgrounds.</summary>
        public static Colour4 Hover = Colour4.FromHex("3b3d52");

        public static Colour4 Border = Colour4.FromHex("45475a");
        public static Colour4 Overlay = Colour4.FromHex("6c7086");
        public static Colour4 Accent = Colour4.FromHex("89b4fa");
        public static Colour4 AccentText = Colour4.FromHex("1e1e2e");
        public static Colour4 Success = Colour4.FromHex("a6e3a1");
        public static Colour4 Warning = Colour4.FromHex("f9e2af");
        public static Colour4 Danger = Colour4.FromHex("f38ba8");
    }

    /// <summary>
    /// Base for controls with one piece of content (Border, Button, Window), like WPF's ContentControl.
    /// <see cref="Content"/> can be text, a drawable, or any object shown through <see cref="ContentTemplate"/>.
    /// The presented content sits in a <see cref="Slot"/>, so it follows the same alignment rules as panel children.
    /// Setting <see cref="Template"/> replaces the default look; a <see cref="ContentPresenter"/> in the template shows the content.
    /// </summary>
    public abstract partial class ContentControl : CompositeDrawable, ILayoutElement
    {
        /// <summary>Holds the chrome (background, border, corner masking); animations can scale it around its centre.</summary>
        protected readonly Container Chrome;

        protected readonly Box BackgroundBox;

        private readonly Container contentArea;
        private Slot slot;
        private bool ownsPresented;
        private Drawable presented;

        private object content;
        private DataTemplate contentTemplate;
        private ControlTemplate template;
        private Slot templateSlot;
        private ContentPresenter presenter;
        private bool presentedOnce;

        // The content alignments this control gave the presented content, so later changes replace them and a detach undoes them.
        private HorizontalAlignment? givenHorizontal;
        private VerticalAlignment? givenVertical;

        private Colour4 foreground = Theme.Text;
        private HorizontalAlignment horizontalContentAlignment;
        private VerticalAlignment verticalContentAlignment;

        // Fields, so osu!framework's disposal also unbinds them.
        private BindingScope contentBindings;
        private BindingScope templateBindings;

        private bool sizeToContentX;
        private bool sizeToContentY;

        protected ContentControl()
        {
            InternalChild = Chrome = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Masking = true,
                Children = new Drawable[]
                {
                    BackgroundBox = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0 },
                    contentArea = new Container { RelativeSizeAxes = Axes.Both },
                },
            };
        }

        /// <summary>
        /// What the control shows: a string becomes a <see cref="TextBlock"/>, a drawable is shown as is,
        /// and other objects go through <see cref="ContentTemplate"/> (or their ToString()).
        /// </summary>
        public object Content
        {
            get => content;
            set
            {
                if (ReferenceEquals(content, value))
                    return;

                content = value;
                contentChanged();
            }
        }

        /// <summary>Builds the drawable for a non-drawable <see cref="Content"/>.</summary>
        public DataTemplate ContentTemplate
        {
            get => contentTemplate;
            set
            {
                contentTemplate = value;
                contentChanged();
            }
        }

        /// <summary>The content as a drawable; XAML child elements land here.</summary>
        public Drawable Child
        {
            get => content as Drawable;
            set => Content = value;
        }

        /// <summary>The drawable currently shown for <see cref="Content"/>, once loaded.</summary>
        public Drawable PresentedContent => presenter != null ? presenter.PresentedContent : presented;

        /// <summary>Replaces the default look. Applied when the control loads.</summary>
        public ControlTemplate Template
        {
            get => template;
            set
            {
                template = value;

                if (presentedOnce)
                    applyTemplate();
            }
        }

        /// <summary>Colour of text content.</summary>
        public Colour4 Foreground
        {
            get => foreground;
            set
            {
                foreground = value;
                if (ownsPresented && presented is TextBlock text)
                    text.Colour = value;

                OnForegroundChanged();
            }
        }

        /// <summary>Alignment given to content that doesn't set its own.</summary>
        public HorizontalAlignment HorizontalContentAlignment
        {
            get => horizontalContentAlignment;
            set
            {
                horizontalContentAlignment = value;
                if (presented != null) alignContent(presented);
            }
        }

        public VerticalAlignment VerticalContentAlignment
        {
            get => verticalContentAlignment;
            set
            {
                verticalContentAlignment = value;
                if (presented != null) alignContent(presented);
            }
        }

        public Colour4 Background
        {
            get => BackgroundBox.Colour;
            set
            {
                BackgroundBox.Colour = value;
                BackgroundBox.Alpha = 1;
            }
        }

        public Colour4 BorderBrush
        {
            get => Chrome.BorderColour;
            set => Chrome.BorderColour = value;
        }

        public float BorderThickness
        {
            get => Chrome.BorderThickness;
            set
            {
                if (Chrome.BorderThickness != value)
                    Layout.InvalidateMeasure(this);

                Chrome.BorderThickness = value;
                contentArea.Padding = Layout.Add(new MarginPadding(value), padding);

                // osu!framework draws a masked container's border only over pixels its children draw, so without a
                // background the (invisible) background box still has to be drawn for the border to show.
                BackgroundBox.AlwaysPresent = value > 0;
            }
        }

        public float CornerRadius
        {
            get => Chrome.CornerRadius;
            set => Chrome.CornerRadius = value;
        }

        private MarginPadding padding;

        public new MarginPadding Padding
        {
            get => padding;
            set
            {
                if (!Layout.SetAffectsMeasure(this, ref padding, value))
                    return;

                contentArea.Padding = Layout.Add(new MarginPadding(Chrome.BorderThickness), value);
            }
        }

        /// <summary>
        /// Called by compiled ControlTemplates for a <c>&lt;ContentPresenter /&gt;</c> without its own Content:
        /// the presenter shows this control's content from now on.
        /// </summary>
        public void AttachPresenter(ContentPresenter contentPresenter)
        {
            presenter = contentPresenter;
            presenter.ContentTemplate = contentTemplate;
            presenter.Content = content;
        }

        /// <summary>Creates the drawable for text content. Controls override this for their own text style.</summary>
        protected virtual Drawable CreateTextContent(LocalisableString text) => new TextBlock { Text = text, Colour = foreground };

        /// <summary>Called after the presented content changes.</summary>
        protected virtual void OnContentPresented(Drawable drawable)
        {
        }

        /// <summary>Called after <see cref="Foreground"/> changes, also from the constructors of derived controls.</summary>
        protected virtual void OnForegroundChanged()
        {
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            presentedOnce = true;

            if (template != null)
                applyTemplate();
            else
                present();
        }

        private void contentChanged()
        {
            if (!presentedOnce)
                return;

            if (presenter != null)
            {
                presenter.ContentTemplate = contentTemplate;
                presenter.Content = content;
            }
            else
                present();
        }

        private void present()
        {
            Layout.InvalidateMeasure(this);
            detachPresented();

            switch (content)
            {
                case null:
                    return;

                case Drawable d:
                    presented = d;
                    ownsPresented = false;
                    break;

                default:
                    if (contentTemplate != null && contentTemplate.CanBuild(content))
                    {
                        contentBindings = new BindingScope();
                        presented = contentTemplate.Build(content, contentBindings);
                    }
                    else
                        presented = CreateTextContent(content is LocalisableString l ? l : content.ToString());

                    ownsPresented = true;
                    break;
            }

            if (presented == null)
                return;

            alignContent(presented);
            contentArea.Add(slot = new Slot(presented));
            OnContentPresented(presented);
        }

        private void detachPresented()
        {
            contentBindings?.UnbindAll();
            contentBindings = null;

            if (slot == null)
                return;

            // Drawables handed in as Content belong to the caller, so they are detached rather than disposed,
            // getting back the margin and size the slot took over.
            if (!ownsPresented)
            {
                slot.Detach();
                unalignContent(presented);
            }

            givenHorizontal = null;
            givenVertical = null;

            contentArea.Remove(slot, true);
            slot = null;
            presented = null;
        }

        private void alignContent(Drawable drawable)
        {
            var props = Layout.Get(drawable);

            // Content that set its own alignment keeps it; an alignment this control gave is replaced on change.
            if (props.HorizontalAlignment == HorizontalAlignment.Stretch || props.HorizontalAlignment == givenHorizontal)
                props.HorizontalAlignment = (givenHorizontal = horizontalContentAlignment).Value;
            if (props.VerticalAlignment == VerticalAlignment.Stretch || props.VerticalAlignment == givenVertical)
                props.VerticalAlignment = (givenVertical = verticalContentAlignment).Value;
        }

        private void unalignContent(Drawable drawable)
        {
            var props = Layout.Get(drawable);

            if (givenHorizontal != null && props.HorizontalAlignment == givenHorizontal)
                props.HorizontalAlignment = HorizontalAlignment.Stretch;
            if (givenVertical != null && props.VerticalAlignment == givenVertical)
                props.VerticalAlignment = VerticalAlignment.Stretch;
        }

        private void applyTemplate()
        {
            Layout.InvalidateMeasure(this);
            detachPresented();

            if (templateSlot != null)
            {
                // Hand the content back before the old template tree (and its presenter) is disposed.
                presenter?.DetachContent();
                presenter = null;
                templateBindings?.UnbindAll();
                RemoveInternal(templateSlot, true);
                templateSlot = null;
            }

            if (template == null)
            {
                Chrome.Alpha = 1;
                present();
                return;
            }

            Chrome.Alpha = 0;
            templateBindings = new BindingScope();

            var root = template.Build(this, templateBindings);
            if (root != null)
                AddInternal(templateSlot = new Slot(root));
        }

        /// <summary>
        /// True: the content and template sit in slots, which pass on their size changes, and the properties that change the size
        /// asked for report themselves. A derived control whose measuring reads anything else reports changes to it too.
        /// </summary>
        public virtual bool ReportsMeasureChanges => true;

        public virtual Vector2 Measure(Vector2 availableSize)
        {
            if (templateSlot != null)
                return templateSlot.Measure(availableSize);

            var chrome = new Vector2(contentArea.Padding.TotalHorizontal, contentArea.Padding.TotalVertical);
            var desired = slot?.Measure(Vector2.ComponentMax(Vector2.Zero, availableSize - chrome)) ?? Vector2.Zero;
            return desired + chrome;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            sizeToContentX = !(Parent is Slot) && !RelativeSizeAxes.HasFlag(Axes.X) && !AutoSizeAxes.HasFlag(Axes.X) && Width == 0;
            sizeToContentY = !(Parent is Slot) && !RelativeSizeAxes.HasFlag(Axes.Y) && !AutoSizeAxes.HasFlag(Axes.Y) && Height == 0;
        }

        protected override void UpdateAfterChildren()
        {
            base.UpdateAfterChildren();

            if (sizeToContentX || sizeToContentY)
            {
                var desired = Measure(new Vector2(float.PositiveInfinity));
                if (sizeToContentX) Width = desired.X;
                if (sizeToContentY) Height = desired.Y;
            }

            if (templateSlot != null)
                templateSlot.Arrange(Vector2.Zero, DrawSize);
            else
                slot?.Arrange(Vector2.Zero, contentArea.ChildSize);
        }

        protected override void Dispose(bool isDisposing)
        {
            contentBindings?.UnbindAll();
            templateBindings?.UnbindAll();
            base.Dispose(isDisposing);
        }
    }

    /// <summary>Draws a background, border and rounded corners around one child.</summary>
    public partial class Border : ContentControl
    {
    }

    /// <summary>
    /// Shows a piece of content through an optional template, without any chrome of its own.
    /// In a ControlTemplate, <c>&lt;ContentPresenter /&gt;</c> shows the templated control's content.
    /// </summary>
    public partial class ContentPresenter : ContentControl
    {
        /// <summary>Detaches caller-owned content so it survives this presenter being disposed.</summary>
        internal void DetachContent() => Content = null;
    }

    /// <summary>
    /// Like WPF's UserControl: the root of a reusable piece of XAML (<c>&lt;UserControl x:Class="App.SettingsPage"&gt;</c>).
    /// It is a chrome-less ContentControl, so its content stretches to whatever space the parent gives it.
    /// </summary>
    public partial class UserControl : ContentControl
    {
    }

    /// <summary>
    /// Like WPF's Window: the root of a screen, with a background, one child and a layer that
    /// popups (ComboBox drop-downs, menus, tooltips) open in. <c>Title</c> sets the OS window title.
    /// </summary>
    public partial class Window : ContentControl, IPopupLayerProvider
    {
        private string title;

        [Resolved(canBeNull: true)]
        private GameHost host { get; set; }

        public Window()
        {
            RelativeSizeAxes = Axes.Both;
            Background = Theme.Base;
            Chrome.Masking = false;

            AddInternal(PopupLayer = new PopupLayer());
        }

        public PopupLayer PopupLayer { get; }

        public string Title
        {
            get => title;
            set
            {
                title = value;
                if (IsLoaded) applyTitle();
            }
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // Keep popups above template content added after construction.
            ChangeInternalChildDepth(PopupLayer, float.MinValue);
            applyTitle();
        }

        private void applyTitle()
        {
            if (title != null && host?.Window != null)
                host.Window.Title = title;
        }
    }
}

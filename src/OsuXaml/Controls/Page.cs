using System;
using System.ComponentModel;
using osu.Framework.Graphics;

namespace OsuXaml.Controls
{
    /// <summary>How a navigation moved through a <see cref="Frame"/>'s journal, with WPF's names.</summary>
    public enum NavigationMode
    {
        /// <summary>To new content; the forward stack is dropped.</summary>
        New,

        Back,
        Forward,
    }

    /// <summary>Describes a finished navigation (<see cref="Frame.Navigated"/>).</summary>
    public class NavigationEventArgs : EventArgs
    {
        public NavigationEventArgs(Frame navigator, object content, NavigationMode navigationMode)
        {
            Navigator = navigator;
            Content = content;
            NavigationMode = navigationMode;
        }

        /// <summary>The frame that navigated.</summary>
        public Frame Navigator { get; }

        /// <summary>The content navigated to: the page, drawable or object passed to Navigate.</summary>
        public object Content { get; }

        public NavigationMode NavigationMode { get; }
    }

    /// <summary>Describes a navigation that is about to happen (<see cref="Frame.Navigating"/>); set <c>Cancel</c> to stay put.</summary>
    public class NavigatingCancelEventArgs : CancelEventArgs
    {
        public NavigatingCancelEventArgs(Frame navigator, object content, NavigationMode navigationMode)
        {
            Navigator = navigator;
            Content = content;
            NavigationMode = navigationMode;
        }

        public Frame Navigator { get; }

        /// <summary>The content about to be shown.</summary>
        public object Content { get; }

        public NavigationMode NavigationMode { get; }
    }

    /// <summary>
    /// Like WPF's Page: the root of one screen shown in a <see cref="Frame"/> (<c>&lt;Page x:Class="App.HomePage" Title="Home"&gt;</c>).
    /// It is a chrome-less ContentControl that fills the frame. While a frame hosts it, <see cref="NavigationService"/> lets
    /// the page navigate that frame: <c>NavigationService.Navigate(new DetailsPage(item))</c>, <c>NavigationService.GoBack()</c>.
    /// </summary>
    public partial class Page : ContentControl
    {
        /// <summary>A name for the page, for navigation history and headers.</summary>
        public string Title { get; set; }

        /// <summary>The navigation service of the frame showing this page; null until a frame navigates to it.</summary>
        public NavigationService NavigationService { get; internal set; }

        protected override void Dispose(bool isDisposing)
        {
            NavigationService = null;
            base.Dispose(isDisposing);
        }
    }

    /// <summary>
    /// Navigates a <see cref="Frame"/> on behalf of the content inside it, like WPF's NavigationService.
    /// Pages get it as <see cref="Page.NavigationService"/>; any element can look it up with <see cref="GetNavigationService"/>.
    /// </summary>
    public sealed class NavigationService
    {
        internal NavigationService(Frame frame)
        {
            Frame = frame;
        }

        /// <summary>The frame this service navigates.</summary>
        public Frame Frame { get; }

        /// <summary>The current content. Setting it navigates to the new content.</summary>
        public object Content
        {
            get => Frame.Content;
            set => Frame.Navigate(value);
        }

        /// <summary>Whether there is an entry to go back to.</summary>
        public bool CanGoBack => Frame.CanGoBack.Value;

        /// <summary>Whether there is an entry to go forward to.</summary>
        public bool CanGoForward => Frame.CanGoForward.Value;

        /// <inheritdoc cref="Controls.Frame.Navigate"/>
        public bool Navigate(object content) => Frame.Navigate(content);

        /// <inheritdoc cref="Controls.Frame.GoBack"/>
        public void GoBack() => Frame.GoBack();

        /// <inheritdoc cref="Controls.Frame.GoForward"/>
        public void GoForward() => Frame.GoForward();

        /// <inheritdoc cref="Controls.Frame.RemoveBackEntry"/>
        public object RemoveBackEntry() => Frame.RemoveBackEntry();

        /// <summary>Raised after the frame navigates.</summary>
        public event Action<NavigationEventArgs> Navigated
        {
            add => Frame.Navigated += value;
            remove => Frame.Navigated -= value;
        }

        /// <summary>Raised before the frame navigates; set <c>Cancel</c> to stay on the current content.</summary>
        public event Action<NavigatingCancelEventArgs> Navigating
        {
            add => Frame.Navigating += value;
            remove => Frame.Navigating -= value;
        }

        /// <summary>
        /// The service of the frame <paramref name="element"/> is shown in: the nearest <see cref="Frame"/> above it, or for a page
        /// kept in a frame's history, the page's own service. Null outside any frame.
        /// </summary>
        public static NavigationService GetNavigationService(Drawable element)
        {
            for (var d = element; d != null; d = d.Parent)
            {
                switch (d)
                {
                    case Page { NavigationService: not null } page:
                        return page.NavigationService;

                    case Frame frame when d != element:
                        return frame.NavigationService;
                }
            }

            return null;
        }
    }
}

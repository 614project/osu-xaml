using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using OsuXaml.Controls;

namespace OsuXaml.Tests
{
    /// <summary>A dialog's own popup layer goes with the dialog, but what others put there stays theirs.</summary>
    [HeadlessTest]
    public partial class DialogPopupLayerTests : TestScene
    {
        [Test]
        public void WhatADialogsLayerHoldsForOthersOutlivesTheDialog()
        {
            Window window = null;
            Dialog dialog = null;
            TextBlock inDialog = null, inWindow = null;
            Container holder = null;
            Popup popup = null;

            AddStep("create", () =>
            {
                Child = window = new Window { Child = inWindow = new TextBlock { Text = "In the window" } };

                // Made in code, as a tip that points at an element: it joins the layer of whatever it opens at.
                holder = new Container { Child = popup = new Popup { StaysOpen = true, Child = new TextBlock { Text = "Tip" } } };
            });
            AddStep("show a dialog", () =>
            {
                dialog = new Dialog { Title = "Dialog", Content = inDialog = new TextBlock { Text = "In the dialog" } };
                dialog.ShowDialog(inWindow);
            });
            AddUntilStep("dialog loaded", () => inDialog.IsLoaded);

            AddStep("open the popup at an element of the dialog", () =>
            {
                dialog.Host.PopupLayer.Host(holder);
                popup.PlacementTarget = inDialog;
                popup.Open();
            });
            AddUntilStep("open in the dialog's layer", () => dialog.Host.PopupLayer.OpenPopups.Contains(popup) && popup.Root.IsPresent);

            AddStep("close the dialog", () => dialog.Close());
            AddUntilStep("the dialog is gone", () => dialog.Host.Parent == null);
            AddAssert("the popup closed and left, and nothing of it was disposed", () =>
                !popup.IsOpen.Value && holder.Parent == null && popup.Root.Parent == null
                && !FrameworkInternals.IsDisposed(holder) && !FrameworkInternals.IsDisposed(popup) && !FrameworkInternals.IsDisposed(popup.Root));

            AddStep("open it in the window", () =>
            {
                window.PopupLayer.Host(holder);
                popup.PlacementTarget = inWindow;
                popup.Open();
            });
            AddUntilStep("it shows there", () => window.PopupLayer.OpenPopups.Contains(popup) && popup.Root.IsPresent && popup.Root.Parent != null);
        }
    }
}

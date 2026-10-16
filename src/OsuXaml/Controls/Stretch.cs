using System;
using osuTK;

namespace OsuXaml.Controls
{
    /// <summary>How content is resized to fill the space it is given, like WPF's Stretch.</summary>
    public enum Stretch
    {
        /// <summary>Keeps its natural size.</summary>
        None,

        /// <summary>Fills the space exactly, distorting the aspect ratio if needed.</summary>
        Fill,

        /// <summary>As large as fits while keeping the aspect ratio.</summary>
        Uniform,

        /// <summary>Covers the whole space while keeping the aspect ratio; the overflow is cut off.</summary>
        UniformToFill,
    }

    /// <summary>Whether stretching may enlarge content, shrink it, or both.</summary>
    public enum StretchDirection
    {
        UpOnly,
        DownOnly,
        Both,
    }

    /// <summary>The scaling rules shared by <see cref="Image"/>, <see cref="Viewbox"/> and the shapes, following WPF's Viewbox.</summary>
    public static class StretchMath
    {
        /// <summary>
        /// The scale that makes content of <paramref name="contentSize"/> fill <paramref name="availableSize"/>.
        /// An unlimited axis follows the limited one (or stays at 1 when neither is limited), so content in a vertical
        /// StackPanel scales to the panel's width, as in WPF.
        /// </summary>
        public static Vector2 ComputeScale(Vector2 availableSize, Vector2 contentSize, Stretch stretch, StretchDirection direction = StretchDirection.Both)
        {
            float scaleX = 1, scaleY = 1;

            bool limitedX = float.IsFinite(availableSize.X);
            bool limitedY = float.IsFinite(availableSize.Y);

            if (stretch == Stretch.None || (!limitedX && !limitedY))
                return Vector2.One;

            scaleX = contentSize.X == 0 ? 0 : availableSize.X / contentSize.X;
            scaleY = contentSize.Y == 0 ? 0 : availableSize.Y / contentSize.Y;

            if (!limitedX)
                scaleX = scaleY;
            else if (!limitedY)
                scaleY = scaleX;
            else
            {
                switch (stretch)
                {
                    case Stretch.Uniform:
                        scaleX = scaleY = Math.Min(scaleX, scaleY);
                        break;

                    case Stretch.UniformToFill:
                        scaleX = scaleY = Math.Max(scaleX, scaleY);
                        break;
                }
            }

            switch (direction)
            {
                case StretchDirection.UpOnly:
                    scaleX = Math.Max(1, scaleX);
                    scaleY = Math.Max(1, scaleY);
                    break;

                case StretchDirection.DownOnly:
                    scaleX = Math.Min(1, scaleX);
                    scaleY = Math.Min(1, scaleY);
                    break;
            }

            return new Vector2(scaleX, scaleY);
        }

        /// <summary>The size content of <paramref name="contentSize"/> takes after <see cref="ComputeScale"/>.</summary>
        public static Vector2 ComputeSize(Vector2 availableSize, Vector2 contentSize, Stretch stretch, StretchDirection direction = StretchDirection.Both)
            => contentSize * ComputeScale(availableSize, contentSize, stretch, direction);
    }
}

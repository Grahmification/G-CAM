using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using GCam.Core.Tooling;

namespace GCam.UI.Controls
{
    /// <summary>
    /// Draws a tool's 2D silhouette on a millimetre grid.
    /// </summary>
    /// <remarks>
    /// Rendered directly in OnRender rather than assembled from shapes: the grid is
    /// dozens of lines whose spacing depends on the current zoom, and drawing it
    /// imperatively is far simpler than generating and recycling that many elements.
    ///
    /// The cutter profile is a half-silhouette - radius against height - so it is
    /// mirrored about the axis here to read as a tool rather than an outline.
    /// </remarks>
    public sealed class ToolProfileView : FrameworkElement
    {
        /// <summary>Fine grid spacing, mm.</summary>
        private const double FineGridMm = 1.0;

        /// <summary>Coarse grid spacing, mm.</summary>
        private const double CoarseGridMm = 10.0;

        /// <summary>
        /// Below this many device pixels between fine lines, the fine grid is dropped.
        /// A 1mm grid on a 60mm tool in a small panel is a grey smear, not information.
        /// </summary>
        private const double MinimumGridPixelSpacing = 4.0;

        private const double MarginPixels = 12.0;

        private static readonly Brush BackgroundBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xFC, 0xFC, 0xFC)));
        private static readonly Pen FineGridPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xEC, 0xEC, 0xEC)), 1));
        private static readonly Pen CoarseGridPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xD2, 0xD6, 0xDA)), 1));
        private static readonly Pen AxisPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xA8, 0xB0, 0xB8)), 1)
        {
            DashStyle = new DashStyle(new double[] { 4, 3 }, 0),
        });

        private static readonly Brush ToolFill = Frozen(new SolidColorBrush(Color.FromArgb(0x40, 0x2D, 0x7D, 0xD2)));
        private static readonly Pen ToolPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x2D, 0x7D, 0xD2)), 1.4));

        // The shank does not cut, so it reads as plain steel rather than as cutting edge.
        private static readonly Brush ShankFill = Frozen(new SolidColorBrush(Color.FromArgb(0x38, 0xB0, 0x88, 0x3C)));
        private static readonly Pen ShankPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xB0, 0x88, 0x3C)), 1.2));

        // Deliberately muted: the holder is usually far bigger than the cutter, and
        // drawing it as prominently would make every preview look like a holder.
        private static readonly Brush HolderFill = Frozen(new SolidColorBrush(Color.FromArgb(0x30, 0x7A, 0x86, 0x92)));
        private static readonly Pen HolderPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x8A, 0x96, 0xA2)), 1.0));

        private static readonly Typeface LabelTypeface =
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        public static readonly DependencyProperty ToolProperty = DependencyProperty.Register(
            nameof(Tool),
            typeof(Tool),
            typeof(ToolProfileView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ShowHolderProperty = DependencyProperty.Register(
            nameof(ShowHolder),
            typeof(bool),
            typeof(ToolProfileView),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>The tool to draw. Null shows a placeholder.</summary>
        public Tool Tool
        {
            get => (Tool)GetValue(ToolProperty);
            set => SetValue(ToolProperty, value);
        }

        /// <summary>
        /// Whether to include the holder above the cutter.
        /// </summary>
        /// <remarks>
        /// Worth being able to turn off. A holder is commonly 90mm tall beside a 3mm
        /// end mill, and fitting both to the panel leaves the cutter a few pixels high.
        /// </remarks>
        public bool ShowHolder
        {
            get => (bool)GetValue(ShowHolderProperty);
            set => SetValue(ShowHolderProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
            var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
            dc.DrawRectangle(BackgroundBrush, null, bounds);

            if (ActualWidth <= 0 || ActualHeight <= 0)
            {
                return;
            }

            IReadOnlyList<ProfilePoint> profile = TryGetProfile();
            if (profile == null || profile.Count < 2)
            {
                DrawGrid(dc, bounds, scale: 4.0, origin: new Point(bounds.Width / 2, bounds.Height - MarginPixels));
                DrawPlaceholder(dc, bounds);
                return;
            }

            Tool tool = Tool;

            // The shank runs from the top of the cutting body up to the holder face.
            IReadOnlyList<ProfilePoint> shank = tool?.GetShankProfile();
            if (shank != null && shank.Count < 2)
            {
                shank = null;
            }

            // The holder sits above the cutter, its lower face at the stickout height.
            IReadOnlyList<ProfilePoint> holder = null;
            double holderBase = 0;
            if (ShowHolder && tool?.Holder != null && tool.Holder.Segments.Count > 0)
            {
                holder = tool.Holder.GetProfile();
                holderBase = tool.Stickout;
            }

            double maxRadius = 0;
            double maxHeight = 0;
            foreach (ProfilePoint point in profile)
            {
                maxRadius = Math.Max(maxRadius, point.Radius);
                maxHeight = Math.Max(maxHeight, point.Height);
            }

            if (shank != null)
            {
                foreach (ProfilePoint point in shank)
                {
                    maxRadius = Math.Max(maxRadius, point.Radius);
                    maxHeight = Math.Max(maxHeight, point.Height);
                }
            }

            if (holder != null)
            {
                foreach (ProfilePoint point in holder)
                {
                    maxRadius = Math.Max(maxRadius, point.Radius);
                    maxHeight = Math.Max(maxHeight, holderBase + point.Height);
                }
            }

            // Fit the whole tool, mirrored, with a margin. Guard against a degenerate
            // tool so the scale can never become infinite.
            double usableWidth = Math.Max(bounds.Width - (2 * MarginPixels), 1);
            double usableHeight = Math.Max(bounds.Height - (2 * MarginPixels), 1);
            double widthMm = Math.Max(maxRadius * 2, 0.001);
            double heightMm = Math.Max(maxHeight, 0.001);
            double scale = Math.Min(usableWidth / widthMm, usableHeight / heightMm);

            // Origin is the tool tip on the axis: x centred, y at the bottom of the
            // drawn tool. Grid lines then fall on whole millimetres from the tip.
            double toolPixelHeight = maxHeight * scale;
            var origin = new Point(
                bounds.Width / 2,
                ((bounds.Height - toolPixelHeight) / 2) + toolPixelHeight);

            DrawGrid(dc, bounds, scale, origin);

            // Back to front, so each part draws over the one it joins.
            if (holder != null)
            {
                DrawSilhouette(dc, holder, scale, origin, holderBase, HolderFill, HolderPen);
            }

            if (shank != null)
            {
                DrawSilhouette(dc, shank, scale, origin, 0, ShankFill, ShankPen);
            }

            DrawSilhouette(dc, profile, scale, origin, 0, ToolFill, ToolPen);
        }

        /// <summary>
        /// A tool with invalid geometry must not take the window down - the browser is
        /// how you would discover such a tool in the first place.
        /// </summary>
        private IReadOnlyList<ProfilePoint> TryGetProfile()
        {
            Tool tool = Tool;
            if (tool == null)
            {
                return null;
            }

            try
            {
                return tool.GetProfile().Points;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void DrawGrid(DrawingContext dc, Rect bounds, double scale, Point origin)
        {
            dc.PushClip(new RectangleGeometry(bounds));

            if (FineGridMm * scale >= MinimumGridPixelSpacing)
            {
                DrawGridLines(dc, bounds, scale, origin, FineGridMm, FineGridPen);
            }

            DrawGridLines(dc, bounds, scale, origin, CoarseGridMm, CoarseGridPen);

            // The tool axis - the one line that is about the tool rather than the grid.
            dc.DrawLine(AxisPen, new Point(origin.X, bounds.Top), new Point(origin.X, bounds.Bottom));

            dc.Pop();
        }

        private static void DrawGridLines(
            DrawingContext dc, Rect bounds, double scale, Point origin, double stepMm, Pen pen)
        {
            double step = stepMm * scale;
            if (step <= 0.5)
            {
                return;
            }

            // Snap to whole pixels so 1px lines stay crisp rather than blurring across two.
            for (double x = origin.X; x <= bounds.Right; x += step)
            {
                dc.DrawLine(pen, new Point(Math.Round(x) + 0.5, bounds.Top), new Point(Math.Round(x) + 0.5, bounds.Bottom));
            }

            for (double x = origin.X - step; x >= bounds.Left; x -= step)
            {
                dc.DrawLine(pen, new Point(Math.Round(x) + 0.5, bounds.Top), new Point(Math.Round(x) + 0.5, bounds.Bottom));
            }

            for (double y = origin.Y; y >= bounds.Top; y -= step)
            {
                dc.DrawLine(pen, new Point(bounds.Left, Math.Round(y) + 0.5), new Point(bounds.Right, Math.Round(y) + 0.5));
            }

            for (double y = origin.Y + step; y <= bounds.Bottom; y += step)
            {
                dc.DrawLine(pen, new Point(bounds.Left, Math.Round(y) + 0.5), new Point(bounds.Right, Math.Round(y) + 0.5));
            }
        }

        /// <summary>
        /// Draws a half-profile mirrored about the axis, raised by <paramref name="baseHeight"/>.
        /// </summary>
        private static void DrawSilhouette(
            DrawingContext dc,
            IReadOnlyList<ProfilePoint> profile,
            double scale,
            Point origin,
            double baseHeight,
            Brush fill,
            Pen stroke)
        {
            var geometry = new StreamGeometry();
            using (StreamGeometryContext ctx = geometry.Open())
            {
                // Up the right-hand side...
                ctx.BeginFigure(ToScreen(profile[0], scale, origin, baseHeight, false), isFilled: true, isClosed: true);
                for (int i = 1; i < profile.Count; i++)
                {
                    ctx.LineTo(ToScreen(profile[i], scale, origin, baseHeight, false), isStroked: true, isSmoothJoin: false);
                }

                // ...across the top and back down the mirrored left-hand side.
                for (int i = profile.Count - 1; i >= 0; i--)
                {
                    ctx.LineTo(ToScreen(profile[i], scale, origin, baseHeight, true), isStroked: true, isSmoothJoin: false);
                }
            }

            geometry.Freeze();
            dc.DrawGeometry(fill, stroke, geometry);
        }

        private static Point ToScreen(
            ProfilePoint point, double scale, Point origin, double baseHeight, bool mirrored)
        {
            double radius = mirrored ? -point.Radius : point.Radius;

            // Screen y grows downward; tool height grows upward from the tip.
            return new Point(
                origin.X + (radius * scale),
                origin.Y - ((baseHeight + point.Height) * scale));
        }

        private void DrawPlaceholder(DrawingContext dc, Rect bounds)
        {
            var text = new FormattedText(
                Tool == null ? "No tool selected" : "Profile unavailable",
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                11,
                new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0x90)));

            dc.DrawText(text, new Point(
                (bounds.Width - text.Width) / 2,
                (bounds.Height - text.Height) / 2));
        }

        private static T Frozen<T>(T freezable)
            where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }
    }
}

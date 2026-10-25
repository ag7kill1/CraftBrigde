using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CraftBridge
{
    internal static class Ui
    {
        public static readonly Color Background = Color.FromArgb(247, 248, 250);
        public static readonly Color Ink = Color.FromArgb(29, 29, 31);
        public static readonly Color Muted = Color.FromArgb(119, 122, 130);
        public static readonly Color Accent = Color.FromArgb(0, 113, 227);
        public static readonly Color Border = Color.FromArgb(229, 231, 235);
        public static readonly Color Soft = Color.FromArgb(245, 246, 248);
        public static readonly Color Sidebar = Color.FromArgb(240, 241, 244);
        public static readonly Color AccentSoft = Color.FromArgb(226, 237, 252);

        public static Label Label(string text, float size, bool bold)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = Ink,
                Font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
                Margin = new Padding(0, 0, 0, 4), UseMnemonic = false };
        }
        public static Button Button(string text, bool primary)
        {
            return new RoundedButton { Text = text, Primary = primary, Height = 38, Width = 112,
                Font = new Font("Microsoft YaHei UI", 9F), Margin = new Padding(6, 0, 0, 0) };
        }
        public static TextBox TextBox()
        {
            return new TextBox { Dock = DockStyle.Fill, Font = new Font("Microsoft YaHei UI", 9F),
                BorderStyle = BorderStyle.None, BackColor = Color.White, ForeColor = Ink, Margin = Padding.Empty };
        }
        public static ComboBox Combo()
        {
            return new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Microsoft YaHei UI", 9F), FlatStyle = FlatStyle.Flat,
                BackColor = Color.White, ForeColor = Ink, Margin = Padding.Empty };
        }
        public static Control Input(Control input)
        {
            InputFrame frame = new InputFrame(input); frame.Dock = DockStyle.Fill;
            frame.Margin = new Padding(0, 0, 0, 6); return frame;
        }
        public static TableLayoutPanel Table(int columns)
        {
            TableLayoutPanel table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = columns,
                Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Color.Transparent };
            if (columns == 1) table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            else { table.RowCount = 1; table.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); }
            return table;
        }
        public static void Row(TableLayoutPanel table, Control control, SizeType type, float height)
        {
            int row = table.RowCount++; table.RowStyles.Add(new RowStyle(type, height)); table.Controls.Add(control, 0, row);
        }
        public static TextBox Notice()
        {
            TextBox box = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                BorderStyle = BorderStyle.None, BackColor = Soft, ForeColor = Muted,
                Font = new Font("Microsoft YaHei UI", 8.5F), ScrollBars = ScrollBars.None,
                Margin = new Padding(0, 10, 0, 0), TabStop = false };
            box.TextChanged += delegate { box.ScrollBars = box.Lines.Length > 2 || box.TextLength > 140 ? ScrollBars.Vertical : ScrollBars.None; };
            return box;
        }
        public static string SafeDisplay(string text) { return String.IsNullOrEmpty(text) ? "待确认" : text; }
        public static GraphicsPath Round(RectangleF bounds, float radius)
        {
            GraphicsPath path = new GraphicsPath(); float d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            if (d <= 0) return path;
            path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90); path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90); path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path;
        }
        public static Color ParentColor(Control control)
        {
            Control parent = control.Parent;
            while (parent != null && parent.BackColor.A == 0) parent = parent.Parent;
            return parent == null ? Background : parent.BackColor;
        }
        public static void NavSelected(Button button, bool selected)
        {
            RoundedButton rounded = button as RoundedButton;
            if (rounded != null) { rounded.Selected = selected; rounded.Invalidate(); }
        }
    }

    internal enum Glyph { None, Export, Import, Help, Shield }

    internal sealed class RoundedButton : Button
    {
        public bool Primary, Navigation, Selected;
        public Glyph Glyph;
        bool hover, pressed;
        public RoundedButton()
        {
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; UseVisualStyleBackColor = false;
            Cursor = Cursors.Hand; DoubleBuffered = true; BackColor = Color.White;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); UpdateRoundRegion(); }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); UpdateRoundRegion(); }
        void UpdateRoundRegion()
        {
            if (Width <= 0 || Height <= 0) return;
            using (GraphicsPath path = Ui.Round(new RectangleF(0, 0, Width, Height), 9 * DeviceDpi / 96F))
            {
                Region previous = Region; Region = new Region(path); if (previous != null) previous.Dispose();
            }
        }
        protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(Ui.ParentColor(this)); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; float scale = DeviceDpi / 96F;
            Color fill = Primary ? Ui.Accent : Color.White, ink = Primary ? Color.White : Ui.Ink;
            if (Navigation) { fill = Selected ? Ui.AccentSoft : Ui.Sidebar; ink = Selected ? Ui.Accent : Ui.Muted; }
            if (!Enabled) { fill = Primary ? Color.FromArgb(227, 230, 235) : Ui.Soft; ink = Color.FromArgb(166, 170, 179); }
            else if (pressed) fill = Primary ? Color.FromArgb(0, 87, 186) : Color.FromArgb(223, 231, 244);
            else if (hover) fill = Primary ? Color.FromArgb(0, 103, 216) : Navigation ? Color.FromArgb(230, 233, 239) : Ui.Soft;
            RectangleF bounds = new RectangleF(0.5F, 0.5F, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            using (GraphicsPath path = Ui.Round(bounds, 9 * scale))
            using (Brush brush = new SolidBrush(fill))
            { g.FillPath(brush, path); if (!Primary && !Navigation) using (Pen pen = new Pen(Ui.Border)) g.DrawPath(pen, path); }
            Rectangle text = ClientRectangle;
            if (Glyph != Glyph.None) { DrawGlyph(g, Glyph, new RectangleF(14 * scale, (Height - 18 * scale) / 2, 18 * scale, 18 * scale), ink); text.X += (int)(43 * scale); text.Width -= (int)(48 * scale); }
            TextRenderer.DrawText(g, Text, Font, text, ink, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | (Navigation ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter));
            if (Focused && ShowFocusCues) using (Pen focus = new Pen(Color.FromArgb(120, 0, 113, 227), 2))
                using (GraphicsPath path = Ui.Round(new RectangleF(3, 3, Math.Max(1, Width - 6), Math.Max(1, Height - 6)), 7 * scale)) g.DrawPath(focus, path);
        }
        public static void DrawGlyph(Graphics g, Glyph glyph, RectangleF rect, Color color)
        {
            GraphicsState state = g.Save(); g.TranslateTransform(rect.X, rect.Y); g.ScaleTransform(rect.Width / 20, rect.Height / 20);
            using (Pen pen = new Pen(color, 1.6F))
            {
                pen.StartCap = pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
                if (glyph == Glyph.Help) { g.DrawEllipse(pen, 2, 2, 16, 16); g.DrawArc(pen, 7, 5, 6, 6, 190, 210); g.DrawLine(pen, 10, 11, 10, 12); g.DrawLine(pen, 10, 15, 10, 15.2F); }
                else if (glyph == Glyph.Shield) { g.DrawLines(pen, new[] { new PointF(10, 2), new PointF(17, 5), new PointF(16, 13), new PointF(10, 18), new PointF(4, 13), new PointF(3, 5), new PointF(10, 2) }); g.DrawLines(pen, new[] { new PointF(6, 10), new PointF(9, 13), new PointF(14, 7) }); }
                else { g.DrawLines(pen, new[] { new PointF(4, 11), new PointF(4, 17), new PointF(16, 17), new PointF(16, 11) }); g.DrawLine(pen, 10, 2, 10, 12); if (glyph == Glyph.Export) g.DrawLines(pen, new[] { new PointF(6, 6), new PointF(10, 2), new PointF(14, 6) }); else g.DrawLines(pen, new[] { new PointF(6, 8), new PointF(10, 12), new PointF(14, 8) }); }
            }
            g.Restore(state);
        }
    }

    internal sealed class InputFrame : Panel
    {
        public InputFrame(Control input)
        {
            DoubleBuffered = true; BackColor = Color.White; Padding = new Padding(10, input is ComboBox ? 4 : 7, 8, 4);
            input.Dock = DockStyle.Fill; Controls.Add(input); input.Enter += delegate { Invalidate(); }; input.Leave += delegate { Invalidate(); };
            SetStyle(ControlStyles.ResizeRedraw, true);
        }
        public override Size GetPreferredSize(Size proposedSize)
        { return new Size((int)(160 * DeviceDpi / 96F), (int)(36 * DeviceDpi / 96F)); }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Ui.ParentColor(this)); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Ui.Round(new RectangleF(.5F, .5F, Math.Max(1, Width - 1), Math.Max(1, Height - 1)), 8 * DeviceDpi / 96F))
            using (Brush fill = new SolidBrush(Enabled ? Color.White : Ui.Soft))
            using (Pen border = new Pen(ContainsFocus ? Ui.Accent : Ui.Border))
            { e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path); }
        }
    }

    internal sealed class BridgeMark : Control
    {
        public BridgeMark() { DoubleBuffered = true; Size = new Size(48, 48); AccessibleName = "CraftBridge"; }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.ScaleTransform(Width / 52F, Height / 52F);
            using (GraphicsPath path = Ui.Round(new RectangleF(1, 1, 50, 50), 13))
            using (LinearGradientBrush fill = new LinearGradientBrush(new Point(0, 0), new Point(52, 52), Color.FromArgb(65, 158, 255), Color.FromArgb(0, 97, 216))) g.FillPath(fill, path);
            using (Pen pen = new Pen(Color.White, 3))
            { pen.StartCap = pen.EndCap = LineCap.Round; g.DrawLine(pen, 11, 34, 41, 34); g.DrawLine(pen, 15, 36, 15, 19); g.DrawLine(pen, 37, 36, 37, 19); g.DrawBezier(pen, 15, 20, 19, 32, 33, 32, 37, 20); g.DrawLine(pen, 22, 28, 22, 34); g.DrawLine(pen, 30, 28, 30, 34); }
        }
    }

    internal sealed class Card : Panel
    {
        public Card() { BackColor = Color.White; Padding = new Padding(22, 18, 22, 18); DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true); }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Ui.ParentColor(this)); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Ui.Round(new RectangleF(.5F, .5F, Math.Max(1, Width - 1), Math.Max(1, Height - 1)), 16 * DeviceDpi / 96F))
            using (Brush white = new SolidBrush(Color.White)) using (Pen border = new Pen(Ui.Border))
            { e.Graphics.FillPath(white, path); e.Graphics.DrawPath(border, path); }
        }
    }

    internal sealed class EmptyState : Control
    {
        public string Heading = "选择一个游戏实例", Detail = "扫描后，世界、模组与配置会显示在这里。";
        public EmptyState() { DoubleBuffered = true; BackColor = Color.White; Dock = DockStyle.Fill; AccessibleRole = AccessibleRole.StaticText; }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; float scale = DeviceDpi / 96F;
            float x = Width / 2F, y = Math.Max(10 * scale, Height / 2F - 56 * scale);
            using (GraphicsPath path = Ui.Round(new RectangleF(x - 27 * scale, y, 54 * scale, 54 * scale), 16 * scale))
            using (Brush fill = new SolidBrush(Ui.Soft)) g.FillPath(fill, path);
            RoundedButton.DrawGlyph(g, Glyph.Import, new RectangleF(x - 13 * scale, y + 14 * scale, 26 * scale, 26 * scale), Color.FromArgb(157, 165, 177));
            using (Font title = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold))
            using (Font detail = new Font("Microsoft YaHei UI", 8.7F))
            { TextRenderer.DrawText(g, Heading, title, new Rectangle(8, (int)(y + 66 * scale), Width - 16, (int)(26 * scale)), Ui.Ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
              TextRenderer.DrawText(g, Detail, detail, new Rectangle(8, (int)(y + 94 * scale), Width - 16, (int)(25 * scale)), Ui.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis); }
        }
    }
}

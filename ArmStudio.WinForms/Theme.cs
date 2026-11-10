using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace ArmStudio.WinForms
{
    /// <summary>Shared presentation tokens for the operator console.</summary>
    public static class Theme
    {
        public static readonly Color Background = Color.FromArgb(16, 21, 29);
        public static readonly Color Surface = Color.FromArgb(23, 30, 41);
        public static readonly Color Raised = Color.FromArgb(32, 42, 55);
        public static readonly Color Border = Color.FromArgb(44, 56, 72);
        public static readonly Color Text = Color.FromArgb(237, 243, 251);
        public static readonly Color Muted = Color.FromArgb(142, 159, 180);
        public static readonly Color Accent = Color.FromArgb(102, 228, 193);
        public static readonly Color Danger = Color.FromArgb(239, 101, 101);
        public static readonly Color Blue = Color.FromArgb(130, 180, 250);
        public static readonly Color Gold = Color.FromArgb(239, 199, 123);
        private static readonly FontFamily UiFamily = FindFamily("Segoe UI", "DejaVu Sans", FontFamily.GenericSansSerif);
        private static readonly FontFamily MonoFamily = FindFamily("Consolas", "DejaVu Sans Mono", FontFamily.GenericMonospace);

        public static Font Ui(float size = 10, FontStyle style = FontStyle.Regular)
        {
            return new Font(UiFamily, size, style, GraphicsUnit.Point);
        }

        public static Font Mono(float size = 11, FontStyle style = FontStyle.Regular)
        {
            return new Font(MonoFamily, size, style, GraphicsUnit.Point);
        }

        private static FontFamily FindFamily(string preferred, string fallback, FontFamily generic)
        {
            FontFamily alternative = null;
            foreach (FontFamily family in FontFamily.Families)
            {
                if (String.Equals(family.Name, preferred, StringComparison.OrdinalIgnoreCase)) return family;
                if (String.Equals(family.Name, fallback, StringComparison.OrdinalIgnoreCase)) alternative = family;
            }
            return alternative ?? generic;
        }

        public static Button Button(string text, bool primary = false, bool danger = false)
        {
            Color background = danger ? Color.FromArgb(76, 38, 46) : primary ? Accent : Raised;
            Color foreground = danger ? Danger : primary ? Background : Text;
            Button button = new Button
            {
                Text = text,
                AutoSize = false,
                Height = 36,
                Width = 118,
                BackColor = background,
                ForeColor = foreground,
                Font = Ui(9, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false,
                Margin = new Padding(0, 0, 8, 0),
                Padding = new Padding(8, 0, 8, 0),
                AccessibleName = text,
                TabStop = true
            };
            button.FlatAppearance.BorderColor = danger ? Color.FromArgb(131, 63, 70) : primary ? Accent : Border;
            button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.FlatAppearance.MouseOverBackColor = danger ? Color.FromArgb(96, 44, 51) : primary ? Color.FromArgb(144, 240, 214) : Color.FromArgb(44, 57, 74);
            button.FlatAppearance.MouseDownBackColor = danger ? Color.FromArgb(112, 47, 53) : primary ? Color.FromArgb(72, 192, 160) : Color.FromArgb(52, 68, 86);
            return button;
        }

        public static Label Label(string text, float size = 10, Color? color = null, FontStyle style = FontStyle.Regular)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = color ?? Text,
                BackColor = Color.Transparent,
                Font = Ui(size, style),
                Margin = new Padding(0),
                UseMnemonic = false
            };
        }

        public static void StyleGrid(DataGridView grid)
        {
            grid.BackgroundColor = Surface;
            grid.BorderStyle = BorderStyle.None;
            grid.GridColor = Border;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.RowHeadersVisible = false;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToResizeRows = false;
            grid.AllowUserToOrderColumns = false;
            grid.MultiSelect = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.ColumnHeadersHeight = 34;
            grid.RowTemplate.Height = 31;
            grid.DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Surface,
                ForeColor = Text,
                SelectionBackColor = Color.FromArgb(38, 65, 70),
                SelectionForeColor = Accent,
                Font = Ui(9),
                Padding = new Padding(6, 0, 4, 0)
            };
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(26, 34, 46);
            grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Raised,
                ForeColor = Muted,
                SelectionBackColor = Raised,
                SelectionForeColor = Text,
                Font = Ui(8, FontStyle.Bold),
                Padding = new Padding(6, 0, 4, 0),
                Alignment = DataGridViewContentAlignment.MiddleLeft
            };
        }
    }

    public class CardPanel : Panel
    {
        public CardPanel()
        {
            DoubleBuffered = true;
            BackColor = Theme.Surface;
            Padding = new Padding(16);
            Margin = new Padding(0);
            ResizeRedraw = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Width < 2 || Height < 2) return;
            using (Pen pen = new Pen(Theme.Border))
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }

    public class StatusPill : Label
    {
        public StatusPill()
        {
            AutoSize = true;
            Font = Theme.Ui(8, FontStyle.Bold);
            ForeColor = Theme.Accent;
            BackColor = Color.FromArgb(30, 61, 58);
            Padding = new Padding(10, 7, 10, 7);
            Margin = new Padding(0);
            TextAlign = ContentAlignment.MiddleCenter;
            UseMnemonic = false;
        }
    }

    /// <summary>Native tab navigation with a fully painted dark header strip.</summary>
    public class DarkTabs : TabControl
    {
        public DarkTabs()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            DrawMode = TabDrawMode.OwnerDrawFixed;
            Font = Theme.Ui(9);
            Padding = new Point(14, 7);
            ItemSize = new Size(150, 32);
            SizeMode = TabSizeMode.Normal;
            HotTrack = false;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Surface);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Surface);
            using (SolidBrush selected = new SolidBrush(Theme.Raised))
            using (SolidBrush background = new SolidBrush(Theme.Surface))
            using (Pen border = new Pen(Theme.Border))
            using (Pen indicator = new Pen(Theme.Accent, 2))
            {
                Rectangle page = DisplayRectangle;
                if (page.Width > 0 && page.Height > 0)
                    e.Graphics.DrawRectangle(border, page.X - 1, page.Y - 1, page.Width + 1, page.Height + 1);
                for (int index = 0; index < TabCount; index++)
                {
                    Rectangle bounds = GetTabRect(index);
                    bool active = index == SelectedIndex;
                    e.Graphics.FillRectangle(active ? selected : background, bounds);
                    TextRenderer.DrawText(e.Graphics, TabPages[index].Text, Font, bounds,
                        active ? Theme.Accent : Theme.Muted,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                    if (active) e.Graphics.DrawLine(indicator, bounds.Left + 8, bounds.Bottom - 2, bounds.Right - 8, bounds.Bottom - 2);
                    if (active && Focused && ShowFocusCues)
                    {
                        Rectangle focus = bounds;
                        focus.Inflate(-4, -4);
                        ControlPaint.DrawFocusRectangle(e.Graphics, focus, Theme.Accent, Theme.Raised);
                    }
                }
            }
        }

        protected override void OnSelectedIndexChanged(EventArgs e)
        {
            base.OnSelectedIndexChanged(e);
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            for (int index = 0; index < TabCount; index++)
            {
                if (!GetTabRect(index).Contains(e.Location)) continue;
                SelectedIndex = index;
                Focus();
                break;
            }
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }
    }

    /// <summary>
    /// A native rich-text program editor with a line gutter. ActiveLine is one-based;
    /// zero clears the execution marker. Formatting never changes Source.
    /// </summary>
    public class CodeEditor : UserControl
    {
        private readonly RichTextBox editor;
        private readonly GutterPanel gutter;
        private readonly Timer syntaxTimer;
        private bool formatting;
        private int activeLine;
        private static readonly Color EditorBackground = Color.FromArgb(16, 23, 34);

        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wordParameter, ref Point pointParameter);

        public RichTextBox Editor { get { return editor; } }

        public string Source
        {
            get { return editor.Text; }
            set
            {
                editor.Text = value ?? String.Empty;
                HighlightSyntax();
            }
        }

        public int ActiveLine
        {
            get { return activeLine; }
            set
            {
                activeLine = Math.Max(0, value);
                gutter.Invalidate();
            }
        }

        public event EventHandler SourceChanged;

        public CodeEditor()
        {
            DoubleBuffered = true;
            BackColor = EditorBackground;
            Padding = new Padding(0, 9, 10, 9);
            MinimumSize = new Size(160, 100);
            editor = new RichTextBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = EditorBackground,
                ForeColor = Theme.Text,
                Font = Theme.Mono(11),
                AcceptsTab = true,
                WordWrap = false,
                DetectUrls = false,
                HideSelection = false,
                ScrollBars = RichTextBoxScrollBars.Both,
                EnableAutoDragDrop = false,
                AccessibleName = "Editor de programa",
                AccessibleDescription = "Instrucciones MoveL, MoveC y WaitTime. Números de línea a la izquierda."
            };
            gutter = new GutterPanel(this)
            {
                Dock = DockStyle.Left,
                Width = 51,
                BackColor = EditorBackground
            };
            Controls.Add(editor);
            Controls.Add(gutter);
            syntaxTimer = new Timer { Interval = 180 };
            syntaxTimer.Tick += delegate
            {
                syntaxTimer.Stop();
                HighlightSyntax();
            };
            editor.TextChanged += delegate
            {
                if (formatting) return;
                syntaxTimer.Stop();
                syntaxTimer.Start();
                gutter.Invalidate();
                EventHandler handler = SourceChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            };
            editor.VScroll += delegate { gutter.Invalidate(); };
            editor.Resize += delegate { gutter.Invalidate(); };
            editor.SelectionChanged += delegate { if (!formatting) gutter.Invalidate(); };
            editor.MouseWheel += delegate { gutter.Invalidate(); };
            editor.FontChanged += delegate { gutter.Invalidate(); };
        }

        public void HighlightSyntax()
        {
            if (formatting || editor.IsDisposed) return;
            formatting = true;
            int selectionStart = editor.SelectionStart;
            int selectionLength = editor.SelectionLength;
            bool wasModified = editor.Modified;
            bool windowsScroll = Environment.OSVersion.Platform == PlatformID.Win32NT && editor.IsHandleCreated;
            Point scroll = Point.Empty;
            int firstVisible = editor.IsHandleCreated ? editor.GetCharIndexFromPosition(new Point(1, 1)) : 0;
            if (windowsScroll) SendMessage(editor.Handle, 0x04DD, IntPtr.Zero, ref scroll); // EM_GETSCROLLPOS
            try
            {
                editor.SuspendLayout();
                editor.SelectAll();
                editor.SelectionColor = Theme.Text;
                string text = editor.Text;
                PaintMatches(text, @"\b[pP][A-Za-z0-9_]*\b", Theme.Blue, RegexOptions.None);
                PaintMatches(text, @"\b(?:v\d+(?:\.\d+)?|z0|fine|tool0)\b", Theme.Gold, RegexOptions.IgnoreCase);
                PaintMatches(text, @"(?<![A-Za-z0-9_])\d+(?:\.\d+)?\b", Theme.Gold, RegexOptions.None);
                PaintMatches(text, @"\b(?:MoveL|MoveC|WaitTime|MODULE|ENDMODULE|PROC|ENDPROC)\b", Theme.Accent, RegexOptions.IgnoreCase);
                PaintMatches(text, @"!.*$", Theme.Muted, RegexOptions.Multiline);
                if (!windowsScroll && editor.IsHandleCreated)
                {
                    // The public fallback keeps the editor usable on Mono without user32.
                    editor.Select(Math.Min(firstVisible, text.Length), 0);
                    editor.ScrollToCaret();
                }
                editor.Select(Math.Min(selectionStart, text.Length), Math.Min(selectionLength, Math.Max(0, text.Length - selectionStart)));
                if (windowsScroll) SendMessage(editor.Handle, 0x04DE, IntPtr.Zero, ref scroll); // EM_SETSCROLLPOS
                editor.Modified = wasModified;
            }
            finally
            {
                editor.ResumeLayout();
                formatting = false;
                gutter.Invalidate();
            }
        }

        private void PaintMatches(string text, string pattern, Color color, RegexOptions options)
        {
            foreach (Match match in Regex.Matches(text, pattern, options))
            {
                editor.Select(match.Index, match.Length);
                editor.SelectionColor = color;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) syntaxTimer.Dispose();
            base.Dispose(disposing);
        }

        private sealed class GutterPanel : Panel
        {
            private readonly CodeEditor owner;

            public GutterPanel(CodeEditor owner)
            {
                this.owner = owner;
                DoubleBuffered = true;
                ResizeRedraw = true;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                RichTextBox target = owner.editor;
                if (target.IsDisposed || !target.IsHandleCreated) return;
                int firstVisibleCharacter = target.GetCharIndexFromPosition(new Point(1, 1));
                int firstLine = Math.Max(0, target.GetLineFromCharIndex(firstVisibleCharacter) - 1);
                int lineCount = Math.Max(1, target.Lines.Length);
                int lineHeight = Math.Max(14, target.Font.Height);
                using (Font font = new Font(target.Font.FontFamily, 8.5f))
                using (SolidBrush normal = new SolidBrush(Theme.Muted))
                using (SolidBrush active = new SolidBrush(Theme.Accent))
                using (Pen separator = new Pen(Color.FromArgb(34, 45, 60)))
                using (StringFormat format = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center })
                {
                    e.Graphics.DrawLine(separator, Width - 11, 0, Width - 11, Height);
                    for (int line = firstLine; line < lineCount; line++)
                    {
                        int character = target.GetFirstCharIndexFromLine(line);
                        if (character < 0) character = target.TextLength;
                        Point position = target.GetPositionFromCharIndex(character);
                        if (position.Y > Height) break;
                        if (position.Y + lineHeight < 0) continue;
                        RectangleF bounds = new RectangleF(0, position.Y, Width - 20, lineHeight);
                        bool isActive = owner.activeLine == line + 1;
                        e.Graphics.DrawString((line + 1).ToString(), font, isActive ? active : normal, bounds, format);
                        if (isActive)
                        {
                            float center = position.Y + lineHeight / 2f;
                            e.Graphics.FillPolygon(active, new[]
                            {
                                new PointF(Width - 7, center - 4),
                                new PointF(Width - 7, center + 4),
                                new PointF(Width - 2, center)
                            });
                        }
                    }
                }
            }
        }
    }
}

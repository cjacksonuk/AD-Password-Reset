using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ADUserManager
{
    static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(242, 244, 247);
        public static readonly Color Card = Color.White;
        public static readonly Color Border = Color.FromArgb(220, 224, 230);
        public static readonly Color Accent = Color.FromArgb(0, 103, 192);
        public static readonly Color AccentDark = Color.FromArgb(0, 84, 158);
        public static readonly Color AccentLight = Color.FromArgb(230, 241, 251);
        public static readonly Color Header = Color.FromArgb(19, 41, 75);
        public static readonly Color Header2 = Color.FromArgb(28, 72, 128);
        public static readonly Color Text = Color.FromArgb(30, 36, 44);
        public static readonly Color Muted = Color.FromArgb(98, 108, 122);
        public static readonly Color Success = Color.FromArgb(15, 123, 64);
        public static readonly Color SuccessLight = Color.FromArgb(222, 243, 230);
        public static readonly Color Danger = Color.FromArgb(190, 38, 30);
        public static readonly Color DangerLight = Color.FromArgb(252, 228, 226);
        public static readonly Color Warn = Color.FromArgb(150, 90, 0);
        public static readonly Color WarnLight = Color.FromArgb(255, 241, 214);
        public static readonly Color Neutral = Color.FromArgb(233, 236, 240);

        public static readonly Font Base = new Font("Segoe UI", 9.75f);
        public static readonly Font Bold = new Font("Segoe UI Semibold", 9.75f);
        public static readonly Font Small = new Font("Segoe UI", 8.75f);
        public static readonly Font H1 = new Font("Segoe UI Semibold", 15f);
        public static readonly Font H2 = new Font("Segoe UI Semibold", 11.25f);
        public static readonly Font Big = new Font("Segoe UI Semibold", 13.5f);
        public static readonly Font Mono = new Font("Consolas", 12f, FontStyle.Bold);
        public static readonly Font MonoSmall = new Font("Consolas", 9.75f);

        public static float Scale = 1f;
        public static int S(int px) { return (int)Math.Round(px * Scale); }

        public static GraphicsPath Round(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = radius * 2;
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        public static void Cue(TextBox tb, string text)
        {
            if (tb.IsHandleCreated) SendMessage(tb.Handle, 0x1501, (IntPtr)1, text);
            else tb.HandleCreated += delegate { SendMessage(tb.Handle, 0x1501, (IntPtr)1, text); };
        }

        public static Label Label(string text, Font font, Color color)
        {
            return new Label { UseMnemonic = false, Text = text, Font = font, ForeColor = color, AutoSize = true, Margin = new Padding(0, S(3), 0, S(3)) };
        }

        public static TextBox Input()
        {
            return new TextBox { Font = Base, Dock = DockStyle.Fill, Margin = new Padding(0, S(3), 0, S(3)), BorderStyle = BorderStyle.FixedSingle };
        }
    }

    /// <summary>White rounded panel with optional title.</summary>
    class Card : Panel
    {
        string _title;
        public string Title { get { return _title; } set { _title = value; UpdatePadding(); Invalidate(); } }

        public Card(string title)
        {
            DoubleBuffered = true;
            BackColor = Theme.Card;
            Margin = new Padding(Theme.S(8));
            Dock = DockStyle.Fill;
            _title = title;
            UpdatePadding();
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        void UpdatePadding()
        {
            Padding = new Padding(Theme.S(18), string.IsNullOrEmpty(_title) ? Theme.S(16) : Theme.S(46), Theme.S(18), Theme.S(14));
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Theme.Round(r, Theme.S(8)))
            using (var b = new SolidBrush(BackColor))
            using (var pen = new Pen(Theme.Border))
            {
                e.Graphics.FillPath(b, p);
                e.Graphics.DrawPath(pen, p);
            }
            if (!string.IsNullOrEmpty(_title))
            {
                using (var accent = new SolidBrush(Theme.Accent))
                    e.Graphics.FillRectangle(accent, Theme.S(18), Theme.S(17), Theme.S(3), Theme.S(18));
                TextRenderer.DrawText(e.Graphics, _title, Theme.H2, new Point(Theme.S(26), Theme.S(15)), Theme.Text, TextFormatFlags.NoPrefix);
            }
        }
    }

    class FlatBtn : Button
    {
        readonly bool _primary;
        public FlatBtn(string text, bool primary)
        {
            _primary = primary;
            Text = text;
            FlatStyle = FlatStyle.Flat;
            Font = Theme.Bold;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(Theme.S(12), Theme.S(4), Theme.S(12), Theme.S(4));
            Margin = new Padding(0, Theme.S(3), Theme.S(8), Theme.S(3));
            MinimumSize = new Size(Theme.S(70), Theme.S(32));
            Cursor = Cursors.Hand;
            UseVisualStyleBackColor = false;
            ApplyColors();
        }

        void ApplyColors()
        {
            if (_primary)
            {
                BackColor = Enabled ? Theme.Accent : Color.FromArgb(160, 185, 210);
                ForeColor = Color.White;
                FlatAppearance.BorderSize = 0;
                FlatAppearance.MouseOverBackColor = Theme.AccentDark;
                FlatAppearance.MouseDownBackColor = Color.FromArgb(0, 66, 125);
            }
            else
            {
                BackColor = Color.White;
                ForeColor = Enabled ? Theme.Text : Theme.Muted;
                FlatAppearance.BorderSize = 1;
                FlatAppearance.BorderColor = Theme.Border;
                FlatAppearance.MouseOverBackColor = Theme.AccentLight;
                FlatAppearance.MouseDownBackColor = Color.FromArgb(210, 228, 245);
            }
        }

        protected override void OnEnabledChanged(EventArgs e) { ApplyColors(); base.OnEnabledChanged(e); }
        protected override bool ShowFocusCues { get { return false; } }
    }

    /// <summary>Navigation tab with an accent underline when selected.</summary>
    class NavTab : Control
    {
        bool _selected, _hover;
        public string Glyph = "";
        public bool Selected { get { return _selected; } set { _selected = value; Invalidate(); } }

        public NavTab(string text, string glyph)
        {
            Text = text;
            Glyph = glyph;
            DoubleBuffered = true;
            Cursor = Cursors.Hand;
            Font = Theme.Bold;
            BackColor = Color.White;
            Height = Theme.S(44);
            Margin = new Padding(0);
            using (Graphics g = CreateGraphics())
                Width = TextRenderer.MeasureText(g, glyph + "   " + text, Font, Size.Empty, TextFormatFlags.NoPrefix).Width + Theme.S(36);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(_hover && !_selected ? Color.FromArgb(246, 248, 251) : BackColor);
            Color fg = _selected ? Theme.Accent : (_hover ? Theme.Text : Theme.Muted);
            var glyphFont = new Font("Segoe MDL2 Assets", 11f);
            int x = Theme.S(18);
            Size gs = TextRenderer.MeasureText(Glyph, glyphFont);
            TextRenderer.DrawText(e.Graphics, Glyph, glyphFont, new Rectangle(x, 0, gs.Width, Height), fg,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(x + gs.Width + Theme.S(6), 0, Width, Height), fg,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
            glyphFont.Dispose();
            if (_selected)
                using (var b = new SolidBrush(Theme.Accent))
                    e.Graphics.FillRectangle(b, Theme.S(12), Height - Theme.S(3), Width - Theme.S(24), Theme.S(3));
        }
    }

    /// <summary>Small coloured pill e.g. "Enabled", "Locked".</summary>
    class Chip : Control
    {
        public Color Fill = Theme.Neutral;
        public Chip(string text, Color fore, Color fill)
        {
            Text = text; ForeColor = fore; Fill = fill;
            Font = Theme.Bold;
            DoubleBuffered = true;
            Margin = new Padding(0, Theme.S(4), Theme.S(6), Theme.S(4));
            Size sz = TextRenderer.MeasureText(text, Font);
            Size = new Size(sz.Width + Theme.S(18), sz.Height + Theme.S(6));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent != null ? Parent.BackColor : Color.White);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = Theme.Round(new Rectangle(0, 0, Width - 1, Height - 1), (Height - 1) / 2))
            using (var b = new SolidBrush(Fill))
                e.Graphics.FillPath(b, p);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    /// <summary>Dark gradient banner with title and signed-in user.</summary>
    class HeaderBanner : Control
    {
        public string Title = "AD User Manager";
        public string Subtitle = "Create users  ·  Reset passwords";
        public string UserName = "Connecting...";
        public string UserDetail = "";
        public string Initials = "";

        public HeaderBanner()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var lg = new LinearGradientBrush(ClientRectangle, Theme.Header, Theme.Header2, 0f))
                g.FillRectangle(lg, ClientRectangle);

            // App mark
            int m = Theme.S(40), top = (Height - m) / 2, left = Theme.S(20);
            using (GraphicsPath p = Theme.Round(new Rectangle(left, top, m, m), Theme.S(9)))
            using (var b = new SolidBrush(Color.FromArgb(40, 255, 255, 255)))
                g.FillPath(b, p);
            using (var f = new Font("Segoe MDL2 Assets", 16f))
                TextRenderer.DrawText(g, "", f, new Rectangle(left, top, m, m), Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            int tx = left + m + Theme.S(14);
            TextRenderer.DrawText(g, Title, Theme.H1, new Point(tx, Height / 2 - Theme.S(24)), Color.White);
            TextRenderer.DrawText(g, Subtitle, Theme.Base, new Point(tx + Theme.S(2), Height / 2 + Theme.S(3)),
                Color.FromArgb(190, 210, 235));

            // Signed-in user (right)
            int av = Theme.S(38);
            int ax = Width - av - Theme.S(20), ay = (Height - av) / 2;
            using (var b = new SolidBrush(Color.FromArgb(255, 255, 255)))
                g.FillEllipse(b, ax, ay, av, av);
            TextRenderer.DrawText(g, Initials, Theme.Bold, new Rectangle(ax, ay, av, av), Theme.Header,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            Size n1 = TextRenderer.MeasureText(UserName, Theme.Bold);
            Size n2 = TextRenderer.MeasureText(UserDetail, Theme.Small);
            int rx = ax - Theme.S(12);
            TextRenderer.DrawText(g, UserName, Theme.Bold, new Point(rx - n1.Width, Height / 2 - n1.Height), Color.White);
            TextRenderer.DrawText(g, UserDetail, Theme.Small, new Point(rx - n2.Width, Height / 2 + Theme.S(1)),
                Color.FromArgb(190, 210, 235));
        }
    }

    /// <summary>
    /// Default / generated / typed password picker shared by the Reset and Create pages.
    /// </summary>
    class PasswordChooser : UserControl
    {
        public readonly RadioButton RbDefault, RbGenerate, RbCustom;
        public readonly TextBox TxtGenerated, TxtPwd, TxtConfirm;
        public readonly CheckBox ChkShow, ChkMustChange;
        readonly Label _lblDefault, _lblValidation, _lblDefaultNote;
        readonly FlatBtn _btnRegen, _btnCopy;
        readonly Panel _customPanel, _genPanel;

        public string DefaultPassword = "";
        public string Style = "Memorable";
        public int Length = 12;
        public Func<string> Validator;   // returns null when OK
        public event EventHandler Changed;

        bool _mustChangeBeforeDefault = true;

        public PasswordChooser()
        {
            BackColor = Theme.Card;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Dock = DockStyle.Top;
            Margin = new Padding(0);

            var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Margin = new Padding(0) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            RbDefault = Radio("Use the default password");
            _lblDefault = new Label { Font = Theme.Mono, ForeColor = Theme.Accent, AutoSize = true, Margin = new Padding(Theme.S(22), 0, Theme.S(8), 0) };
            _lblDefaultNote = Theme.Label("Always requires a password change at next logon", Theme.Small, Theme.Muted);
            _lblDefaultNote.Margin = new Padding(Theme.S(22), 0, 0, Theme.S(6));

            RbGenerate = Radio("Generate a password");
            _genPanel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(Theme.S(22), 0, 0, Theme.S(4)) };
            TxtGenerated = new TextBox { Font = Theme.Mono, ReadOnly = true, Width = Theme.S(210), BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(248, 250, 252), Margin = new Padding(0, Theme.S(4), Theme.S(6), 0) };
            _btnRegen = new FlatBtn("New", false);
            _btnCopy = new FlatBtn("Copy", false);
            _genPanel.Controls.AddRange(new Control[] { TxtGenerated, _btnRegen, _btnCopy });

            RbCustom = Radio("Type a password");
            _customPanel = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Top, Margin = new Padding(Theme.S(22), 0, 0, 0) };
            ((TableLayoutPanel)_customPanel).ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            ((TableLayoutPanel)_customPanel).ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            TxtPwd = new TextBox { Font = Theme.Base, UseSystemPasswordChar = true, Width = Theme.S(230), BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, Theme.S(3), 0, Theme.S(3)) };
            TxtConfirm = new TextBox { Font = Theme.Base, UseSystemPasswordChar = true, Width = Theme.S(230), BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, Theme.S(3), 0, Theme.S(3)) };
            ChkShow = new CheckBox { Text = "Show", Font = Theme.Small, AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, Theme.S(4), 0, 0) };
            var l1 = Theme.Label("New password", Theme.Small, Theme.Muted); l1.Margin = new Padding(0, Theme.S(7), Theme.S(10), 0);
            var l2 = Theme.Label("Confirm", Theme.Small, Theme.Muted); l2.Margin = new Padding(0, Theme.S(7), Theme.S(10), 0);
            ((TableLayoutPanel)_customPanel).Controls.Add(l1, 0, 0);
            ((TableLayoutPanel)_customPanel).Controls.Add(TxtPwd, 1, 0);
            ((TableLayoutPanel)_customPanel).Controls.Add(l2, 0, 1);
            ((TableLayoutPanel)_customPanel).Controls.Add(TxtConfirm, 1, 1);
            ((TableLayoutPanel)_customPanel).Controls.Add(ChkShow, 1, 2);

            _lblValidation = new Label { AutoSize = true, Font = Theme.Small, Margin = new Padding(0, Theme.S(6), 0, Theme.S(2)), MaximumSize = new Size(Theme.S(420), 0) };
            ChkMustChange = new CheckBox { Text = "User must change password at next logon", Font = Theme.Base, AutoSize = true, Checked = true, Margin = new Padding(0, Theme.S(4), 0, 0) };

            t.Controls.Add(RbDefault);
            t.Controls.Add(_lblDefault);
            t.Controls.Add(_lblDefaultNote);
            t.Controls.Add(RbGenerate);
            t.Controls.Add(_genPanel);
            t.Controls.Add(RbCustom);
            t.Controls.Add(_customPanel);
            t.Controls.Add(_lblValidation);
            t.Controls.Add(ChkMustChange);
            Controls.Add(t);

            RbDefault.CheckedChanged += delegate { ModeChanged(); };
            RbGenerate.CheckedChanged += delegate { ModeChanged(); };
            RbCustom.CheckedChanged += delegate { ModeChanged(); };
            _btnRegen.Click += delegate { Regenerate(); };
            _btnCopy.Click += delegate { CopyToClipboard(TxtGenerated.Text); };
            TxtPwd.TextChanged += delegate { OnChanged(); };
            TxtConfirm.TextChanged += delegate { OnChanged(); };
            ChkMustChange.CheckedChanged += delegate { if (!RbDefault.Checked) _mustChangeBeforeDefault = ChkMustChange.Checked; OnChanged(); };
            ChkShow.CheckedChanged += delegate
            {
                TxtPwd.UseSystemPasswordChar = !ChkShow.Checked;
                TxtConfirm.UseSystemPasswordChar = !ChkShow.Checked;
            };
            RbDefault.Checked = true;
        }

        static RadioButton Radio(string text)
        {
            return new RadioButton { Text = text, Font = Theme.Bold, AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(0, Theme.S(6), 0, Theme.S(2)) };
        }

        public void Configure(string defaultPassword, string style, int length)
        {
            DefaultPassword = defaultPassword ?? "";
            Style = style; Length = length;
            _lblDefault.Text = DefaultPassword.Length > 0 ? DefaultPassword : "(not set in config file)";
            RbDefault.Enabled = DefaultPassword.Length > 0;
            if (!RbDefault.Enabled && RbDefault.Checked) RbGenerate.Checked = true;
            if (TxtGenerated.Text.Length == 0 || RbGenerate.Checked) Regenerate();
            ModeChanged();
        }

        public void Regenerate()
        {
            TxtGenerated.Text = PasswordTools.Generate(Style, Length);
            OnChanged();
        }

        void ModeChanged()
        {
            _genPanel.Visible = RbGenerate.Checked;
            _customPanel.Visible = RbCustom.Checked;
            _lblDefaultNote.Visible = RbDefault.Checked;
            if (RbGenerate.Checked && TxtGenerated.Text.Length == 0) Regenerate();
            if (RbDefault.Checked)
            {
                ChkMustChange.Checked = true;
                ChkMustChange.Enabled = false;
            }
            else
            {
                if (!ChkMustChange.Enabled) ChkMustChange.Checked = _mustChangeBeforeDefault;
                ChkMustChange.Enabled = true;
            }
            if (RbCustom.Checked && Visible) TxtPwd.Focus();
            OnChanged();
        }

        public string Password
        {
            get
            {
                if (RbDefault.Checked) return DefaultPassword;
                if (RbGenerate.Checked) return TxtGenerated.Text;
                return TxtPwd.Text;
            }
        }

        public bool MustChange { get { return RbDefault.Checked || ChkMustChange.Checked; } }

        /// <summary>Returns null when the chosen password is usable, otherwise why not.</summary>
        public string Problem()
        {
            if (RbCustom.Checked)
            {
                if (TxtPwd.Text.Length == 0) return "Type the new password.";
                if (TxtPwd.Text != TxtConfirm.Text) return "The two passwords do not match.";
            }
            return Validator != null ? Validator() : null;
        }

        public void Reset()
        {
            TxtPwd.Text = ""; TxtConfirm.Text = ""; ChkShow.Checked = false;
            Regenerate();
            RbDefault.Checked = RbDefault.Enabled;
            if (!RbDefault.Enabled) RbGenerate.Checked = true;
        }

        public void RefreshValidation() { OnChanged(); }

        void OnChanged()
        {
            string p = Problem();
            if (RbCustom.Checked && TxtPwd.Text.Length == 0 && TxtConfirm.Text.Length == 0)
            {
                _lblValidation.ForeColor = Theme.Muted;
                _lblValidation.Text = "Type and confirm the new password.";
            }
            else if (p == null)
            {
                _lblValidation.ForeColor = Theme.Success;
                _lblValidation.Text = "✔  Password meets the domain policy";
            }
            else
            {
                _lblValidation.ForeColor = Theme.Danger;
                _lblValidation.Text = "✖  " + p;
            }
            if (Changed != null) Changed(this, EventArgs.Empty);
        }

        public static void CopyToClipboard(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            try { Clipboard.SetText(text); } catch { }
        }
    }

    /// <summary>Pick "never expires" or the last day an account may be used.</summary>
    class ExpiryDialog : Form
    {
        public DateTime? Result;
        readonly RadioButton _rbNever, _rbDate;
        readonly DateTimePicker _dtp;

        public static bool Ask(IWin32Window owner, string userName, DateTime? current, out DateTime? result)
        {
            using (var d = new ExpiryDialog(userName, current))
            {
                bool ok = d.ShowDialog(owner) == DialogResult.OK;
                result = d.Result;
                return ok;
            }
        }

        ExpiryDialog(string userName, DateTime? current)
        {
            Text = "Account expiry";
            Font = Theme.Base;
            BackColor = Color.White;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var outer = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill, Margin = new Padding(0) };
            var stripe = new Panel { Height = Theme.S(6), Dock = DockStyle.Top, BackColor = Theme.Accent, Margin = new Padding(0) };
            var content = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(Theme.S(24), Theme.S(18), Theme.S(24), Theme.S(8)), Margin = new Padding(0) };

            var h = Theme.Label("Account expiry for " + userName, Theme.Big, Theme.Text);
            h.Margin = new Padding(0, 0, 0, Theme.S(6));
            var b = Theme.Label("After the end of the chosen day the account can no longer sign in.\nIts files, groups and password are kept, and it can be extended at any time.", Theme.Base, Theme.Muted);
            b.Margin = new Padding(0, 0, 0, Theme.S(10));

            _rbNever = new RadioButton { Text = "Never expires", Font = Theme.Bold, AutoSize = true, Margin = new Padding(0, Theme.S(4), 0, Theme.S(4)) };
            _rbDate = new RadioButton { Text = "Account expires at the end of:", Font = Theme.Bold, AutoSize = true, Margin = new Padding(0, Theme.S(8), 0, Theme.S(2)) };
            _dtp = new DateTimePicker
            {
                Format = DateTimePickerFormat.Custom, CustomFormat = "dddd  dd MMMM yyyy", Width = Theme.S(260),
                Font = Theme.Base, Margin = new Padding(Theme.S(22), Theme.S(2), 0, Theme.S(6)), MinDate = DateTime.Today.AddYears(-5)
            };

            var quick = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(Theme.S(480), 0), Margin = new Padding(Theme.S(22), 0, 0, Theme.S(6)) };
            DateTime today = DateTime.Today;
            DateTime aug = new DateTime(today.Month > 8 || (today.Month == 8 && today.Day == 31) ? today.Year + 1 : today.Year, 8, 31);
            AddQuick(quick, "1 week", today.AddDays(7));
            AddQuick(quick, "1 month", today.AddMonths(1));
            AddQuick(quick, "3 months", today.AddMonths(3));
            AddQuick(quick, "31 Aug " + aug.Year, aug);
            AddQuick(quick, "Today", today);

            content.Controls.AddRange(new Control[] { h, b, _rbNever, _rbDate, _dtp, quick });

            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Padding = new Padding(Theme.S(24), Theme.S(8), Theme.S(16), Theme.S(14)), BackColor = Color.FromArgb(247, 248, 250), Margin = new Padding(0) };
            var ok = new FlatBtn("Save", true);
            var cancel = new FlatBtn("Cancel", false) { DialogResult = DialogResult.Cancel };
            ok.Click += delegate
            {
                Result = _rbDate.Checked ? (DateTime?)_dtp.Value.Date : null;
                DialogResult = DialogResult.OK;
            };
            buttons.Controls.Add(ok); buttons.Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;

            outer.Controls.Add(stripe);
            outer.Controls.Add(content);
            outer.Controls.Add(buttons);
            Controls.Add(outer);

            _rbNever.CheckedChanged += delegate { _dtp.Enabled = _rbDate.Checked; };
            _dtp.Value = current.HasValue && current.Value >= _dtp.MinDate ? current.Value : aug;
            if (current.HasValue) _rbDate.Checked = true; else _rbNever.Checked = true;
            _dtp.Enabled = _rbDate.Checked;
        }

        void AddQuick(FlowLayoutPanel host, string text, DateTime date)
        {
            var btn = new FlatBtn(text, false) { MinimumSize = new Size(Theme.S(60), Theme.S(28)), Font = Theme.Small };
            btn.Click += delegate { _rbDate.Checked = true; _dtp.Value = date; };
            host.Controls.Add(btn);
        }
    }

    /// <summary>Modern looking message / confirmation dialog.</summary>
    class InfoDialog : Form
    {
        public static DialogResult Show(IWin32Window owner, string heading, string body, string highlight,
                                        Color tone, string okText, string cancelText)
        {
            using (var d = new InfoDialog(heading, body, highlight, tone, okText, cancelText))
                return d.ShowDialog(owner);
        }

        InfoDialog(string heading, string body, string highlight, Color tone, string okText, string cancelText)
        {
            Text = heading;
            Font = Theme.Base;
            BackColor = Color.White;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(0);

            var outer = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill, Padding = new Padding(0), Margin = new Padding(0) };

            var stripe = new Panel { Height = Theme.S(6), Dock = DockStyle.Top, BackColor = tone, Margin = new Padding(0) };
            var content = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(Theme.S(24), Theme.S(18), Theme.S(24), Theme.S(8)), Margin = new Padding(0) };
            var h = Theme.Label(heading, Theme.Big, Theme.Text);
            h.Margin = new Padding(0, 0, 0, Theme.S(10));
            content.Controls.Add(h);
            var b = new Label { UseMnemonic = false, Text = body, Font = Theme.Base, ForeColor = Theme.Text, AutoSize = true, MaximumSize = new Size(Theme.S(520), 0), Margin = new Padding(0, 0, 0, Theme.S(8)) };
            content.Controls.Add(b);

            if (!string.IsNullOrEmpty(highlight))
            {
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, Theme.S(4), 0, Theme.S(8)) };
                var pw = new TextBox { Text = highlight, Font = Theme.Mono, ReadOnly = true, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(248, 250, 252), ForeColor = Theme.Accent, Width = Theme.S(260), Margin = new Padding(0, Theme.S(4), Theme.S(8), 0) };
                var copy = new FlatBtn("Copy", false);
                copy.Click += delegate { PasswordChooser.CopyToClipboard(highlight); copy.Text = "Copied ✔"; };
                row.Controls.Add(pw); row.Controls.Add(copy);
                content.Controls.Add(row);
            }

            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Padding = new Padding(Theme.S(24), Theme.S(8), Theme.S(16), Theme.S(14)), BackColor = Color.FromArgb(247, 248, 250), Margin = new Padding(0) };
            var ok = new FlatBtn(okText, true) { DialogResult = DialogResult.OK };
            buttons.Controls.Add(ok);
            AcceptButton = ok;
            if (!string.IsNullOrEmpty(cancelText))
            {
                var cancel = new FlatBtn(cancelText, false) { DialogResult = DialogResult.Cancel };
                buttons.Controls.Add(cancel);
                CancelButton = cancel;
            }
            else CancelButton = ok;

            outer.Controls.Add(stripe);
            outer.Controls.Add(content);
            outer.Controls.Add(buttons);
            Controls.Add(outer);
            MinimumSize = new Size(Theme.S(420), 0);
        }
    }
}

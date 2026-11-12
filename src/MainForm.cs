using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ADUserManager
{
    public class MainForm : Form
    {
        AppConfig _cfg;
        AdService _ad = new AdService();
        AuditLog _log;
        int _busy;

        // shell
        HeaderBanner _header;
        NavTab _tabReset, _tabCreate, _tabSettings;
        Panel _pageReset, _pageCreate, _pageSettings;
        Label _status, _footScope;
        ProgressBar _progress;

        // reset page
        TextBox _txtSearch;
        FlatBtn _btnSearch, _btnListAll, _btnReset, _btnToggleEnabled, _btnExpiry;
        HashSet<string> _selRights;          // attributes we may write on the selected user (null = unknown)
        ToolTip _tips = new ToolTip();
        DateTimePicker _dtpCreateExpiry;
        CheckBox _chkCreateExpiry;
        ListView _list;
        Label _lblCount;
        TableLayoutPanel _selBody;
        PasswordChooser _pwReset;
        CheckBox _chkUnlock;
        AdUser _selected;

        // create page
        TextBox _txtFirst, _txtLast, _txtDisplay, _txtSam, _txtMail, _txtTitle, _txtDesc;
        Label _lblSamStatus, _lblUpn, _pvProfile, _pvHome, _pvGroups, _pvWarn;
        FlowLayoutPanel _samAlts;
        ComboBox _cmbType, _cmbOU;
        CheckBox _chkEnabled, _chkHome;
        PasswordChooser _pwCreate;
        FlatBtn _btnCreate, _btnClear;
        Timer _samTimer;
        bool _samEdited, _displayEdited, _mailEdited, _suppress;
        bool? _samAvailable;

        // settings page
        TextBox _txtSettings;
        Label _lblCfgPath;
        RichTextBox _rtbReq;
        FlatBtn _btnReqRun, _btnNet, _btnRsat;
        bool _reqRunning;

        class OuItem
        {
            public string Dn;
            public override string ToString() { return AdService.FriendlyPath(Dn); }
        }

        public MainForm()
        {
            Text = "AD User Manager";
            Font = Theme.Base;
            BackColor = Theme.Bg;
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(Theme.S(960), Theme.S(640));
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Min(Theme.S(1220), wa.Width), Math.Min(Theme.S(860), wa.Height));
            if (wa.Width < Theme.S(1100) || wa.Height < Theme.S(780)) WindowState = FormWindowState.Maximized;
            KeyPreview = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            BuildShell();
            BuildResetPage();
            BuildCreatePage();
            BuildSettingsPage();
            ShowPage(_pageReset);
            UpdateEnabled();
        }

        // ==================================================================
        // Shell
        // ==================================================================

        void BuildShell()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Margin = new Padding(0), Padding = new Padding(0), BackColor = Theme.Bg };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(70)));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(45)));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(26)));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(30)));

            _header = new HeaderBanner { Dock = DockStyle.Fill, Margin = new Padding(0) };
            root.Controls.Add(_header, 0, 0);

            var nav = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Margin = new Padding(0) };
            nav.Paint += delegate (object s, PaintEventArgs e)
            {
                using (var p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, 0, nav.Height - 1, nav.Width, nav.Height - 1);
            };
            var navFlow = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, WrapContents = false, Padding = new Padding(Theme.S(10), 0, 0, 0), BackColor = Color.White, Margin = new Padding(0) };
            _tabReset = new NavTab("Reset password", "");
            _tabCreate = new NavTab("Create user", "");
            _tabSettings = new NavTab("Scope & settings", "");
            _tabReset.Click += delegate { ShowPage(_pageReset); };
            _tabCreate.Click += delegate { ShowPage(_pageCreate); };
            _tabSettings.Click += delegate { ShowPage(_pageSettings); };
            navFlow.Controls.AddRange(new Control[] { _tabReset, _tabCreate, _tabSettings });
            nav.Controls.Add(navFlow);
            root.Controls.Add(nav, 0, 1);

            var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Theme.S(10), Theme.S(8), Theme.S(10), Theme.S(0)), Margin = new Padding(0), BackColor = Theme.Bg };
            _pageReset = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            _pageCreate = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            _pageSettings = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            content.Controls.AddRange(new Control[] { _pageReset, _pageCreate, _pageSettings });
            root.Controls.Add(content, 0, 2);

            var statusBar = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0), Padding = new Padding(Theme.S(20), 0, Theme.S(20), 0), BackColor = Theme.Bg };
            _status = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Muted, Font = Theme.Small, AutoEllipsis = true };
            _progress = new ProgressBar { Dock = DockStyle.Right, Width = Theme.S(150), Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 25, Visible = false, Margin = new Padding(0, Theme.S(8), 0, Theme.S(8)) };
            var progressHost = new Panel { Dock = DockStyle.Right, Width = Theme.S(150), Padding = new Padding(0, Theme.S(8), 0, Theme.S(8)) };
            progressHost.Controls.Add(_progress);
            _progress.Dock = DockStyle.Fill;
            statusBar.Controls.Add(_status);
            statusBar.Controls.Add(progressHost);
            root.Controls.Add(statusBar, 0, 3);

            var footer = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Margin = new Padding(0), Padding = new Padding(Theme.S(20), 0, Theme.S(20), 0) };
            footer.Paint += delegate (object s, PaintEventArgs e)
            {
                using (var p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, 0, 0, footer.Width, 0);
            };
            _footScope = new Label
            {
                UseMnemonic = false,
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = Theme.Small, ForeColor = Theme.Muted, AutoEllipsis = true,
                Text = "\U0001F512  Users outside the OU scope defined in " + AppConfig.FileName + " cannot be searched or changed."
            };
            var credit = new Label
            {
                Dock = DockStyle.Right, AutoSize = true, TextAlign = ContentAlignment.MiddleRight, Font = Theme.Small, ForeColor = Theme.Muted,
                Text = "Developed by support@icthero.co.uk  ·  11/11/26", Padding = new Padding(0, Theme.S(7), 0, 0)
            };
            footer.Controls.Add(_footScope);
            footer.Controls.Add(credit);
            root.Controls.Add(footer, 0, 4);

            Controls.Add(root);
        }

        NavTab TabFor(Panel page)
        {
            return page == _pageReset ? _tabReset : page == _pageCreate ? _tabCreate : _tabSettings;
        }

        /// <summary>Hides the tabs switched off in [Features] and moves off a hidden page.</summary>
        Panel DefaultPage()
        {
            return _cfg.DefaultTab == "Create" ? _pageCreate : _cfg.DefaultTab == "Scope" ? _pageSettings : _pageReset;
        }

        void ApplyTabVisibility(bool firstLoad)
        {
            _tabReset.Visible = _cfg.ShowResetTab;
            _tabCreate.Visible = _cfg.ShowCreateTab;
            _tabSettings.Visible = _cfg.ShowSettingsTab;
            var jobs = new List<string>();
            if (_cfg.ShowCreateTab) jobs.Add("Create users");
            if (_cfg.ShowResetTab) jobs.Add("Reset passwords");
            _header.Subtitle = jobs.Count > 0 ? string.Join("  ·  ", jobs.ToArray()) : "Scope & settings";
            Panel current = _pageCreate.Visible ? _pageCreate : _pageSettings.Visible ? _pageSettings : _pageReset;
            ShowPage(firstLoad ? DefaultPage() : current);
        }

        void ShowPage(Panel page)
        {
            if (!TabFor(page).Visible)
            {
                // Page switched off in the config - go to the first tab still shown.
                if (_tabReset.Visible) page = _pageReset;
                else if (_tabCreate.Visible) page = _pageCreate;
                else page = _pageSettings;
            }
            _pageReset.Visible = page == _pageReset;
            _pageCreate.Visible = page == _pageCreate;
            _pageSettings.Visible = page == _pageSettings;
            _tabReset.Selected = page == _pageReset;
            _tabCreate.Selected = page == _pageCreate;
            _tabSettings.Selected = page == _pageSettings;
            if (page == _pageReset && _txtSearch != null) _txtSearch.Focus();
            if (page == _pageCreate && _txtFirst != null) _txtFirst.Focus();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.F) && _tabReset.Visible) { ShowPage(_pageReset); _txtSearch.Focus(); _txtSearch.SelectAll(); return true; }
            if (keyData == (Keys.Control | Keys.N) && _tabCreate.Visible) { ShowPage(_pageCreate); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ==================================================================
        // Reset page
        // ==================================================================

        void BuildResetPage()
        {
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0), BackColor = Theme.Bg };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // ---- Find a user
            var find = new Card("Find a user");
            var ft = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Theme.Card, Margin = new Padding(0) };
            ft.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            ft.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            ft.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            ft.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var searchRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, Margin = new Padding(0) };
            searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _txtSearch = new TextBox { Font = new Font("Segoe UI", 11f), Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, Theme.S(4), Theme.S(8), Theme.S(4)) };
            Theme.Cue(_txtSearch, "Name or username, e.g. jsmith or Jane Sm…");
            _txtSearch.KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; DoSearch(false); }
                if (e.KeyCode == Keys.Down && _list.Items.Count > 0) { e.SuppressKeyPress = true; _list.Focus(); if (_list.SelectedItems.Count == 0) _list.Items[0].Selected = true; }
            };
            _btnSearch = new FlatBtn("Search", true);
            _btnListAll = new FlatBtn("List all", false);
            _btnListAll.Margin = new Padding(0, Theme.S(3), 0, Theme.S(3));
            _btnSearch.Click += delegate { DoSearch(false); };
            _btnListAll.Click += delegate { DoSearch(true); };
            searchRow.Controls.Add(_txtSearch, 0, 0);
            searchRow.Controls.Add(_btnSearch, 1, 0);
            searchRow.Controls.Add(_btnListAll, 2, 0);

            var hint = Theme.Label("Type part of a name, or a full username, then press Enter. Wildcards (*) are allowed.", Theme.Small, Theme.Muted);
            hint.Margin = new Padding(0, 0, 0, Theme.S(8));

            _list = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false,
                BorderStyle = BorderStyle.FixedSingle, Font = Theme.Base, Margin = new Padding(0)
            };
            _list.Columns.Add("Name", Theme.S(190));
            _list.Columns.Add("Username", Theme.S(110));
            _list.Columns.Add("Location", Theme.S(200));
            _list.Columns.Add("Status", Theme.S(80));
            _list.SelectedIndexChanged += delegate
            {
                ShowSelected(_list.SelectedItems.Count > 0 ? (AdUser)_list.SelectedItems[0].Tag : null);
            };
            _list.Resize += delegate { FitListColumns(); };
            _list.DoubleClick += delegate { if (_selected != null) _pwReset.Focus(); };
            int sortCol = 0; bool asc = true;
            _list.ColumnClick += delegate (object s, ColumnClickEventArgs e)
            {
                asc = e.Column == sortCol ? !asc : true; sortCol = e.Column;
                _list.ListViewItemSorter = new ItemComparer(sortCol, asc);
            };
            SetEmptyListMessage();

            _lblCount = Theme.Label("", Theme.Small, Theme.Muted);
            _lblCount.Margin = new Padding(0, Theme.S(6), 0, 0);

            ft.Controls.Add(searchRow, 0, 0);
            ft.Controls.Add(hint, 0, 1);
            ft.Controls.Add(_list, 0, 2);
            ft.Controls.Add(_lblCount, 0, 3);
            find.Controls.Add(ft);

            // ---- Right column
            var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0), BackColor = Theme.Bg };
            right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(238)));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var sel = new Card("Selected user");
            _selBody = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, BackColor = Theme.Card, Margin = new Padding(0) };
            var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = false, BackColor = Theme.Card, Margin = new Padding(0), Padding = new Padding(0, Theme.S(4), 0, 0) };
            _btnToggleEnabled = new FlatBtn("Disable account", false) { MinimumSize = new Size(Theme.S(140), Theme.S(32)) };
            _btnExpiry = new FlatBtn("Account expiry…", false) { MinimumSize = new Size(Theme.S(140), Theme.S(32)) };
            _btnToggleEnabled.Click += delegate { DoToggleEnabled(); };
            _btnExpiry.Click += delegate { DoExpiry(); };
            actions.Controls.Add(_btnToggleEnabled);
            actions.Controls.Add(_btnExpiry);
            sel.Controls.Add(_selBody);
            sel.Controls.Add(actions);
            _selBody.BringToFront();

            var pwCard = new Card("New password");
            _pwReset = new PasswordChooser();
            _pwReset.Validator = delegate
            {
                return PasswordTools.Validate(_pwReset.Password, _ad.MinPwdLength, _ad.Complexity,
                    _selected != null ? _selected.Sam : null, _selected != null ? _selected.DisplayName : null);
            };
            _chkUnlock = new CheckBox { Text = "Unlock the account", AutoSize = true, Font = Theme.Base, Margin = new Padding(0, Theme.S(4), 0, Theme.S(8)), Enabled = false };
            _btnReset = new FlatBtn("Reset password", true) { MinimumSize = new Size(Theme.S(170), Theme.S(38)) };
            _btnReset.Click += delegate { DoReset(); };
            pwCard.Controls.Add(ScrollWithFooter(_pwReset, _chkUnlock, _btnReset));

            right.Controls.Add(sel, 0, 0);
            right.Controls.Add(pwCard, 0, 1);

            grid.Controls.Add(find, 0, 0);
            grid.Controls.Add(right, 1, 0);
            _pageReset.Controls.Add(grid);
            ShowSelected(null);
        }

        class ItemComparer : System.Collections.IComparer
        {
            readonly int _col; readonly bool _asc;
            public ItemComparer(int col, bool asc) { _col = col; _asc = asc; }
            public int Compare(object x, object y)
            {
                var a = (ListViewItem)x; var b = (ListViewItem)y;
                int r = string.Compare(a.SubItems[_col].Text, b.SubItems[_col].Text, StringComparison.CurrentCultureIgnoreCase);
                return _asc ? r : -r;
            }
        }

        void FitListColumns()
        {
            if (_list.Columns.Count < 4) return;
            int w = _list.ClientSize.Width - 4;
            if (w < Theme.S(300)) return;
            _list.Columns[0].Width = (int)(w * 0.31);
            _list.Columns[1].Width = (int)(w * 0.23);
            _list.Columns[3].Width = (int)(w * 0.16);
            _list.Columns[2].Width = w - _list.Columns[0].Width - _list.Columns[1].Width - _list.Columns[3].Width;
        }

        /// <summary>Location shown relative to the scope root it sits under (shorter, easier to scan).</summary>
        string ShortLocation(AdUser u)
        {
            string parent = AdService.ParentDn(u.Dn);
            string root = _ad.ResetRoots.Where(r => AdService.IsUnder(parent, r)).OrderByDescending(r => r.Length).FirstOrDefault();
            if (root == null) return u.Location;
            string full = AdService.FriendlyPath(parent), rootPath = AdService.FriendlyPath(root);
            if (full.Length > rootPath.Length && full.StartsWith(rootPath)) return full.Substring(rootPath.Length).TrimStart(' ', '›');
            return AdService.FriendlyPath(root).Split('›').Last().Trim();
        }

        void SetEmptyListMessage()
        {
            _list.Items.Clear();
        }

        async void DoSearch(bool all)
        {
            if (!_ad.Connected || _busy > 0) return;
            string term = all ? "" : _txtSearch.Text.Trim();
            if (!all && term.Length == 0)
            {
                SetStatus("Type a name or username to search, or click 'List all'.");
                _txtSearch.Focus();
                return;
            }
            if (all) _txtSearch.Text = "";

            bool truncated = false;
            List<AdUser> results;
            int max = _cfg.MaxSearchResults;
            try
            {
                results = await RunAsync("Searching your OUs…", delegate { return _ad.Search(term, max, out truncated); });
            }
            catch (Exception ex)
            {
                ShowError("Search failed", ex);
                return;
            }

            _list.BeginUpdate();
            _list.ListViewItemSorter = null;
            _list.Items.Clear();
            ListViewItem pick = null;
            foreach (AdUser u in results)
            {
                var item = new ListViewItem(new[] { u.Name, u.Sam, ShortLocation(u), StatusText(u) }) { Tag = u };
                if (!u.Enabled) item.ForeColor = Theme.Muted;
                else if (u.Locked || u.Expired) item.ForeColor = Theme.Danger;
                _list.Items.Add(item);
                if (string.Equals(u.Sam, term, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(u.Upn, term, StringComparison.OrdinalIgnoreCase)) pick = item;
            }
            _list.EndUpdate();
            FitListColumns();

            if (results.Count == 0)
            {
                _lblCount.ForeColor = Theme.Warn;
                _lblCount.Text = all ? "No users found in your OU scope."
                                     : "No users matching “" + term + "” were found within your OU scope.";
                ShowSelected(null);
            }
            else
            {
                _lblCount.ForeColor = Theme.Muted;
                _lblCount.Text = results.Count + (results.Count == 1 ? " user" : " users") + " found" +
                                 (truncated ? "  —  showing the first " + max + ", refine your search" : "") +
                                 "   ·   click a column to sort";
                if (pick == null && results.Count == 1) pick = _list.Items[0];
                if (pick != null) { pick.Selected = true; pick.Focused = true; pick.EnsureVisible(); }
                else ShowSelected(null);
            }
            SetStatus(results.Count + " result(s) for " + (all ? "all users in scope" : "“" + term + "”"));
        }

        static string StatusText(AdUser u)
        {
            if (!u.Enabled) return "Disabled";
            if (u.Expired) return "Expired";
            if (u.Locked) return "Locked";
            if (u.MustChange) return "Must change";
            return "Active";
        }

        void ShowSelected(AdUser u)
        {
            _selected = u;
            _selBody.SuspendLayout();
            _selBody.Controls.Clear();
            _selBody.RowStyles.Clear();
            if (u == null)
            {
                var icon = new Label { Text = "", Font = new Font("Segoe MDL2 Assets", 20f), ForeColor = Theme.Border, AutoSize = true, Margin = new Padding(0, Theme.S(4), 0, Theme.S(6)) };
                var msg = Theme.Label("No user selected.\nSearch on the left and pick a user, or type a\nusername and press Enter.", Theme.Base, Theme.Muted);
                _selBody.Controls.Add(icon);
                _selBody.Controls.Add(msg);
            }
            else
            {
                var name = Theme.Label(u.Name, Theme.Big, Theme.Text);
                name.Margin = new Padding(0, 0, 0, 0);
                name.AutoEllipsis = true;
                var ids = Theme.Label(u.Sam + (u.Upn.Length > 0 ? "   ·   " + u.Upn : ""), Theme.Base, Theme.Muted);
                ids.Margin = new Padding(0, 0, 0, Theme.S(2));

                var chips = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), BackColor = Theme.Card };
                chips.Controls.Add(u.Enabled ? new Chip("Enabled", Theme.Success, Theme.SuccessLight) : new Chip("Disabled", Theme.Muted, Theme.Neutral));
                if (u.Expired) chips.Controls.Add(new Chip("Expired " + u.ExpiresEndOf.Value.ToString("dd/MM/yyyy"), Theme.Danger, Theme.DangerLight));
                else if (u.ExpiresEndOf.HasValue) chips.Controls.Add(new Chip("Expires " + u.ExpiresEndOf.Value.ToString("dd/MM/yyyy"), Theme.Warn, Theme.WarnLight));
                if (u.Locked) chips.Controls.Add(new Chip("Locked out", Theme.Danger, Theme.DangerLight));
                if (u.MustChange) chips.Controls.Add(new Chip("Must change password", Theme.Warn, Theme.WarnLight));
                if (u.NeverExpires && chips.Controls.Count < 3) chips.Controls.Add(new Chip("Password never expires", Theme.Muted, Theme.Neutral));

                var where = Theme.Label("\U0001F4C1  " + u.Location, Theme.Small, Theme.Muted);
                var dates = Theme.Label("Password set: " + Fmt(u.PwdLastSet) + "     Last logon: " + Fmt(u.LastLogon), Theme.Small, Theme.Muted);
                dates.Margin = new Padding(0, 0, 0, 0);
                _selBody.Controls.Add(name);
                _selBody.Controls.Add(ids);
                _selBody.Controls.Add(chips);
                _selBody.Controls.Add(where);
                _selBody.Controls.Add(dates);
            }
            _selBody.ResumeLayout();

            _chkUnlock.Enabled = u != null && u.Locked;
            _chkUnlock.Checked = u != null && u.Locked;
            _chkUnlock.Text = u != null && u.Locked ? "Unlock the account (currently locked out)" : "Unlock the account";
            _btnToggleEnabled.Text = u != null && !u.Enabled ? "Enable account" : "Disable account";
            _pwReset.RefreshValidation();
            _selRights = null;
            UpdateEnabled();
            if (u != null) LoadRights(u.Dn);
        }

        /// <summary>Asks the DC which attributes we may write on this user, so buttons reflect real permissions.</summary>
        async void LoadRights(string dn)
        {
            HashSet<string> rights = null;
            try { rights = await Task.Run(delegate { return _ad.WritableAttributes(dn); }); }
            catch { rights = null; }
            if (_selected == null || _selected.Dn != dn) return;
            _selRights = rights;
            UpdateEnabled();
        }

        bool MayWrite(string attribute)
        {
            return _selRights == null || _selRights.Contains(attribute);
        }

        /// <summary>Re-reads a user and refreshes the list row and the selected-user card.</summary>
        async Task RefreshUser(string dn)
        {
            try
            {
                AdUser fresh = await RunAsync("Refreshing…", delegate { return _ad.Reload(dn); });
                if (fresh == null) return;
                foreach (ListViewItem it in _list.Items)
                    if (((AdUser)it.Tag).Dn == fresh.Dn)
                    {
                        it.Tag = fresh;
                        it.SubItems[3].Text = StatusText(fresh);
                        it.ForeColor = !fresh.Enabled ? Theme.Muted : (fresh.Locked || fresh.Expired) ? Theme.Danger : Theme.Text;
                    }
                if (_selected != null && _selected.Dn == fresh.Dn) ShowSelected(fresh);
            }
            catch { }
        }

        async void DoToggleEnabled()
        {
            AdUser u = _selected;
            if (u == null || _busy > 0) return;
            bool enable = !u.Enabled;
            string verb = enable ? "Enable" : "Disable";
            string body = "User:\t" + u.Name + "  (" + u.Sam + ")\nLocation:\t" + u.Location + "\n\n" +
                          (enable ? "The user will be able to sign in again." + (u.Expired ? "\nNote: the account has also EXPIRED - set a new expiry date too." : "")
                                  : "The user will not be able to sign in. Their files, groups and password are kept,\nand the account can be enabled again at any time.");
            if (InfoDialog.Show(this, verb + " " + u.Name + "?", body, null, enable ? Theme.Accent : Theme.Danger,
                                verb + " account", "Cancel") != DialogResult.OK) return;
            try
            {
                await RunAsync(verb + "ing " + u.Sam + "…", delegate { _ad.SetEnabled(u, enable); });
            }
            catch (Exception ex)
            {
                _log.Write(verb + " account", u, "", "FAILED: " + AdService.Friendly(ex));
                ShowError("Account not " + (enable ? "enabled" : "disabled"), ex);
                return;
            }
            _log.Write(verb + " account", u, "was " + StatusText(u).ToLowerInvariant(), "Success");
            SetStatus(u.Name + " (" + u.Sam + ") " + (enable ? "enabled" : "disabled") + " at " + DateTime.Now.ToString("HH:mm:ss"));
            await RefreshUser(u.Dn);
        }

        async void DoExpiry()
        {
            AdUser u = _selected;
            if (u == null || _busy > 0) return;
            DateTime? lastDay;
            if (!ExpiryDialog.Ask(this, u.Name, u.ExpiresEndOf, out lastDay)) return;
            string before = u.ExpiresEndOf.HasValue ? "end of " + u.ExpiresEndOf.Value.ToString("dd/MM/yyyy") : "never";
            string after = lastDay.HasValue ? "end of " + lastDay.Value.ToString("dd/MM/yyyy") : "never";
            if (before == after) { SetStatus("Account expiry unchanged."); return; }
            try
            {
                await RunAsync("Updating account expiry for " + u.Sam + "…", delegate { _ad.SetExpiry(u, lastDay); });
            }
            catch (Exception ex)
            {
                _log.Write("Set account expiry", u, "from " + before + " to " + after, "FAILED: " + AdService.Friendly(ex));
                ShowError("Expiry not changed", ex);
                return;
            }
            _log.Write("Set account expiry", u, "from " + before + " to " + after, "Success");
            SetStatus("Account expiry for " + u.Sam + " set to " + after);
            await RefreshUser(u.Dn);
        }

        static string Fmt(DateTime? d)
        {
            return d.HasValue ? d.Value.ToString("dd/MM/yyyy HH:mm") : "never";
        }

        async void DoReset()
        {
            AdUser u = _selected;
            if (u == null || _busy > 0) return;
            string problem = _pwReset.Problem();
            if (problem != null)
            {
                InfoDialog.Show(this, "Check the password", problem, null, Theme.Warn, "OK", null);
                return;
            }
            string pwd = _pwReset.Password;
            bool must = _pwReset.MustChange;
            bool unlock = _chkUnlock.Enabled && _chkUnlock.Checked;
            string source = _pwReset.RbDefault.Checked ? "default password" : _pwReset.RbGenerate.Checked ? "generated password" : "typed password";

            string body = "User:\t" + u.Name + "  (" + u.Sam + ")\n" +
                          "Location:\t" + u.Location + "\n" +
                          "Password:\t" + source + "\n" +
                          "Change at next logon:\t" + (must ? "Yes" : "No") +
                          (unlock ? "\nUnlock account:\tYes" : "");
            if (InfoDialog.Show(this, "Reset password for " + u.Name + "?", body, null, Theme.Accent, "Reset password", "Cancel") != DialogResult.OK)
                return;
            string resetDetails = source + "; must change at next logon=" + (must ? "Yes" : "No") +
                                  (unlock ? "; account unlocked" : "") + "; was " + StatusText(u).ToLowerInvariant();

            try
            {
                await RunAsync("Resetting password for " + u.Sam + "…", delegate { _ad.ResetPassword(u, pwd, must, unlock); });
            }
            catch (Exception ex)
            {
                _log.Write("Reset password", u, resetDetails, "FAILED: " + AdService.Friendly(ex));
                ShowError("Password not changed", ex);
                return;
            }
            _log.Write("Reset password", u, resetDetails, "Success");
            SetStatus("Password reset for " + u.Name + " (" + u.Sam + ") at " + DateTime.Now.ToString("HH:mm:ss"));

            InfoDialog.Show(this, "Password reset", u.Name + " (" + u.Sam + ") now has the password below." +
                            (must ? "\nThey will be asked to change it when they next sign in." : "") +
                            (unlock ? "\nThe account has been unlocked." : ""),
                            pwd, Theme.Success, "Done", null);

            await RefreshUser(u.Dn);
            _pwReset.Reset();
        }

        // ==================================================================
        // Create page
        // ==================================================================

        void BuildCreatePage()
        {
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0), BackColor = Theme.Bg };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // ---- Details
            var details = new Card("New user details") { AutoScroll = true };
            var f = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, BackColor = Theme.Card, Margin = new Padding(0) };
            f.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            f.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            _txtFirst = Theme.Input();
            _txtLast = Theme.Input();
            _txtDisplay = Theme.Input();
            _txtSam = Theme.Input();
            _txtMail = Theme.Input();
            _txtTitle = Theme.Input();
            _txtDesc = Theme.Input();
            _lblSamStatus = Theme.Label(" ", Theme.Small, Theme.Muted);
            _lblSamStatus.Margin = new Padding(0, 0, 0, Theme.S(4));
            _lblUpn = Theme.Label("", Theme.Base, Theme.Muted);
            _cmbType = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Font = Theme.Base, Margin = new Padding(0, Theme.S(3), 0, Theme.S(3)) };
            _cmbOU = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Font = Theme.Base, Margin = new Padding(0, Theme.S(3), 0, Theme.S(3)), DropDownWidth = Theme.S(480) };
            _chkEnabled = new CheckBox { Text = "Account enabled", AutoSize = true, Checked = true, Margin = new Padding(0, Theme.S(6), 0, 0) };

            int r = 0;
            AddRow(f, ref r, "First name *", _txtFirst);
            AddRow(f, ref r, "Last name *", _txtLast);
            AddRow(f, ref r, "Display name *", _txtDisplay);
            _samAlts = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, Theme.S(2)), BackColor = Theme.Card };
            AddRow(f, ref r, "User type *", _cmbType);
            AddRow(f, ref r, "Username *", _txtSam);
            AddRow(f, ref r, "", _samAlts);
            AddRow(f, ref r, "", _lblSamStatus);
            AddRow(f, ref r, "Logon name", _lblUpn);
            AddRow(f, ref r, "Email", _txtMail);
            AddRow(f, ref r, "Job title", _txtTitle);
            AddRow(f, ref r, "Description", _txtDesc);
            AddRow(f, ref r, "Create in OU *", _cmbOU);
            var expRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), BackColor = Theme.Card };
            _chkCreateExpiry = new CheckBox { Text = "Expires at the end of", AutoSize = true, Margin = new Padding(0, Theme.S(6), Theme.S(8), 0) };
            _dtpCreateExpiry = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "ddd dd MMM yyyy", Width = Theme.S(150), Enabled = false, Margin = new Padding(0, Theme.S(3), 0, Theme.S(3)), MinDate = DateTime.Today };
            DateTime today = DateTime.Today;
            _dtpCreateExpiry.Value = new DateTime(today.Month > 8 ? today.Year + 1 : today.Year, 8, 31);
            _chkCreateExpiry.CheckedChanged += delegate { _dtpCreateExpiry.Enabled = _chkCreateExpiry.Checked; };
            expRow.Controls.Add(_chkCreateExpiry);
            expRow.Controls.Add(_dtpCreateExpiry);
            AddRow(f, ref r, "", _chkEnabled);
            AddRow(f, ref r, "", expRow);
            details.Controls.Add(f);

            _txtFirst.TextChanged += delegate { NameChanged(); };
            _txtLast.TextChanged += delegate { NameChanged(); };
            _txtDisplay.TextChanged += delegate { if (!_suppress) _displayEdited = _txtDisplay.Text.Length > 0; UpdateCreateState(); };
            _txtSam.TextChanged += delegate
            {
                bool wasEdited = _samEdited;
                if (!_suppress) _samEdited = _txtSam.Text.Length > 0;
                if (wasEdited != _samEdited) UpdateSamAlternatives();
                _samAvailable = null;
                _lblSamStatus.ForeColor = Theme.Muted;
                _lblSamStatus.Text = _txtSam.Text.Length > 0 ? "Checking…" : " ";
                _samTimer.Stop(); _samTimer.Start();
                UpdateDerived();
            };
            _txtMail.TextChanged += delegate { if (!_suppress) _mailEdited = _txtMail.Text.Length > 0; };
            _cmbType.SelectedIndexChanged += delegate { UserTypeChanged(); };
            _cmbOU.SelectedIndexChanged += delegate { UpdateCreateState(); };

            _samTimer = new Timer { Interval = 600 };
            _samTimer.Tick += delegate { _samTimer.Stop(); CheckSam(); };

            // ---- Right column
            var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0), BackColor = Theme.Bg };
            right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(196)));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var paths = new Card("Profile & home folder");
            var pv = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Theme.Card, Margin = new Padding(0) };
            pv.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            pv.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _pvProfile = PathLabel();
            _pvHome = PathLabel();
            _pvGroups = PathLabel();
            _pvGroups.Font = Theme.Base;
            _chkHome = new CheckBox { Text = "Create the home folder on the server and grant the user access", AutoSize = true, Checked = true, Margin = new Padding(0, Theme.S(6), 0, 0) };
            _pvWarn = Theme.Label("", Theme.Small, Theme.Danger);
            _pvWarn.MaximumSize = new Size(Theme.S(440), 0);
            int pr = 0;
            AddRow(pv, ref pr, "Profile path", _pvProfile);
            AddRow(pv, ref pr, "Home folder", _pvHome);
            AddRow(pv, ref pr, "Groups", _pvGroups);
            pv.Controls.Add(_chkHome, 0, pr); pv.SetColumnSpan(_chkHome, 2); pr++;
            pv.Controls.Add(_pvWarn, 0, pr); pv.SetColumnSpan(_pvWarn, 2);
            paths.Controls.Add(pv);

            var pwCard = new Card("Password");
            _pwCreate = new PasswordChooser();
            _pwCreate.Validator = delegate
            {
                return PasswordTools.Validate(_pwCreate.Password, _ad.MinPwdLength, _ad.Complexity, _txtSam.Text.Trim(), _txtDisplay.Text.Trim());
            };
            _pwCreate.Changed += delegate { UpdateCreateState(); };
            var btnRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, Theme.S(12), 0, 0), BackColor = Theme.Card };
            _btnCreate = new FlatBtn("Create user", true) { MinimumSize = new Size(Theme.S(160), Theme.S(38)) };
            _btnClear = new FlatBtn("Clear form", false) { MinimumSize = new Size(Theme.S(100), Theme.S(38)) };
            _btnCreate.Click += delegate { DoCreate(); };
            _btnClear.Click += delegate { ClearCreateForm(); };
            btnRow.Controls.Add(_btnCreate);
            btnRow.Controls.Add(_btnClear);
            pwCard.Controls.Add(ScrollWithFooter(_pwCreate, btnRow));

            right.Controls.Add(paths, 0, 0);
            right.Controls.Add(pwCard, 0, 1);

            grid.Controls.Add(details, 0, 0);
            grid.Controls.Add(right, 1, 0);
            _pageCreate.Controls.Add(grid);
        }

        /// <summary>Scrollable body with footer controls that always stay visible (action buttons).</summary>
        static Control ScrollWithFooter(Control body, params Control[] footer)
        {
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, BackColor = Theme.Card, Margin = new Padding(0) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Card, Margin = new Padding(0) };
            body.Dock = DockStyle.Top;
            scroll.Controls.Add(body);
            t.Controls.Add(scroll, 0, 0);
            var line = new Panel { Height = 1, Dock = DockStyle.Top, BackColor = Theme.Border, Margin = new Padding(0, Theme.S(4), 0, Theme.S(4)) };
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.Controls.Add(line, 0, 1);
            for (int i = 0; i < footer.Length; i++)
            {
                t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                t.Controls.Add(footer[i], 0, i + 2);
            }
            return t;
        }

        static Label PathLabel()
        {
            return new Label { UseMnemonic = false, AutoSize = true, Font = Theme.MonoSmall, ForeColor = Theme.Text, Margin = new Padding(0, Theme.S(5), 0, Theme.S(5)), MaximumSize = new Size(Theme.S(380), 0) };
        }

        static void AddRow(TableLayoutPanel t, ref int row, string label, Control c)
        {
            if (label.Length > 0)
            {
                var l = Theme.Label(label, Theme.Base, Theme.Muted);
                l.Margin = new Padding(0, Theme.S(7), Theme.S(14), 0);
                l.Anchor = AnchorStyles.Left | AnchorStyles.Top;
                t.Controls.Add(l, 0, row);
            }
            t.Controls.Add(c, 1, row);
            row++;
        }

        void NameChanged()
        {
            if (_cfg == null) return;
            string first = _txtFirst.Text.Trim(), last = _txtLast.Text.Trim();
            _suppress = true;
            if (!_displayEdited) _txtDisplay.Text = (first + " " + last).Trim();
            if (!_samEdited) _txtSam.Text = _cfg.BuildUsername(first, last, SelectedType);
            _suppress = false;
            UpdateSamAlternatives();
            UpdateDerived();
        }

        UserType SelectedType
        {
            get { return _cmbType.SelectedItem as UserType; }
        }

        /// <summary>Usernames from every configured format (for the selected user type), as one-click choices.</summary>
        List<string> SamOptions()
        {
            if (_cfg == null) return new List<string>();
            return _cfg.BuildUsernameOptions(_txtFirst.Text.Trim(), _txtLast.Text.Trim(), SelectedType);
        }

        void UpdateSamAlternatives()
        {
            if (_cfg == null) return;
            _samAlts.SuspendLayout();
            _samAlts.Controls.Clear();
            List<string> opts = SamOptions();
            string current = _txtSam.Text.Trim();
            string fmt = string.Join(";  ", _cfg.UsernameFormatsFor(SelectedType).ToArray());
            if (opts.Count == 0)
            {
                _samAlts.Controls.Add(SmallMuted("Suggested from " + fmt + " - you can type any username."));
            }
            else
            {
                _samAlts.Controls.Add(SmallMuted(_samEdited ? "Your own username.  Suggestions:" : "Suggested - type over it to use a different one."
                                                 + (opts.Count > 1 ? "  Other formats:" : "")));
                foreach (string o in opts)
                {
                    if (!_samEdited && o.Equals(current, StringComparison.OrdinalIgnoreCase)) continue;
                    string pick = o;
                    var link = new LinkLabel
                    {
                        Text = o, AutoSize = true, Font = Theme.Small, UseMnemonic = false,
                        LinkColor = Theme.Accent, ActiveLinkColor = Theme.AccentDark, LinkBehavior = LinkBehavior.HoverUnderline,
                        Margin = new Padding(Theme.S(4), Theme.S(1), 0, 0)
                    };
                    link.LinkClicked += delegate
                    {
                        _txtSam.Text = pick;          // counts as the operator's choice (kept if the name is edited)
                        UpdateSamAlternatives();
                        _txtSam.Focus();
                        _txtSam.SelectionStart = _txtSam.Text.Length;
                    };
                    _tips.SetToolTip(link, "Use " + o);
                    _samAlts.Controls.Add(link);
                }
            }
            _tips.SetToolTip(_txtSam, "Suggested from UsernameFormat in " + AppConfig.FileName + ": " + fmt +
                                      "\nType any other username to override it. Clear the box to go back to the suggestion.");
            _samAlts.ResumeLayout();
        }

        /// <summary>A path found on existing users, written with placeholders, and whether it matches the config.</summary>
        string FoundPath(string pattern)
        {
            string ph = _ad.WithPlaceholders(pattern, _cfg);
            string s = ph;
            if (!ph.Equals(pattern, StringComparison.Ordinal)) s += "   = " + pattern;
            bool match = false;
            foreach (string cand in new[] { _cfg.ProfilePath }.Concat(_cfg.UserTypes.SelectMany(t => new[] { t.HomePath, t.ProfilePath })))
            {
                if (string.IsNullOrEmpty(cand)) continue;
                string exp = _cfg.Expand(cand, "<username>", "", "", _ad.DomainDns, "");
                if (exp.TrimEnd('\\').Equals(pattern.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) { match = true; break; }
            }
            return (match ? "✔ " : "  ") + s;
        }

        static Label SmallMuted(string text)
        {
            var l = Theme.Label(text, Theme.Small, Theme.Muted);
            l.Margin = new Padding(0, Theme.S(1), 0, 0);
            return l;
        }

        string UpnSuffix
        {
            get { return _cfg != null ? _cfg.UpnSuffixFor(SelectedType, _ad.DomainDns) : _ad.DomainDns; }
        }

        void UpdateDerived()
        {
            if (_cfg == null) return;
            string sam = _txtSam.Text.Trim();
            _lblUpn.Text = sam.Length > 0 ? sam + "@" + UpnSuffix : "";
            string mailFmt = _cfg.EmailFormatFor(SelectedType);
            if (!_mailEdited)
            {
                _suppress = true;
                _txtMail.Text = sam.Length > 0 && mailFmt.Length > 0 ? Expand(mailFmt, sam) : "";
                _suppress = false;
            }
            UpdatePreview();
            UpdateCreateState();
        }

        string Expand(string template, string sam)
        {
            UserType t = SelectedType;
            return _cfg.Expand(template, sam.Length > 0 ? sam : "<username>", _txtFirst.Text.Trim(), _txtLast.Text.Trim(),
                               _ad.DomainDns, t != null ? t.Name : "");
        }

        void UserTypeChanged()
        {
            UserType t = SelectedType;
            if (t != null && t.DefaultOU.Length > 0 && _ad.Connected)
            {
                string want = _ad.NormalizeDn(_cfg.Expand(t.DefaultOU, "", "", "", _ad.DomainDns, t.Name));
                foreach (OuItem o in _cmbOU.Items)
                    if (string.Equals(o.Dn, want, StringComparison.OrdinalIgnoreCase)) { _cmbOU.SelectedItem = o; break; }
            }
            NameChanged();   // the type may have its own username / UPN / email format
        }

        void UpdatePreview()
        {
            if (_cfg == null) return;
            UserType t = SelectedType;
            string sam = _txtSam.Text.Trim();
            _pvProfile.Text = Expand(_cfg.ProfilePathFor(t), sam);
            if (_pvProfile.Text.Length == 0) _pvProfile.Text = "(none)";
            string home = t != null ? Expand(t.HomePath, sam) : "";
            _pvHome.Text = home.Length > 0 ? _cfg.HomeDriveFor(t) + "   " + home : (t == null ? "(choose a user type)" : "(none)");
            List<string> groups = _cfg.GroupsFor(t);
            _pvGroups.Text = groups.Count > 0 ? string.Join(", ", groups.ToArray()) : "(none)";
            _chkHome.Enabled = home.Length > 0;

            var warn = new List<string>();
            foreach (string s in _cfg.MissingServerSettings())
                warn.Add("⚠  " + s + "= is not set - edit it in " + AppConfig.FileName + " (Scope & settings page).");
            if (_cfg.ActiveUserTypes.Count == 0) warn.Add("⚠  No [UserType:...] sections in the config file - no home folder or groups will be set.");
            if (_ad.Connected && _ad.UpnSuffixesRead && !_ad.IsKnownUpnSuffix(UpnSuffix))
                warn.Add("⚠  UPN suffix @" + UpnSuffix + " is not registered in this forest - users may not be able to sign in with it.");
            _pvWarn.Text = string.Join("\n", warn.ToArray());
        }

        async void CheckSam()
        {
            string sam = _txtSam.Text.Trim();
            if (sam.Length == 0 || !_ad.Connected) { _lblSamStatus.Text = " "; return; }
            string invalid = SamProblem(sam);
            if (invalid != null)
            {
                _samAvailable = false;
                _lblSamStatus.ForeColor = Theme.Danger;
                _lblSamStatus.Text = "✖  " + invalid;
                UpdateCreateState();
                return;
            }
            string upn = sam + "@" + UpnSuffix;
            bool taken; string suggestion = null;
            try
            {
                taken = await Task.Run(delegate { return _ad.SamExists(sam) || _ad.UpnExists(upn); });
                List<string> alts = SamOptions();
                if (taken) suggestion = await Task.Run(delegate { return _ad.SuggestFreeSam(sam, alts); });
            }
            catch (Exception ex)
            {
                _lblSamStatus.ForeColor = Theme.Warn;
                _lblSamStatus.Text = "Could not check availability: " + AdService.Friendly(ex);
                return;
            }
            if (_txtSam.Text.Trim() != sam) return;   // user kept typing
            _samAvailable = !taken;
            if (taken)
            {
                _lblSamStatus.ForeColor = Theme.Danger;
                _lblSamStatus.Text = "✖  Already in use" + (suggestion != null && suggestion != sam ? " - try “" + suggestion + "”" : "");
            }
            else
            {
                _lblSamStatus.ForeColor = Theme.Success;
                _lblSamStatus.Text = "✔  Available";
            }
            _pwCreate.RefreshValidation();
            UpdateCreateState();
        }

        static string SamProblem(string sam)
        {
            if (sam.Length > 20) return "Max 20 characters.";
            if (!Regex.IsMatch(sam, @"^[A-Za-z0-9._-]+$")) return "Use letters, numbers, dot, dash or underscore only.";
            if (sam.EndsWith(".")) return "Cannot end with a dot.";
            return null;
        }

        void UpdateCreateState()
        {
            UpdateEnabled();
        }

        string CreateProblem()
        {
            if (_txtFirst.Text.Trim().Length == 0) return "Enter the first name.";
            if (_txtLast.Text.Trim().Length == 0) return "Enter the last name.";
            if (_txtDisplay.Text.Trim().Length == 0) return "Enter the display name.";
            string sam = _txtSam.Text.Trim();
            if (sam.Length == 0) return "Enter a username.";
            string sp = SamProblem(sam);
            if (sp != null) return "Username: " + sp;
            if (_samAvailable == false) return "That username is already in use.";
            if (_cfg.ActiveUserTypes.Count > 0 && _cmbType.SelectedItem == null) return "Choose a user type.";
            if (_cmbOU.SelectedItem == null) return _cmbOU.Items.Count == 0 ? "You have no OUs to create users in (see Scope & settings)." : "Choose the OU to create the user in.";
            return _pwCreate.Problem();
        }

        async void DoCreate()
        {
            if (_busy > 0 || !_ad.Connected) return;
            string problem = CreateProblem();
            if (problem != null)
            {
                InfoDialog.Show(this, "Check the details", problem, null, Theme.Warn, "OK", null);
                return;
            }

            UserType t = SelectedType;
            string sam = _txtSam.Text.Trim();
            var q = new NewUserRequest
            {
                OuDn = ((OuItem)_cmbOU.SelectedItem).Dn,
                FirstName = _txtFirst.Text.Trim(),
                LastName = _txtLast.Text.Trim(),
                DisplayName = _txtDisplay.Text.Trim(),
                Sam = sam,
                Upn = sam + "@" + UpnSuffix,
                Mail = _txtMail.Text.Trim(),
                Title = _txtTitle.Text.Trim(),
                Description = _txtDesc.Text.Trim(),
                ProfilePath = _cfg.ProfilePathFor(t).Length > 0 ? Expand(_cfg.ProfilePathFor(t), sam) : "",
                HomeDirectory = t != null ? Expand(t.HomePath, sam) : "",
                HomeDrive = _cfg.HomeDriveFor(t),
                Groups = _cfg.GroupsFor(t),
                Password = _pwCreate.Password,
                MustChange = _pwCreate.MustChange,
                Enabled = _chkEnabled.Checked,
                CreateHomeFolder = _chkHome.Checked && _chkHome.Enabled,
                ExpiresEndOf = _chkCreateExpiry.Visible && _chkCreateExpiry.Checked ? (DateTime?)_dtpCreateExpiry.Value.Date : null,
                HomeFolderRights = _cfg.HomeFolderRights
            };

            string body = "Name:\t" + q.DisplayName + "\n" +
                          "Username:\t" + q.Sam + "   (" + q.Upn + ")\n" +
                          "OU:\t" + AdService.FriendlyPath(q.OuDn) + "\n" +
                          (t != null ? "User type:\t" + t.Name + "\n" : "") +
                          "Profile:\t" + (q.ProfilePath.Length > 0 ? q.ProfilePath : "(none)") + "\n" +
                          "Home:\t" + (q.HomeDirectory.Length > 0 ? q.HomeDrive + "  " + q.HomeDirectory + (q.CreateHomeFolder ? "  (will be created)" : "") : "(none)") + "\n" +
                          (q.Groups.Count > 0 ? "Groups:\t" + string.Join(", ", q.Groups.ToArray()) + "\n" : "") +
                          "Change password at next logon:\t" + (q.MustChange ? "Yes" : "No") + "\n" +
                          "Account:\t" + (q.Enabled ? "Enabled" : "Disabled") +
                          (q.ExpiresEndOf.HasValue ? "\nExpires:\tend of " + q.ExpiresEndOf.Value.ToString("dd/MM/yyyy") : "");
            if (InfoDialog.Show(this, "Create " + q.DisplayName + "?", body, null, Theme.Accent, "Create user", "Cancel") != DialogResult.OK)
                return;

            string pwSource = _pwCreate.RbDefault.Checked ? "default password" : _pwCreate.RbGenerate.Checked ? "generated password" : "typed password";
            string createDetails = "UPN " + q.Upn + "; user type " + (t != null ? t.Name : "(none)") + "; " + pwSource +
                                   "; must change at next logon=" + (q.MustChange ? "Yes" : "No") + "; enabled=" + (q.Enabled ? "Yes" : "No") +
                                   "; expires=" + (q.ExpiresEndOf.HasValue ? "end of " + q.ExpiresEndOf.Value.ToString("dd/MM/yyyy") : "never") +
                                   "; profile " + q.ProfilePath + "; home " + q.HomeDrive + " " + q.HomeDirectory +
                                   (q.Groups.Count > 0 ? "; groups " + string.Join(", ", q.Groups.ToArray()) : "") +
                                   (q.Mail.Length > 0 ? "; email " + q.Mail : "");
            CreateResult res;
            try
            {
                res = await RunAsync("Creating " + q.Sam + "…", delegate { return _ad.CreateUser(q); });
            }
            catch (Exception ex)
            {
                _log.Write("Create user", q.Sam, q.DisplayName, AdService.FriendlyPath(q.OuDn), createDetails, "FAILED: " + AdService.Friendly(ex));
                ShowError("User not created", ex);
                return;
            }

            _log.Write("Create user", q.Sam, q.DisplayName, AdService.FriendlyPath(q.OuDn),
                       createDetails + "; " + string.Join("; ", res.Done.ToArray()),
                       res.Warnings.Count == 0 ? "Success" : "Success with warnings: " + string.Join(" | ", res.Warnings.ToArray()));
            SetStatus("Created " + q.DisplayName + " (" + q.Sam + ") at " + DateTime.Now.ToString("HH:mm:ss"));

            var sb = new StringBuilder();
            foreach (string d in res.Done) sb.AppendLine("✔  " + d);
            foreach (string w in res.Warnings) sb.AppendLine("⚠  " + w);
            sb.AppendLine();
            sb.Append("Username:  " + q.Sam + "     Password:");
            InfoDialog.Show(this, res.Warnings.Count == 0 ? "User created" : "User created - with warnings",
                            sb.ToString(), q.Password, res.Warnings.Count == 0 ? Theme.Success : Theme.Warn, "Done", null);
            ClearCreateForm();
        }

        void ClearCreateForm()
        {
            _suppress = true;
            foreach (TextBox tb in new[] { _txtFirst, _txtLast, _txtDisplay, _txtSam, _txtMail, _txtTitle, _txtDesc }) tb.Text = "";
            _suppress = false;
            _samEdited = _displayEdited = _mailEdited = false;
            _samAvailable = null;
            _lblSamStatus.Text = " ";
            _chkEnabled.Checked = _cfg == null || _cfg.EnableAccount;
            _chkCreateExpiry.Checked = false;
            _chkHome.Checked = _cfg == null || _cfg.CreateHomeFolder;
            _pwCreate.Reset();
            UpdateSamAlternatives();
            UpdateDerived();
            _txtFirst.Focus();
        }

        // ==================================================================
        // Settings page
        // ==================================================================

        void BuildSettingsPage()
        {
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0), BackColor = Theme.Bg };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var info = new Card("Your access & configuration");
            _txtSettings = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None,
                BackColor = Theme.Card, Font = Theme.MonoSmall, ForeColor = Theme.Text, WordWrap = true
            };
            info.Controls.Add(_txtSettings);

            var actions = new Card("Configuration file");
            var at = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, BackColor = Theme.Card };
            var explain = Theme.Label("Settings are stored in " + AppConfig.FileName + " in the same folder as the app. " +
                                      "Copy the folder to any domain PC - it needs no installation.", Theme.Base, Theme.Muted);
            explain.MaximumSize = new Size(Theme.S(360), 0);
            _lblCfgPath = Theme.Label("", Theme.Small, Theme.Text);
            _lblCfgPath.MaximumSize = new Size(Theme.S(360), 0);
            _lblCfgPath.Margin = new Padding(0, Theme.S(4), 0, Theme.S(6));
            var bOpen = new FlatBtn("Open config file", true) { MinimumSize = new Size(Theme.S(180), Theme.S(34)) };
            var bReload = new FlatBtn("Reload config", false) { MinimumSize = new Size(Theme.S(180), Theme.S(34)) };
            var bFolder = new FlatBtn("Open app folder", false) { MinimumSize = new Size(Theme.S(180), Theme.S(34)) };
            var bLog = new FlatBtn("Open audit log", false) { MinimumSize = new Size(Theme.S(180), Theme.S(34)) };
            bOpen.Click += delegate { OpenConfig(); };
            bReload.Click += async delegate { await InitAsync(true); };
            bFolder.Click += delegate { Shell("explorer.exe", "/select,\"" + _cfg.FilePath + "\""); };
            bLog.Click += delegate
            {
                if (_log != null && _log.Path != null && File.Exists(_log.Path)) Shell(_log.Path, "");
                else InfoDialog.Show(this, "No audit log yet", "The log is created after the first password reset or new user.\n" +
                                     "Set LogFile= in the config file to change where it is written.", null, Theme.Accent, "OK", null);
            };
            var shortcuts = Theme.Label("Shortcuts:  Ctrl+F  find a user     Ctrl+N  create a user", Theme.Small, Theme.Muted);
            shortcuts.Margin = new Padding(0, Theme.S(6), 0, 0);
            var btns = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Margin = new Padding(0), BackColor = Theme.Card };
            btns.Controls.Add(bOpen, 0, 0); btns.Controls.Add(bReload, 1, 0);
            btns.Controls.Add(bFolder, 0, 1); btns.Controls.Add(bLog, 1, 1);
            at.Controls.AddRange(new Control[] { explain, _lblCfgPath, btns, shortcuts });
            actions.Controls.Add(at);

            // ---- Requirements & dependencies
            var req = new Card("Requirements & dependencies");
            var rt = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, BackColor = Theme.Card, Margin = new Padding(0) };
            rt.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            rt.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rt.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var reqBtns = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, Theme.S(6)), BackColor = Theme.Card };
            _btnReqRun = new FlatBtn("Check requirements", true);
            _btnNet = new FlatBtn("Install .NET 4.8", false);
            _btnRsat = new FlatBtn("Install RSAT (optional)", false);
            _btnReqRun.Click += async delegate { await RunRequirementsAsync(true); };
            _btnNet.Click += delegate
            {
                try { Requirements.OpenDotNetDownload(); SetStatus("Download and run the .NET Framework 4.8 Runtime installer, then restart the PC."); }
                catch (Exception ex) { ShowError("Could not open the download page", ex); }
            };
            _btnRsat.Click += delegate
            {
                if (InfoDialog.Show(this, "Install RSAT Active Directory tools?",
                        "This app does NOT need RSAT. It is only used by the older 'Change or Reset PW.ps1' script.\n\n" +
                        "Windows will ask for administrator rights, then a PowerShell window installs the tools " +
                        "(it can take several minutes and needs access to Windows Update).", null, Theme.Accent, "Install", "Cancel") != DialogResult.OK)
                    return;
                try
                {
                    if (!Requirements.InstallRsat()) { SetStatus("RSAT install cancelled."); return; }
                    if (_log != null) _log.Write("Install RSAT", "", "", "", "Started elevated RSAT AD tools install", "Started");
                    SetStatus("RSAT install started in a separate window - click 'Check requirements' when it has finished.");
                }
                catch (Exception ex) { ShowError("Could not start the RSAT install", ex); }
            };
            _tips.SetToolTip(_btnReqRun, "Checks this PC, the domain connection, and every server, share, group, OU and UPN suffix named in the config file.");
            _tips.SetToolTip(_btnNet, "Opens Microsoft's .NET Framework 4.8 download page.");
            _tips.SetToolTip(_btnRsat, "Only needed for the older PowerShell script - not for this app. Needs admin rights.");
            reqBtns.Controls.AddRange(new Control[] { _btnReqRun, _btnNet, _btnRsat });
            _rtbReq = new RichTextBox
            {
                Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Theme.Card,
                Font = Theme.Small, ForeColor = Theme.Text, DetectUrls = false, ScrollBars = RichTextBoxScrollBars.Vertical,
                Margin = new Padding(0)
            };
            rt.Controls.Add(reqBtns, 0, 0);
            rt.Controls.Add(_rtbReq, 0, 1);
            req.Controls.Add(rt);
            ShowRequirements(null);

            var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0), BackColor = Theme.Bg };
            right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(262)));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            right.Controls.Add(actions, 0, 0);
            right.Controls.Add(req, 0, 1);

            grid.Controls.Add(info, 0, 0);
            grid.Controls.Add(right, 1, 0);
            _pageSettings.Controls.Add(grid);
        }

        async void StartRequirementsCheck()
        {
            try { await RunRequirementsAsync(false); } catch { /* shown in the panel; never block startup */ }
        }

        async Task RunRequirementsAsync(bool userAsked)
        {
            if (_reqRunning || _cfg == null) return;
            _reqRunning = true;
            _btnReqRun.Enabled = false;
            _btnReqRun.Text = "Checking…";
            AppConfig cfg = _cfg; AdService ad = _ad;
            string dir = Path.GetDirectoryName(Application.ExecutablePath);
            List<RequirementCheck> results;
            try
            {
                results = await Task.Run(delegate { return Requirements.Run(cfg, ad, dir); });
            }
            catch (Exception ex)
            {
                results = new List<RequirementCheck> { new RequirementCheck { Group = "Check", State = CheckState.Fail, Name = "Requirements check failed", Detail = AdService.Friendly(ex) } };
            }
            finally
            {
                _reqRunning = false;
                _btnReqRun.Enabled = true;
                _btnReqRun.Text = "Check requirements";
            }
            ShowRequirements(results);
            int fails = results.Count(c => c.State == CheckState.Fail), warns = results.Count(c => c.State == CheckState.Warn);
            if (userAsked)
                SetStatus("Requirements check: " + fails + " problem(s), " + warns + " warning(s).");
            else if (fails > 0)
                SetStatus("⚠  Requirements check found " + fails + " problem(s) - see Scope & settings.");
        }

        void ShowRequirements(List<RequirementCheck> results)
        {
            _rtbReq.Clear();
            Action<string, Color, Font> put = delegate (string text, Color color, Font font)
            {
                _rtbReq.SelectionStart = _rtbReq.TextLength;
                _rtbReq.SelectionLength = 0;
                _rtbReq.SelectionColor = color;
                _rtbReq.SelectionFont = font;
                _rtbReq.AppendText(text);
            };

            bool needNet = Requirements.DotNetRelease() < Requirements.Net48Release;
            bool rsat = Requirements.RsatAdModuleInstalled();
            _btnNet.Visible = needNet;
            _btnRsat.Visible = !rsat;

            if (results == null)
                put("Click 'Check requirements' to test this PC, the domain and the servers, shares, groups and OUs in the config file. " +
                    "It runs automatically when the app connects.\r\n\r\n", Theme.Muted, Theme.Small);
            else
            {
                string group = null;
                foreach (RequirementCheck c in results)
                {
                    if (c.Group != group)
                    {
                        group = c.Group;
                        put((_rtbReq.TextLength > 0 ? "\r\n" : "") + group.ToUpperInvariant() + "\r\n", Theme.Text, Theme.Bold);
                    }
                    string icon; Color col;
                    switch (c.State)
                    {
                        case CheckState.Ok: icon = "✔"; col = Theme.Success; break;
                        case CheckState.Warn: icon = "⚠"; col = Theme.Warn; break;
                        case CheckState.Fail: icon = "✖"; col = Theme.Danger; break;
                        default: icon = "•"; col = Theme.Accent; break;
                    }
                    put(" " + icon + "  ", col, Theme.Small);
                    put(c.Name, Theme.Text, Theme.Small);
                    put(c.Detail.Length > 0 ? "  -  " + c.Detail + "\r\n" : "\r\n", Theme.Muted, Theme.Small);
                    if (c.Fix.Length > 0 && c.State != CheckState.Ok)
                        put("       → " + c.Fix + "\r\n", col, Theme.Small);
                }
                put("\r\nChecked " + DateTime.Now.ToString("HH:mm:ss") + "\r\n\r\n", Theme.Muted, Theme.Small);
            }
            put(Requirements.Instructions + "\r\n", Theme.Muted, Theme.Small);
            _rtbReq.SelectionStart = 0;
            _rtbReq.ScrollToCaret();
        }

        void OpenConfig()
        {
            if (_cfg == null) return;
            if (!File.Exists(_cfg.FilePath))
            {
                InfoDialog.Show(this, "Config file missing", _cfg.FilePath + " does not exist and could not be created " +
                                "(is the folder read-only?).", null, Theme.Warn, "OK", null);
                return;
            }
            Shell("notepad.exe", "\"" + _cfg.FilePath + "\"");
            SetStatus("After saving your changes in Notepad, click 'Reload config'.");
        }

        void Shell(string exe, string args)
        {
            try { Process.Start(exe, args); }
            catch (Exception ex) { ShowError("Could not open", ex); }
        }

        void RefreshSettingsText()
        {
            var sb = new StringBuilder();
            string nl = Environment.NewLine;
            if (_ad.Connected)
            {
                sb.Append("SIGNED IN AS").Append(nl);
                sb.Append("  " + _ad.UserDisplayName + "  (" + _ad.LogonName + ")").Append(nl);
                sb.Append("  " + _ad.UserDn).Append(nl).Append(nl);
                sb.Append("DOMAIN").Append(nl);
                sb.Append("  " + _ad.DomainDns + "    domain controller: " + _ad.Dc).Append(nl);
                sb.Append("  Password policy: at least " + _ad.MinPwdLength + " characters" +
                          (_ad.Complexity ? ", complexity required" : "")).Append(nl).Append(nl);

                sb.Append("PASSWORD RESET / SEARCH SCOPE   (" + _ad.ResetScopeSource + ")").Append(nl);
                sb.Append("  Only users in these OUs and their sub-OUs can be found:").Append(nl);
                if (_ad.ResetRoots.Count == 0) sb.Append("  (none - add OUs under [ResetPasswordOUs])").Append(nl);
                foreach (string dn in _ad.ResetRoots) sb.Append("   • " + AdService.FriendlyPath(dn)).Append(nl);
                sb.Append(nl);

                sb.Append("CREATE USER OUs   (" + _ad.CreateScopeSource + ")").Append(nl);
                if (_ad.CreateOUs.Count == 0) sb.Append("  (none - add OUs under [CreateUserOUs])").Append(nl);
                foreach (string dn in _ad.CreateOUs) sb.Append("   • " + AdService.FriendlyPath(dn)).Append(nl);
                sb.Append(nl);

                if (_ad.ExcludeOUs.Count > 0)
                {
                    sb.Append("EXCLUDED OUs").Append(nl);
                    foreach (string dn in _ad.ExcludeOUs) sb.Append("   • " + AdService.FriendlyPath(dn)).Append(nl);
                    sb.Append(nl);
                }
            }
            else sb.Append("NOT CONNECTED TO ACTIVE DIRECTORY").Append(nl).Append(nl);

            if (_cfg != null)
            {
                sb.Append("SETTINGS").Append(nl);
                sb.Append("  DC (<dcserver>):     " + (AppConfig.IsSet(_cfg.DCServer) ? _cfg.DCServer + "   (" + _cfg.DCServerSource + ")" : "⚠ not set and not detected - set DCServer=")).Append(nl);
                sb.Append("  Files (<fileserver>): " + (AppConfig.IsSet(_cfg.FileServer) ? _cfg.FileServer + "   (" + _cfg.FileServerSource + ")" : "⚠ not set and not detected - set FileServer=")).Append(nl);
                sb.Append("  Profile path:        " + _cfg.ProfilePath).Append(nl);
                sb.Append("  Home drive:          " + _cfg.HomeDrive).Append(nl);
                sb.Append("  Default password:    " + _cfg.DefaultPassword + "   (always forces change at next logon)").Append(nl);
                sb.Append("  Generated passwords: " + _cfg.GeneratedPasswordStyle + ", " + _cfg.GeneratedPasswordLength + "+ characters").Append(nl);
                sb.Append("  Username format:     " + string.Join("  |  ", _cfg.UsernameFormatsFor(null).ToArray()) + "   (always editable)").Append(nl);
                string dom = _ad.Connected ? _ad.DomainDns : "<domain>";
                sb.Append("  UPN suffix:          @" + _cfg.UpnSuffixFor(null, dom) +
                          (_cfg.UpnSuffix.Length == 0 ? "   (UpnSuffix= blank - using the domain name)" : "")).Append(nl);
                if (_ad.Connected && _ad.UpnSuffixesRead)
                    sb.Append("  UPN suffixes in this forest: " + string.Join(", ", _ad.UpnSuffixes.ToArray())).Append(nl);
                if (_cfg.EmailFormat.Length > 0) sb.Append("  Email format:        " + _cfg.EmailFormat).Append(nl);
                if (_cfg.CommonGroups.Count > 0) sb.Append("  Groups (all users):  " + string.Join(", ", _cfg.CommonGroups.ToArray())).Append(nl);
                sb.Append("  Disable / enable:    " + (_cfg.AllowDisable ? "on" : "off") + "     Account expiry: " + (_cfg.AllowExpiry ? "on" : "off") + "   (buttons also follow your AD permissions)").Append(nl);
                var hidden = new List<string>();
                if (!_cfg.ShowResetTab) hidden.Add("Reset password");
                if (!_cfg.ShowCreateTab) hidden.Add("Create user");
                if (!_cfg.ShowSettingsTab) hidden.Add("Scope & settings");
                sb.Append("  Hidden tabs:         " + (hidden.Count > 0 ? string.Join(", ", hidden.ToArray()) : "(none)") +
                          "     Opens on: " + (_cfg.DefaultTab == "Create" ? "Create user" : _cfg.DefaultTab == "Scope" ? "Scope & settings" : "Reset password")).Append(nl);
                sb.Append("  Audit log:           " +(_log != null && _log.Path != null ? _log.Path : "(disabled)")).Append(nl);

                sb.Append(nl).Append("USER TYPES").Append(nl);
                if (_cfg.UserTypes.Count == 0) sb.Append("  (none - add [UserType:Name] sections)").Append(nl);
                foreach (UserType t in _cfg.UserTypes)
                {
                    sb.Append("  " + t.Name + (t.Description.Length > 0 ? "  -  " + t.Description : "") + (t.Enabled ? "" : "   [Enabled=false - hidden]")).Append(nl);
                    if (!t.Enabled) continue;
                    sb.Append("     Home:      " + (t.HomePath.Length > 0 ? _cfg.HomeDriveFor(t) + "  " + t.HomePath : "(none)")).Append(nl);
                    string prof = _cfg.ProfilePathFor(t);
                    if (t.ProfilePath.Length > 0) sb.Append("     Profile:   " + (prof.Length > 0 ? prof : "(none)")).Append(nl);
                    if (t.DefaultOU.Length > 0) sb.Append("     OU:        " + (_ad.Connected ? AdService.FriendlyPath(_ad.NormalizeDn(_cfg.Expand(t.DefaultOU, "", "", "", dom, t.Name))) : t.DefaultOU)).Append(nl);
                    List<string> g = _cfg.GroupsFor(t);
                    sb.Append("     Groups:    " + (g.Count > 0 ? string.Join(", ", g.ToArray()) : "(none)")).Append(nl);
                    if (t.UsernameFormat.Length > 0) sb.Append("     Username:  " + string.Join("  |  ", _cfg.UsernameFormatsFor(t).ToArray())).Append(nl);
                    if (t.UpnSuffix.Length > 0) sb.Append("     UPN:       @" + t.UpnSuffix).Append(nl);
                    if (t.EmailFormat.Length > 0) sb.Append("     Email:     " + t.EmailFormat).Append(nl);
                }

                if (_ad.Connected && (_ad.HomePatterns.Count > 0 || _ad.ProfilePatterns.Count > 0))
                {
                    sb.Append(nl).Append("FOUND ON EXISTING USERS IN SCOPE   (" + _ad.PatternSample + " checked - handy when setting up a new site)").Append(nl);
                    sb.Append("  Home folders:").Append(nl);
                    foreach (var kv in _ad.HomePatterns.Take(4)) sb.Append("     " + FoundPath(kv.Key) + "   (" + kv.Value + " users)").Append(nl);
                    if (_ad.HomePatterns.Count == 0) sb.Append("     (none)").Append(nl);
                    sb.Append("  Profile paths:").Append(nl);
                    foreach (var kv in _ad.ProfilePatterns.Take(4)) sb.Append("     " + FoundPath(kv.Key) + "   (" + kv.Value + " users)").Append(nl);
                    if (_ad.ProfilePatterns.Count == 0) sb.Append("     (none)").Append(nl);
                    sb.Append("  Copy any of these into HomePath= / ProfilePath= in " + AppConfig.FileName + " if they suit.").Append(nl);
                }

                var warnings = new List<string>(_cfg.Warnings);
                warnings.AddRange(_ad.ScopeWarnings);
                if (warnings.Count > 0)
                {
                    sb.Append(nl).Append("WARNINGS").Append(nl);
                    foreach (string w in warnings) sb.Append("  ⚠ " + w).Append(nl);
                }
            }
            _txtSettings.Text = sb.ToString();
            _txtSettings.Select(0, 0);
        }

        // ==================================================================
        // Startup / config
        // ==================================================================

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            await InitAsync(false);
        }

        async Task InitAsync(bool reload)
        {
            string dir = Path.GetDirectoryName(Application.ExecutablePath);
            _cfg = AppConfig.LoadOrCreate(dir);
            _log = new AuditLog(_cfg, dir);
            _lblCfgPath.Text = _cfg.FilePath;
            ApplyTabVisibility(!reload);
            _pwReset.Configure(_cfg.DefaultPassword, _cfg.GeneratedPasswordStyle, _cfg.GeneratedPasswordLength);
            _pwCreate.Configure(_cfg.DefaultPassword, _cfg.GeneratedPasswordStyle, _cfg.GeneratedPasswordLength);
            _chkEnabled.Checked = _cfg.EnableAccount;
            _chkHome.Checked = _cfg.CreateHomeFolder;
            _header.Title = _cfg.OrganisationName.Length > 0 ? _cfg.OrganisationName + "  ·  AD User Manager" : "AD User Manager";
            _header.Invalidate();

            _cmbType.Items.Clear();
            foreach (UserType t in _cfg.ActiveUserTypes) _cmbType.Items.Add(t);
            if (_cmbType.Items.Count > 0) _cmbType.SelectedIndex = 0;

            AppConfig cfg = _cfg;
            AdService ad = new AdService();
            try
            {
                await RunAsync("Connecting to Active Directory…", delegate
                {
                    ad.Connect(cfg);
                    ad.ResolveScopes(cfg, delegate (string m) { SetStatusSafe(m); });
                    SetStatusSafe("Looking at existing users' home and profile folders...");
                    ad.DiscoverServers(cfg);
                });
                _ad = ad;
            }
            catch (Exception ex)
            {
                _ad = new AdService();
                _log.Write("App started", "", "", "", "", "FAILED to connect: " + AdService.Friendly(ex));
                _header.UserName = Environment.UserDomainName + "\\" + Environment.UserName;
                _header.UserDetail = "Not connected to Active Directory";
                _header.Initials = "!";
                _header.Invalidate();
                RefreshSettingsText();
                UpdateEnabled();
                SetStatus("Not connected: " + AdService.Friendly(ex));
                StartRequirementsCheck();   // shows what is missing (domain join, .NET, ports...) on Scope & settings
                ShowError("Cannot connect to Active Directory", ex);
                _tabSettings.Visible = true;   // even if hidden in the config: nothing else works, and this shows why
                ShowPage(_pageSettings);
                return;
            }

            _header.UserName = _ad.UserDisplayName;
            _header.UserDetail = _ad.LogonName + "   ·   " + _ad.DomainDns;
            _header.Initials = Initials(_ad.UserDisplayName);
            _header.Invalidate();

            _log.OperatorLogon = _ad.LogonName;
            _log.OperatorName = _ad.UserDisplayName;
            _log.Write(reload ? "Config reloaded" : "App started", "", "", "",
                       "Search scope: " + string.Join(" | ", _ad.ResetRoots.Select(AdService.FriendlyPath).ToArray()) +
                       " (" + _ad.ResetScopeSource + "); create OUs: " + _ad.CreateOUs.Count + " (" + _ad.CreateScopeSource + ")",
                       "Connected to " + _ad.Dc);

            _cmbOU.Items.Clear();
            foreach (string dn in _ad.CreateOUs) _cmbOU.Items.Add(new OuItem { Dn = dn });
            UserTypeChanged();
            if (_cmbOU.SelectedItem == null && _cmbOU.Items.Count == 1) _cmbOU.SelectedIndex = 0;

            _footScope.Text = "\U0001F512  Search is limited to " + _ad.ResetRoots.Count + " OU" + (_ad.ResetRoots.Count == 1 ? "" : "s") +
                              " (" + _ad.ResetScopeSource + "). Users outside the OU scope in " + AppConfig.FileName + " cannot be found.";
            RefreshSettingsText();
            UpdateDerived();
            UpdateEnabled();
            _pwReset.RefreshValidation();
            _pwCreate.RefreshValidation();

            if (reload)
            {
                _list.Items.Clear();
                ShowSelected(null);
                _lblCount.Text = "";
            }

            SetStatus("Ready  ·  " + _ad.ResetRoots.Count + " OU(s) in search scope, " + _ad.CreateOUs.Count + " OU(s) for new users" +
                      (reload ? "  ·  config reloaded" : ""));
            StartRequirementsCheck();

            var notes = new List<string>();
            if (_cfg.CreatedNew)
                notes.Add("A new configuration file was created:\n" + _cfg.FilePath +
                          "\n\nOpen it from 'Scope & settings' to set the OUs you manage, the UPN suffix, and the home folders, " +
                          "OUs and groups for each user type. 'Check requirements' on that page tests everything the config names.");
            if (_ad.ResetRoots.Count == 0)
                notes.Add("No OUs are in your password-reset scope, so no users can be searched. Add OUs under [ResetPasswordOUs] in the config file.");
            if (notes.Count > 0)
            {
                InfoDialog.Show(this, _cfg.CreatedNew ? "Welcome" : "Check your configuration", string.Join("\n\n", notes.ToArray()), null,
                                Theme.Accent, "OK", null);
            }
            if (_ad.ResetRoots.Count == 0) ShowPage(_pageSettings);
            else if (!reload) ShowPage(DefaultPage());   // re-focuses the opening tab after the connect / welcome dialogs
        }

        static string Initials(string name)
        {
            var parts = (name ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return "?";
            if (parts.Length == 1) return parts[0].Substring(0, 1).ToUpperInvariant();
            return (parts[0].Substring(0, 1) + parts[parts.Length - 1].Substring(0, 1)).ToUpperInvariant();
        }

        // ==================================================================
        // Busy / status helpers
        // ==================================================================

        async Task RunAsync(string message, Action work)
        {
            BeginBusy(message);
            try { await Task.Run(work); }
            finally { EndBusy(); }
        }

        async Task<T> RunAsync<T>(string message, Func<T> work)
        {
            BeginBusy(message);
            try { return await Task.Run(work); }
            finally { EndBusy(); }
        }

        void BeginBusy(string message)
        {
            _busy++;
            SetStatus(message);
            _progress.Visible = true;
            Cursor = Cursors.AppStarting;
            UpdateEnabled();
        }

        void EndBusy()
        {
            _busy = Math.Max(0, _busy - 1);
            if (_busy == 0) { _progress.Visible = false; Cursor = Cursors.Default; }
            UpdateEnabled();
        }

        void UpdateEnabled()
        {
            if (_btnSearch == null || _btnCreate == null) return;
            bool ok = _ad.Connected && _busy == 0;
            _btnSearch.Enabled = ok;
            _btnListAll.Enabled = ok;
            _btnReset.Enabled = ok && _selected != null;
            _btnCreate.Enabled = ok && _cfg != null && _cmbOU.Items.Count > 0;

            bool allowDisable = _cfg == null || _cfg.AllowDisable, allowExpiry = _cfg == null || _cfg.AllowExpiry;
            _btnToggleEnabled.Visible = allowDisable;
            _btnExpiry.Visible = allowExpiry;
            bool canUac = MayWrite("userAccountControl"), canExp = MayWrite("accountExpires");
            _btnToggleEnabled.Enabled = ok && _selected != null && canUac;
            _btnExpiry.Enabled = ok && _selected != null && canExp;
            _tips.SetToolTip(_btnToggleEnabled, _selected != null && !canUac ? "You do not have permission to enable/disable this account" : "");
            _tips.SetToolTip(_btnExpiry, _selected != null && !canExp ? "You do not have permission to change this account's expiry" : "");
            if (_chkCreateExpiry != null) _chkCreateExpiry.Visible = _dtpCreateExpiry.Visible = allowExpiry;
        }

        void SetStatus(string text) { _status.Text = text; }

        void SetStatusSafe(string text)
        {
            if (InvokeRequired) BeginInvoke(new Action<string>(SetStatus), text);
            else SetStatus(text);
        }

        void ShowError(string heading, Exception ex)
        {
            InfoDialog.Show(this, heading, AdService.Friendly(ex), null, Theme.Danger, "OK", null);
        }
    }

    /// <summary>
    /// Audit trail (CSV - opens in Excel) kept in the app folder: who did what, to whom, from which PC.
    /// Passwords are never written. Falls back to %LOCALAPPDATA%\ADUserManager if the app folder is read-only.
    /// </summary>
    public class AuditLog
    {
        public string Path { get; private set; }
        public string OperatorLogon = Environment.UserDomainName + "\\" + Environment.UserName;
        public string OperatorName = "";
        public string LastError;

        public AuditLog(AppConfig cfg, string dir)
        {
            if (string.IsNullOrWhiteSpace(cfg.LogFile)) return;
            Path = System.IO.Path.IsPathRooted(cfg.LogFile) ? cfg.LogFile : System.IO.Path.Combine(dir, cfg.LogFile);
        }

        public void Write(string action, AdUser target, string details, string result)
        {
            Write(action, target != null ? target.Sam : "", target != null ? target.Name : "",
                  target != null ? AdService.FriendlyPath(AdService.ParentDn(target.Dn)) : "", details, result);
        }

        public void Write(string action, string targetUser, string targetName, string targetOu, string details, string result)
        {
            if (Path == null) return;
            string[] row =
            {
                DateTime.Now.ToString("yyyy-MM-dd"), DateTime.Now.ToString("HH:mm:ss"), OperatorLogon, OperatorName,
                Environment.MachineName, action, targetUser, targetName, targetOu, details, result
            };
            const string header = "Date,Time,Operator,Operator name,Computer,Action,Target user,Target name,Target OU,Details,Result";
            if (TryAppend(Path, header, row)) return;

            // App folder not writable (e.g. read-only share) - keep a per-user copy instead.
            string fallback = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                                     "ADUserManager", System.IO.Path.GetFileName(Path));
            try { Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fallback)); } catch { }
            if (TryAppend(fallback, header, row)) Path = fallback;
        }

        bool TryAppend(string file, string header, string[] row)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    bool newFile = !File.Exists(file);
                    using (var fs = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    using (var w = new StreamWriter(fs, new UTF8Encoding(newFile)))
                    {
                        if (newFile) w.WriteLine(header);
                        w.WriteLine(string.Join(",", row.Select(Csv).ToArray()));
                    }
                    LastError = null;
                    return true;
                }
                catch (IOException ex) { LastError = ex.Message; System.Threading.Thread.Sleep(150); }   // file open in Excel etc.
                catch (Exception ex) { LastError = ex.Message; return false; }
            }
            return false;
        }

        static string Csv(string v)
        {
            v = (v ?? "").Replace("\r", " ").Replace("\n", " ");
            if (v.Length > 0 && "=+-@".IndexOf(v[0]) >= 0) v = "'" + v;      // stop Excel formula injection
            return v.IndexOfAny(new[] { ',', '"' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
        }
    }
}

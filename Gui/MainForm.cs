using System.Diagnostics;

namespace ModHashGui;

public class BufferedListView : ListView
{
    public BufferedListView()
    {
        DoubleBuffered = true;
    }
}

public class MainForm : Form
{
    static readonly Color Bg = Color.FromArgb(18, 18, 28);
    static readonly Color PanelColor = Color.FromArgb(28, 28, 42);
    static readonly Color Panel2 = Color.FromArgb(42, 42, 64);
    static readonly Color RowA = Color.FromArgb(28, 28, 42);
    static readonly Color RowB = Color.FromArgb(34, 34, 51);
    static readonly Color SelectColor = Color.FromArgb(58, 64, 110);
    static readonly Color TextColor = Color.FromArgb(232, 232, 242);
    static readonly Color Muted = Color.FromArgb(145, 145, 172);
    static readonly Color Accent = Color.FromArgb(110, 125, 255);
    static readonly Color Green = Color.FromArgb(70, 205, 120);
    static readonly Color Amber = Color.FromArgb(245, 185, 60);
    static readonly Color Red = Color.FromArgb(245, 85, 95);
    static readonly Color Gray = Color.FromArgb(120, 150, 190);

    readonly Font headerFont = new Font("Segoe UI", 9.5f, FontStyle.Bold);
    readonly Font regularFont = new Font("Consolas", 10f);
    readonly Font boldFont = new Font("Consolas", 10f, FontStyle.Bold);

    TextBox pathBox = null!;
    TextBox searchBox = null!;
    CheckBox recursiveBox = null!;
    Button browseButton = null!;
    Button autoButton = null!;
    Button scanButton = null!;
    Button copyButton = null!;
    Button saveButton = null!;
    Label totalValue = null!;
    Label verifiedValue = null!;
    Label unverifiedValue = null!;
    Label flaggedValue = null!;
    Label statusLabel = null!;
    ListView list = null!;
    RichTextBox details = null!;
    Panel progressTrack = null!;
    Panel progressFill = null!;
    SplitContainer split = null!;

    readonly Dictionary<string, Button> filterButtons = new();
    readonly ContextMenuStrip autoMenu = new();

    ScanReport? lastReport;
    string lastFolder = "";
    string filter = "All";
    CancellationTokenSource? cts;
    bool running;

    public MainForm()
    {
        Text = "modhash";
        ClientSize = new Size(1100, 740);
        MinimumSize = new Size(900, 620);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Bg;
        ForeColor = TextColor;
        Font = new Font("Segoe UI", 10f);

        BuildLayout();

        EnableDrop(this);
        EnableDrop(list);
        EnableDrop(details);
        EnableDrop(pathBox);
        EnableDrop(split);

        Load += (_, _) =>
        {
            try
            {
                split.SplitterDistance = (int)(split.Height * 0.66);
            }
            catch
            {
            }

            UpdateFilterButtons();
            ResizeNoteColumn();
            ShowDetails(null);
        };

        FormClosing += (_, _) => cts?.Cancel();
    }

    void BuildLayout()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 74, BackColor = PanelColor };
        header.Controls.Add(new Label { Text = "modhash", Font = new Font("Segoe UI", 20f, FontStyle.Bold), ForeColor = Accent, AutoSize = true, Location = new Point(22, 6) });
        header.Controls.Add(new Label { Text = "Mod hash verifier for Minecraft PC checks", Font = new Font("Segoe UI", 9.5f), ForeColor = Muted, AutoSize = true, Location = new Point(25, 46) });

        var accentLine = new Panel { Dock = DockStyle.Top, Height = 2, BackColor = Accent };

        pathBox = new TextBox
        {
            Font = new Font("Segoe UI", 11f),
            BackColor = Panel2,
            ForeColor = TextColor,
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "Paste the mods folder path or drop a folder here",
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Margin = new Padding(0, 0, 10, 0)
        };
        pathBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                _ = RunScanAsync();
            }
        };

        browseButton = MakeButton("Browse", Panel2, 90);
        autoButton = MakeButton("Auto-detect", Panel2, 118);
        scanButton = MakeButton("Scan", Accent, 100);
        scanButton.Margin = new Padding(0);

        browseButton.Click += (_, _) => Browse();
        autoButton.Click += (_, _) => AutoDetect();
        scanButton.Click += async (_, _) => await RunScanAsync();

        var bar = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 66,
            Padding = new Padding(20, 12, 20, 10),
            ColumnCount = 4,
            RowCount = 1,
            BackColor = Bg
        };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        bar.Controls.Add(pathBox, 0, 0);
        bar.Controls.Add(browseButton, 1, 0);
        bar.Controls.Add(autoButton, 2, 0);
        bar.Controls.Add(scanButton, 3, 0);

        var cards = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 94,
            Padding = new Padding(20, 4, 12, 8),
            ColumnCount = 4,
            RowCount = 1,
            BackColor = Bg
        };
        for (var i = 0; i < 4; i++)
        {
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
        }
        cards.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        cards.Controls.Add(MakeCard("Files checked", Accent, out totalValue), 0, 0);
        cards.Controls.Add(MakeCard("Verified on Modrinth", Green, out verifiedValue), 1, 0);
        cards.Controls.Add(MakeCard("Unverified, review these", Amber, out unverifiedValue), 2, 0);
        cards.Controls.Add(MakeCard("Flagged, unusual files", Red, out flaggedValue), 3, 0);

        var filters = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 54,
            Padding = new Padding(20, 8, 20, 4),
            BackColor = Bg,
            WrapContents = false
        };

        foreach (var name in new[] { "All", "Verified", "Unverified", "Flagged" })
        {
            var button = MakeButton(name, Panel2, 96);
            button.Height = 32;
            button.Click += (_, _) =>
            {
                filter = name;
                UpdateFilterButtons();
                RefreshList();
            };
            filterButtons[name] = button;
            filters.Controls.Add(button);
        }

        searchBox = new TextBox
        {
            Width = 230,
            Font = new Font("Segoe UI", 10.5f),
            BackColor = Panel2,
            ForeColor = TextColor,
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "Search files...",
            Margin = new Padding(12, 3, 8, 0)
        };
        searchBox.TextChanged += (_, _) => RefreshList();
        filters.Controls.Add(searchBox);

        recursiveBox = new CheckBox
        {
            Text = "Include subfolders",
            ForeColor = TextColor,
            AutoSize = true,
            Margin = new Padding(8, 7, 0, 0)
        };
        filters.Controls.Add(recursiveBox);

        list = new BufferedListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = false,
            BorderStyle = BorderStyle.None,
            BackColor = RowA,
            ForeColor = TextColor,
            OwnerDraw = true,
            Font = new Font("Segoe UI", 10f),
            SmallImageList = new ImageList { ImageSize = new Size(1, 32) }
        };
        list.Columns.Add("Status", 120);
        list.Columns.Add("File", 300);
        list.Columns.Add("Mod", 230);
        list.Columns.Add("Modrinth", 220);
        list.Columns.Add("Note", 300);
        list.DrawColumnHeader += DrawHeader;
        list.DrawItem += (_, _) => { };
        list.DrawSubItem += DrawSubItem;
        list.SizeChanged += (_, _) => ResizeNoteColumn();
        list.SelectedIndexChanged += (_, _) =>
            ShowDetails(list.SelectedItems.Count > 0 ? list.SelectedItems[0].Tag as ScanResult : null);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Copy SHA-1", null, (_, _) => CopySelected(r => r.Hash));
        menu.Items.Add("Copy file path", null, (_, _) => CopySelected(r => r.FullPath));
        menu.Items.Add("Show in Explorer", null, (_, _) => ShowInExplorer());
        list.ContextMenuStrip = menu;

        details = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = PanelColor,
            ForeColor = TextColor,
            Font = regularFont,
            DetectUrls = false
        };

        split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterWidth = 6,
            BackColor = Bg
        };
        split.Panel1.BackColor = Bg;
        split.Panel2.BackColor = Bg;
        split.Panel1.Padding = new Padding(20, 4, 20, 4);
        split.Panel2.Padding = new Padding(20, 2, 20, 10);
        split.Panel1.Controls.Add(list);
        split.Panel2.Controls.Add(details);

        progressFill = new Panel { Dock = DockStyle.Left, Width = 0, BackColor = Accent };
        progressTrack = new Panel { Dock = DockStyle.Top, Height = 4, BackColor = Panel2 };
        progressTrack.Controls.Add(progressFill);

        statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Ready. Unverified means not found on Modrinth. It is not proof of a cheat.",
            ForeColor = Muted,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };

        copyButton = MakeButton("Copy report", Panel2, 120);
        saveButton = MakeButton("Save report", Accent, 120);
        saveButton.Margin = new Padding(0);
        copyButton.Enabled = false;
        saveButton.Enabled = false;
        copyButton.Click += (_, _) => CopyReport();
        saveButton.Click += (_, _) => SaveReport();

        var bottomRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20, 8, 20, 10),
            ColumnCount = 3,
            RowCount = 1,
            BackColor = PanelColor
        };
        bottomRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        bottomRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottomRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottomRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        bottomRow.Controls.Add(statusLabel, 0, 0);
        bottomRow.Controls.Add(copyButton, 1, 0);
        bottomRow.Controls.Add(saveButton, 2, 0);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 68, BackColor = PanelColor };
        bottom.Controls.Add(bottomRow);
        bottom.Controls.Add(progressTrack);

        Controls.Add(split);
        Controls.Add(bottom);
        Controls.Add(filters);
        Controls.Add(cards);
        Controls.Add(bar);
        Controls.Add(accentLine);
        Controls.Add(header);
    }

    static Button MakeButton(string text, Color back, int width)
    {
        var button = new Button
        {
            Text = text,
            BackColor = back,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Width = width,
            Height = 34,
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 8, 0),
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = ControlPaint.Light(back, 0.25f);
        return button;
    }

    static Panel MakeCard(string caption, Color color, out Label value)
    {
        var card = new Panel { Dock = DockStyle.Fill, BackColor = PanelColor, Margin = new Padding(0, 0, 8, 0) };

        value = new Label
        {
            Dock = DockStyle.Fill,
            Text = "0",
            Font = new Font("Segoe UI", 24f, FontStyle.Bold),
            ForeColor = color,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(14, 0, 0, 0)
        };

        var captionLabel = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 26,
            Text = caption,
            ForeColor = Muted,
            Font = new Font("Segoe UI", 9f),
            TextAlign = ContentAlignment.TopLeft,
            Padding = new Padding(14, 0, 0, 0)
        };

        var stripe = new Panel { Dock = DockStyle.Left, Width = 4, BackColor = color };

        card.Controls.Add(value);
        card.Controls.Add(captionLabel);
        card.Controls.Add(stripe);
        return card;
    }

    static Color StatusColor(string status)
    {
        return status switch
        {
            "Verified" => Green,
            "Unverified" => Amber,
            "Flagged" => Red,
            _ => Gray
        };
    }

    void EnableDrop(Control control)
    {
        control.AllowDrop = true;
        control.DragEnter += (_, e) =>
        {
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Copy;
            }
        };
        control.DragDrop += OnDrop;
    }

    async void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
        {
            return;
        }

        var path = paths[0];

        if (File.Exists(path))
        {
            path = Path.GetDirectoryName(path) ?? path;
        }

        if (!Directory.Exists(path))
        {
            return;
        }

        pathBox.Text = path;
        await RunScanAsync();
    }

    void Browse()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select the mods folder",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        var current = pathBox.Text.Trim().Trim('"');

        if (Directory.Exists(current))
        {
            dialog.SelectedPath = current;
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            pathBox.Text = dialog.SelectedPath;
            _ = RunScanAsync();
        }
    }

    void AutoDetect()
    {
        var folders = ModScanner.FindModFolders();

        if (folders.Count == 0)
        {
            MessageBox.Show(this, "No mods folders were found in the common launcher locations. Paste the path, browse for it, or drop the folder on this window.", "modhash", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (folders.Count == 1)
        {
            pathBox.Text = folders[0];
            _ = RunScanAsync();
            return;
        }

        autoMenu.Items.Clear();

        foreach (var found in folders)
        {
            var folder = found;
            autoMenu.Items.Add(folder, null, (_, _) =>
            {
                pathBox.Text = folder;
                _ = RunScanAsync();
            });
        }

        autoMenu.Show(autoButton, new Point(0, autoButton.Height));
    }

    async Task RunScanAsync()
    {
        if (running)
        {
            cts?.Cancel();
            return;
        }

        var folder = pathBox.Text.Trim().Trim('"');

        if (!Directory.Exists(folder))
        {
            MessageBox.Show(this, "That folder was not found. Paste the path of a mods folder, browse for it, or drop it on this window.", "modhash", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        running = true;
        cts = new CancellationTokenSource();
        scanButton.Text = "Cancel";
        scanButton.BackColor = Red;
        copyButton.Enabled = false;
        saveButton.Enabled = false;
        statusLabel.ForeColor = Muted;
        statusLabel.Text = "Scanning...";
        SetProgress(0, 1);

        var progress = new Progress<(int Done, int Total, string Name)>(p =>
        {
            SetProgress(p.Done, p.Total);
            statusLabel.Text = p.Name == "Contacting Modrinth" ? "Contacting Modrinth..." : p.Done + "/" + p.Total + "   " + p.Name;
        });

        try
        {
            var report = await ModScanner.ScanAsync(folder, recursiveBox.Checked, progress, cts.Token);
            lastReport = report;
            lastFolder = folder;

            UpdateCards();
            RefreshList();
            ShowDetails(null);

            var verified = report.Items.Count(i => i.Status == "Verified");
            var unverified = report.Items.Count(i => i.Status == "Unverified");
            var flagged = report.Items.Count(i => i.Status == "Flagged");

            if (report.Items.Count == 0)
            {
                statusLabel.ForeColor = Amber;
                statusLabel.Text = "No jar files or archives were found in that folder.";
            }
            else if (!report.Online)
            {
                statusLabel.ForeColor = Red;
                statusLabel.Text = "Could not reach Modrinth, so nothing could be verified. Note this in the case file.";
            }
            else
            {
                statusLabel.ForeColor = Green;
                statusLabel.Text = "Done. " + verified + " verified, " + unverified + " unverified, " + flagged + " flagged. Unverified is a lead, not proof.";
            }

            copyButton.Enabled = report.Items.Count > 0;
            saveButton.Enabled = report.Items.Count > 0;
        }
        catch (OperationCanceledException)
        {
            statusLabel.ForeColor = Amber;
            statusLabel.Text = "Scan cancelled.";
        }
        catch (Exception ex)
        {
            statusLabel.ForeColor = Red;
            statusLabel.Text = "Scan failed.";
            MessageBox.Show(this, ex.Message, "modhash", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            running = false;
            scanButton.Text = "Scan";
            scanButton.BackColor = Accent;
            SetProgress(0, 1);
        }
    }

    void SetProgress(int done, int total)
    {
        var fraction = (double)Math.Min(done, total) / Math.Max(total, 1);
        progressFill.Width = (int)(progressTrack.Width * fraction);
    }

    void UpdateCards()
    {
        var items = lastReport?.Items ?? new List<ScanResult>();
        totalValue.Text = items.Count.ToString();
        verifiedValue.Text = items.Count(i => i.Status == "Verified").ToString();
        unverifiedValue.Text = items.Count(i => i.Status == "Unverified").ToString();
        flaggedValue.Text = items.Count(i => i.Status == "Flagged").ToString();
    }

    void UpdateFilterButtons()
    {
        foreach (var pair in filterButtons)
        {
            var active = pair.Key == filter;
            var color = active ? (pair.Key == "All" ? Accent : StatusColor(pair.Key)) : Panel2;
            pair.Value.BackColor = color;
            pair.Value.ForeColor = active && pair.Key == "Unverified" ? Color.Black : Color.White;
            pair.Value.FlatAppearance.MouseOverBackColor = ControlPaint.Light(color, 0.25f);
        }
    }

    void RefreshList()
    {
        list.BeginUpdate();
        list.Items.Clear();

        if (lastReport != null)
        {
            var query = searchBox.Text.Trim();

            foreach (var result in lastReport.Items)
            {
                if (filter != "All" && result.Status != filter)
                {
                    continue;
                }

                if (query != "" &&
                    !result.Name.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                    !result.Meta.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                    !result.Project.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var item = new ListViewItem(new[] { result.Status, result.Name, result.Meta, result.Project, result.Note })
                {
                    Tag = result
                };
                list.Items.Add(item);
            }
        }

        list.EndUpdate();
    }

    void ResizeNoteColumn()
    {
        if (list == null || list.Columns.Count < 5)
        {
            return;
        }

        var used = list.Columns[0].Width + list.Columns[1].Width + list.Columns[2].Width + list.Columns[3].Width;
        list.Columns[4].Width = Math.Max(160, list.ClientSize.Width - used);
    }

    void DrawHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
    {
        using (var brush = new SolidBrush(Panel2))
        {
            e.Graphics.FillRectangle(brush, e.Bounds);
        }

        var rect = new Rectangle(e.Bounds.X + 10, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? "", headerFont, rect, Muted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    void DrawSubItem(object? sender, DrawListViewSubItemEventArgs e)
    {
        if (e.Item == null || e.SubItem == null)
        {
            return;
        }

        var result = e.Item.Tag as ScanResult;
        var back = e.Item.Selected ? SelectColor : (e.ItemIndex % 2 == 0 ? RowA : RowB);

        using (var brush = new SolidBrush(back))
        {
            e.Graphics.FillRectangle(brush, e.Bounds);
        }

        var text = e.SubItem.Text;
        var color = TextColor;

        if (e.ColumnIndex == 0 && result != null)
        {
            text = "●  " + text;
            color = StatusColor(result.Status);
        }
        else if (e.ColumnIndex == 3 && result != null && result.Status == "Verified")
        {
            color = Green;
        }
        else if (e.ColumnIndex == 4)
        {
            color = Muted;
        }

        var rect = new Rectangle(e.Bounds.X + 10, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, text, list.Font, rect, color,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    void Append(string text, Color color, bool bold)
    {
        details.SelectionStart = details.TextLength;
        details.SelectionLength = 0;
        details.SelectionColor = color;
        details.SelectionFont = bold ? boldFont : regularFont;
        details.AppendText(text);
    }

    void Field(string label, string value, Color color)
    {
        if (value == "")
        {
            return;
        }

        Append(label.PadRight(14), Muted, false);
        Append(value + "\n", color, false);
    }

    void ShowDetails(ScanResult? result)
    {
        details.Clear();

        if (result == null)
        {
            Append("Select a file above to see its details.\n", Muted, false);
            return;
        }

        var color = StatusColor(result.Status);

        Append(result.Name + "\n\n", TextColor, true);
        Field("Status", result.Status, color);
        Field("Modrinth", result.Project, Green);
        Field("Mod info", result.Meta, TextColor);
        Field("Note", result.Note, Red);
        Field("SHA-1", result.Hash, TextColor);
        Field("Path", result.FullPath, TextColor);

        Append("\n", TextColor, false);

        var advice = result.Status switch
        {
            "Verified" => "The hash matches a file published on Modrinth.",
            "Unverified" => "Not found on Modrinth. This can be a private mod, a CurseForge-only mod or a modified file. Review it in a decompiler (Recaf, Luyten) before drawing any conclusion.",
            "Flagged" => "Unusual file. Check the note above, then review the file in a decompiler.",
            _ => "Modrinth could not be reached, so this file could not be checked."
        };

        Append(advice + "\n", Muted, false);
    }

    void CopySelected(Func<ScanResult, string> pick)
    {
        if (list.SelectedItems.Count > 0 && list.SelectedItems[0].Tag is ScanResult result)
        {
            var text = pick(result);

            if (text != "")
            {
                Clipboard.SetText(text);
            }
        }
    }

    void ShowInExplorer()
    {
        if (list.SelectedItems.Count > 0 && list.SelectedItems[0].Tag is ScanResult result && File.Exists(result.FullPath))
        {
            Process.Start("explorer.exe", "/select,\"" + result.FullPath + "\"");
        }
    }

    void CopyReport()
    {
        if (lastReport != null)
        {
            Clipboard.SetText(ModScanner.BuildReport(lastFolder, lastReport));
            statusLabel.ForeColor = Green;
            statusLabel.Text = "Report copied to the clipboard.";
        }
    }

    void SaveReport()
    {
        if (lastReport == null)
        {
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = "Text file (*.txt)|*.txt",
            FileName = "modhash-report-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".txt"
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            File.WriteAllText(dialog.FileName, ModScanner.BuildReport(lastFolder, lastReport));
            statusLabel.ForeColor = Green;
            statusLabel.Text = "Report saved: " + dialog.FileName;
        }
    }
}

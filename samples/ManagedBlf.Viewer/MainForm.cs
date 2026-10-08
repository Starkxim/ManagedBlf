using System.Globalization;

namespace ManagedBlf.Viewer;

internal sealed class MainForm : Form
{
    private readonly Button _open = new() { Text = "Open BLF…", AutoSize = true };
    private readonly Button _demo = new() { Text = "Load demo", AutoSize = true };
    private readonly Button _saveDemo = new() { Text = "Save demo…", AutoSize = true };
    private readonly Button _cancel = new() { Text = "Cancel", AutoSize = true, Enabled = false };
    private readonly TextBox _filter = new() { Width = 230, PlaceholderText = "Filter type, ID, data or text" };
    private readonly Label _summary = new() { AutoSize = true, MaximumSize = new Size(1150, 0), Text = "Open a BLF file or load the built-in synthetic demo." };
    private readonly Label _status = new() { AutoSize = true, Text = "Ready • relative time in seconds • text defaults to UTF-8" };
    private readonly ProgressBar _progress = new() { Dock = DockStyle.Bottom, Height = 6 };
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false, RowHeadersVisible = false, VirtualMode = true,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
        BackgroundColor = Color.White, BorderStyle = BorderStyle.None, AutoGenerateColumns = false
    };
    private readonly TextBox _details = new() { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical };
    private IReadOnlyList<PreviewRow> _rows = Array.Empty<PreviewRow>();
    private List<PreviewRow> _visible = [];
    private CancellationTokenSource? _cancellation;
    private bool _closing;

    public MainForm()
    {
        Text = "ManagedBlf — BLF Viewer";
        Width = 1250; Height = 780; MinimumSize = new Size(900, 540);
        Font = new Font("Segoe UI", 10);
        StartPosition = FormStartPosition.CenterScreen;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(16) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var toolbar = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 0, 0, 10) };
        toolbar.Controls.AddRange([_open, _demo, _saveDemo, _cancel, _filter]);
        layout.Controls.Add(toolbar, 0, 0);
        _summary.Margin = new Padding(0, 0, 0, 12);
        layout.Controls.Add(_summary, 0, 1);
        layout.Controls.Add(_grid, 0, 2);
        layout.Controls.Add(_details, 0, 3);
        layout.Controls.Add(_status, 0, 4);
        Controls.Add(layout); Controls.Add(_progress);
        AddColumn("#", 65); AddColumn("Object", 180); AddColumn("Time (s)", 130);
        AddColumn("Channel", 75); AddColumn("ID", 105); AddColumn("Dir", 50); AddColumn("DLC", 50);
        AddColumn("Data / raw body", 360); AddColumn("Flags", 120);
        _grid.CellValueNeeded += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= _visible.Count) return;
            var row = _visible[e.RowIndex];
            e.Value = e.ColumnIndex switch
            {
                0 => row.Number, 1 => row.Type, 2 => row.Time, 3 => row.Channel, 4 => row.Identifier,
                5 => row.Direction, 6 => row.Dlc, 7 => row.Data, 8 => row.Flags, _ => ""
            };
        };
        _grid.SelectionChanged += (_, _) => ShowSelection();
        _filter.TextChanged += (_, _) => ApplyFilter();
        _open.Click += async (_, _) =>
        {
            using var dialog = new OpenFileDialog { Filter = "BLF files (*.blf)|*.blf|All files (*.*)|*.*", CheckFileExists = true };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                await LoadAsync(dialog.FileName, () => new FileStream(dialog.FileName, FileMode.Open, FileAccess.Read, FileShare.Read));
        };
        _demo.Click += async (_, _) => await LoadAsync("Built-in synthetic demo", () => new MemoryStream(DemoSample.Create(), writable: false));
        _saveDemo.Click += (_, _) =>
        {
            using var dialog = new SaveFileDialog { Filter = "BLF files (*.blf)|*.blf", FileName = "managedblf-demo.blf", OverwritePrompt = true };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try { File.WriteAllBytes(dialog.FileName, DemoSample.Create()); _status.Text = "Synthetic demo saved."; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { MessageBox.Show(this, error.Message, "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        _cancel.Click += (_, _) => _cancellation?.Cancel();
    }

    private void AddColumn(string title, int width) => _grid.Columns.Add(new DataGridViewTextBoxColumn
    {
        HeaderText = title, Width = width, SortMode = DataGridViewColumnSortMode.NotSortable
    });

    private async Task LoadAsync(string name, Func<Stream> open)
    {
        if (_cancellation is not null) return;
        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        SetBusy(true);
        _rows = Array.Empty<PreviewRow>(); ApplyFilter(); _details.Clear();
        _summary.Text = name;
        _status.Text = "Scanning file…"; _progress.Value = 0;
        var progress = new Progress<(long Position, long End, ulong Count)>(p =>
        {
            if (_closing || _cancellation != cancellation) return;
            _progress.Value = p.End <= 0 ? 0 : (int)Math.Clamp(p.Position * 100d / p.End, 0, 100);
            _status.Text = $"Scanned {p.Count:N0} objects… (position is container-granular)";
        });
        try
        {
            var result = await Task.Run(() =>
            {
                using var stream = open();
                return PreviewLoader.Load(stream, cancellation.Token, progress);
            }, cancellation.Token);
            if (_closing) return;
            _rows = result.Rows; ApplyFilter();
            string start = result.Header.MeasurementStartTime.ToDateTime()?.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) ?? "unspecified";
            _summary.Text = $"{name}\nStart (timezone unspecified): {start}   •   Header count: {result.Header.ObjectCount:N0}   •   Actual objects: {result.ObjectCount:N0}";
            _status.Text = $"Scan complete in {result.Elapsed.TotalSeconds:F2}s • {result.DecodeWarnings:N0} decode warnings"
                + (result.PreviewLimited ? $" • Showing first {PreviewLoader.PreviewLimit:N0} objects; entire file scanned" : "");
            _progress.Value = 100;
        }
        catch (OperationCanceledException) { if (!_closing) _status.Text = "Cancelled. Scan is incomplete."; }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            if (!_closing)
            {
                _status.Text = "Scan failed; the file was not fully read.";
                _details.Text = error.ToString();
                MessageBox.Show(this, error.Message, "BLF read failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        finally
        {
            _cancellation = null;
            if (!_closing) SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _open.Enabled = _demo.Enabled = _saveDemo.Enabled = !busy;
        _cancel.Enabled = busy;
    }

    private void ApplyFilter()
    {
        string query = _filter.Text.Trim();
        _grid.RowCount = 0;
        _visible = _rows.Where(row => query.Length == 0 ||
            $"{row.Type} {row.Identifier} {row.Channel} {row.Data} {row.Text} {row.Warning}".Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        _grid.RowCount = _visible.Count;
        _grid.Invalidate(); ShowSelection();
    }

    private void ShowSelection()
    {
        int index = _grid.CurrentCell?.RowIndex ?? -1;
        if (index < 0 || index >= _visible.Count) { _details.Clear(); return; }
        var row = _visible[index];
        _details.Text = $"{row.Type}   #{row.Number}\r\nText: {row.Text}\r\nWarning: {row.Warning}\r\nRaw object (up to 256 bytes):\r\n{row.RawHex}";
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _closing = true;
        _cancellation?.Cancel();
        base.OnFormClosing(e);
    }
}

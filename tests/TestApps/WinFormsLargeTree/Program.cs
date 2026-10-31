namespace WinFormsLargeTree;

/// <summary>
/// A WinForms window with more than 512 UI Automation elements in front of a tool strip, the way a calendar of
/// buttons sits in front of the toolbar in many line-of-business apps. UIA3 descendant searches (FindAll /
/// FindFirst / a Subtree CacheRequest) stop after about 512 elements on this tree, so the tool strip at the end
/// is only reachable by walking the tree. The month count can be passed as the first argument (default 12).
/// With <c>grid [rows]</c> it shows <see cref="LargeGridForm"/> instead.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length > 0 && args[0] == "grid")
        {
            Application.Run(new LargeGridForm(args.Length > 1 && int.TryParse(args[1], out var rows) ? rows : 7000));
            return;
        }
        Application.Run(new LargeTreeForm(args.Length > 0 && int.TryParse(args[0], out var m) ? m : 12));
    }
}

/// <summary>
/// A virtual-mode DataGridView with thousands of rows in front of a filter text box and a tool strip, as in many
/// line-of-business forms: a UIA descendant search walks every row before it reaches the controls after the grid.
/// </summary>
internal sealed class LargeGridForm : Form
{
    public LargeGridForm(int rows)
    {
        Text = "WinForms Large Grid";
        ClientSize = new Size(800, 600);

        var grid = new DataGridView { Name = "dataGridView1", Dock = DockStyle.Fill, VirtualMode = true, ReadOnly = true, AllowUserToAddRows = false };
        grid.Columns.Add("Code", "Code");
        grid.Columns.Add("Name", "Name");
        grid.Columns.Add("Amount", "Amount");
        grid.CellValueNeeded += (_, e) => e.Value = e.ColumnIndex switch { 0 => $"C{e.RowIndex}", 1 => $"Item {e.RowIndex}", _ => e.RowIndex * 10 };
        grid.RowCount = rows;

        var filter = new Panel { Name = "filterPanel", Dock = DockStyle.Top, Height = 30 };
        filter.Controls.Add(new TextBox { Name = "filterTextBox", Width = 200 });
        var toolStrip = new ToolStrip { Name = "toolStrip1" };
        toolStrip.Items.Add(new ToolStripButton("Query") { Name = "btnQuery" });

        // UIA lists children in z-order: the grid (added first) comes before the filter panel and the tool strip
        Controls.Add(grid);
        Controls.Add(filter);
        Controls.Add(toolStrip);
    }
}

internal sealed class LargeTreeForm : Form
{
    public LargeTreeForm(int months)
    {
        Text = "WinForms Large Tree";
        ClientSize = new Size(1000, 700);

        var monthPanel = new FlowLayoutPanel { Name = "MonthPanel", Dock = DockStyle.Fill, AutoScroll = true };
        for (var m = 1; m <= months; m++)
        {
            var month = new Panel { Name = $"month{m}", Size = new Size(260, 170) };
            month.Controls.Add(new Label { Name = "MonthInfo", Text = $"Month {m}", AutoSize = true });
            for (var i = 0; i < 49; i++)
            {
                month.Controls.Add(new Button
                {
                    Name = $"day{m}_{i + 1}",
                    Text = (i + 1).ToString(),
                    Size = new Size(36, 20),
                    Location = new Point(i % 7 * 37, 20 + i / 7 * 21),
                });
            }
            monthPanel.Controls.Add(month);
        }

        var year = new ToolStripComboBox { Name = "cmbYear" };
        year.Items.AddRange(["2024", "2025", "2026"]);
        year.Text = "2026";
        var toolStrip = new ToolStrip { Name = "toolStrip1" };
        toolStrip.Items.Add(year);
        toolStrip.Items.Add(new ToolStripButton("Query") { Name = "btnQuery" });
        var top = new Panel { Name = "panelTop", Dock = DockStyle.Top, Height = 32 };
        top.Controls.Add(toolStrip);

        // UIA lists children in z-order: the month panel (added first) comes before the tool strip panel
        Controls.Add(monthPanel);
        Controls.Add(top);
    }
}

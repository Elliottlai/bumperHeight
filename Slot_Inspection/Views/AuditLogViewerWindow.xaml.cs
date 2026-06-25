using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using Slot_Inspection.Helper;

namespace Slot_Inspection.Views;

public partial class AuditLogViewerWindow : Window
{
    private static readonly string LogDir = Path.Combine(
        AppContext.BaseDirectory, "Logs", "AuditLog");

    private List<AuditLogger.Entry> _allEntries = [];

    public AuditLogViewerWindow()
    {
        InitializeComponent();
        RefreshDateList();
        LoadEntries();
    }

    /// <summary>重新列出有紀錄的日期到下拉選單；今天即使還沒有檔案也先放進去，方便剛開機就能選。</summary>
    private void RefreshDateList()
    {
        var dates = AuditLogger.ListAvailableDates();
        if (!dates.Contains(DateTime.Today))
            dates.Insert(0, DateTime.Today);

        CmbDate.ItemsSource = dates.Select(d => d.ToString("yyyy-MM-dd")).ToList();
        CmbDate.SelectedIndex = 0;
    }

    private void LoadEntries()
    {
        if (CmbDate.SelectedItem is not string dateText ||
            !DateTime.TryParse(dateText, out var date))
            date = DateTime.Today;

        _allEntries = AuditLogger.ReadEntries(date);
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        string userFilter = TxtUsernameFilter.Text.Trim();
        string keyword = TxtKeywordFilter.Text.Trim();

        var filtered = _allEntries
            .Where(e => string.IsNullOrEmpty(userFilter) ||
                        e.Username.Contains(userFilter, StringComparison.OrdinalIgnoreCase))
            .Where(e => string.IsNullOrEmpty(keyword) ||
                        e.Action.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.Time)
            .ToList();

        LstEntries.ItemsSource = filtered;
        TxtCount.Text = $"共 {filtered.Count} 筆（{_allEntries.Count} 筆中符合篩選條件）";
    }

    private void CmbDate_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        => LoadEntries();

    private void Filter_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
        => ApplyFilter();

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshDateList();
        LoadEntries();
    }

    private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (!Directory.Exists(LogDir))
            Directory.CreateDirectory(LogDir);
        Process.Start("explorer.exe", LogDir);
    }
}

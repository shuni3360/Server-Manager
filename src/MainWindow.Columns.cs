using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ServerFavorites;

public sealed record ColumnLayout(string Key, double Width);

public partial class MainWindow
{
    private DispatcherTimer? columnSaveTimer;
    private static string ColumnKey(GridViewColumn column) => (string)((GridViewColumnHeader)column.Header).Tag;

    private void InitializeColumns()
    {
        var columns = ((GridView)Servers.View).Columns;
        int index = 0;
        foreach (var saved in preferences.Columns ?? [])
        {
            var column = columns.Skip(index).FirstOrDefault(candidate => ColumnKey(candidate) == saved.Key);
            if (column == null) continue;
            columns.Move(columns.IndexOf(column), index++);
            if (double.IsFinite(saved.Width) && saved.Width >= 0) column.Width = saved.Width;
        }

        columnSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        columnSaveTimer.Tick += (_, _) => SaveColumnsIfPending();
        columns.CollectionChanged += (_, _) => QueueColumnSave();
        var width = DependencyPropertyDescriptor.FromProperty(GridViewColumn.WidthProperty, typeof(GridViewColumn));
        EventHandler changed = (_, _) => QueueColumnSave();
        foreach (var column in columns) width.AddValueChanged(column, changed);
        Closed += (_, _) =>
        {
            columnSaveTimer.Stop();
            foreach (var column in columns) width.RemoveValueChanged(column, changed);
        };
    }

    private void QueueColumnSave()
    {
        columnSaveTimer!.Stop();
        columnSaveTimer.Start();
    }

    private void SaveColumnsIfPending()
    {
        if (columnSaveTimer?.IsEnabled != true) return;
        columnSaveTimer.Stop();
        preferences.Columns = ((GridView)Servers.View).Columns.Select(column =>
            new ColumnLayout(ColumnKey(column), double.IsNaN(column.Width) ? column.ActualWidth : column.Width)).ToList();
        Save();
    }

    private static int NaturalNameComparison(string left, string right)
    {
        // Compare whole digit runs without parsing, so even long numbers cannot overflow.
        for (int i = 0, j = 0; i < left.Length && j < right.Length;)
        {
            bool leftDigit = char.IsAsciiDigit(left[i]), rightDigit = char.IsAsciiDigit(right[j]);
            int endLeft = RunEnd(left, i, leftDigit), endRight = RunEnd(right, j, rightDigit);
            int comparison;
            if (leftDigit && rightDigit)
            {
                while (i < endLeft && left[i] == '0') i++;
                while (j < endRight && right[j] == '0') j++;
                comparison = (endLeft - i).CompareTo(endRight - j);
                if (comparison == 0) comparison = left.AsSpan(i, endLeft - i).SequenceCompareTo(right.AsSpan(j, endRight - j));
            }
            else if (leftDigit != rightDigit) comparison = leftDigit ? -1 : 1;
            else
                comparison = left.AsSpan(i, endLeft - i).CompareTo(right.AsSpan(j, endRight - j), StringComparison.CurrentCultureIgnoreCase);

            if (comparison != 0) return comparison;
            i = endLeft;
            j = endRight;
            if (i == left.Length || j == right.Length) return (left.Length - i).CompareTo(right.Length - j);
        }
        return left.Length.CompareTo(right.Length);
    }

    private static int RunEnd(string text, int start, bool digit)
    {
        int end = start + 1;
        while (end < text.Length && char.IsAsciiDigit(text[end]) == digit) end++;
        return end;
    }
}

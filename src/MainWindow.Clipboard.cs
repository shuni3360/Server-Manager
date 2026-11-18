using System.Runtime.InteropServices;
using System.Windows;

namespace ServerFavorites;

public partial class MainWindow
{
    private int copyRequest;

    private async void Copy(string value)
    {
        int request = ++copyRequest;
        var data = new DataObject(DataFormats.UnicodeText, value);
        for (int attempt = 0; request == copyRequest && IsLoaded; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(data, copy: true);
                Status.Text = T("Copied");
                return;
            }
            catch (Exception error) when (error is ExternalException or UnauthorizedAccessException)
            {
                // WPF can report clipboard contention as CANT_OPEN, CANT_CLOSE or ACCESS_DENIED.
                bool retryable = error.HResult is unchecked((int)0x800401D0) or unchecked((int)0x800401D4)
                    or unchecked((int)0x80070005);
                if (!retryable || attempt >= 3)
                {
                    Status.Text = T("ClipboardError");
                    return;
                }
            }
            await Task.Delay(100);
        }
    }
}

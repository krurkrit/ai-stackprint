// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Threading;

namespace AiStackprint.Ui;

internal partial class LogViewer
{
    /// <summary>F5: read the open file again where it is (the end when following).</summary>
    public void Reload()
    {
        if (!_window)
        {
            LoadAsync(Follow);
        }
    }

    /// <summary>Leaving the Logs page: drops the file text, parsed session, search results, highlighter and controls so the memory can be freed.</summary>
    public void Release()
    {
        _search?.Cancel();
        _tail = null;
        _hits.Clear();
        _code?.Dispose();
        _code = null;
        _readable = null;
        _raw = "";
        _window = false;
        _file = null;
        _readSize = 0;
        _view.Content = null;
        _results.Content = null;
        _status.Text = "";
        _newer.IsVisible = false;
    }

    /// <summary>The open file changed on disk: reload when following, otherwise offer "New lines" (keeps your scroll position).</summary>
    public void FileChanged()
    {
        if (Follow)
        {
            ShowNew();
        }
        else if (_file != null)
        {
            _newer.IsVisible = true;
        }
    }

    private void Render(bool toEnd)
    {
        if (ShowReadable)
        {
            _view.Content = _readable?.Root;
            return;
        }
        if (_code == null)
        {
            _code = new CodeView(_file!.Path, lineNumbers: _x.S.Settings.LogLineNumbers) { KeepOnDetach = true, ScrollPastEnd = true };
            _code.Scrolled += RawScrolled;
        }

        var text = _raw;
        if (_only.IsChecked == true && Pattern() is { } p)
        {
            text = string.Join('\n', text.Split('\n').Where(p.IsMatch));
        }

        _code.Text = text;
        _view.Content = _code.Free();
        if (toEnd)
        {
            Dispatcher.UIThread.Post(_code.ScrollToEnd, DispatcherPriority.Background);
        }
    }
}

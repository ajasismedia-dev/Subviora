namespace Subviora.UiTestBot.Fixtures;

/// <summary>
/// Controlled test-window fixture providing a deterministic target window for capture and OCR automation.
/// Uses standard Win32 GDI / Windows Forms rendering for guaranteed compatibility with PrintWindow,
/// BitBlt, and Windows Graphics Capture.
/// </summary>
public sealed class TestTargetFixture : IAsyncDisposable, IDisposable
{
    private Thread? _uiThread;
    private Form? _form;
    private Label? _label;
    private readonly TaskCompletionSource<nint> _readyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public const string DefaultTitle = "Subviora Test Fixture - English Subtitles";
    public const string InitialText = "What are you doing here, Michael? We need to get to the docks before sunset.";
    public const string UpdatedText = "MISSION PASSED! RESPECT + We need to get to the docks before sunset.";

    public nint WindowHandle { get; private set; }
    public string CurrentText { get; private set; } = InitialText;

    public async Task<nint> StartAsync()
    {
        _uiThread = new Thread(RunFormThread)
        {
            IsBackground = true,
            Name = "TestTargetFixture_WinForms"
        };
        _uiThread.SetApartmentState(ApartmentState.STA);
        _uiThread.Start();

        WindowHandle = await _readyTcs.Task.ConfigureAwait(false);
        return WindowHandle;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern nint OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetThreadDesktop(nint hDesktop);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(nint hDesktop);

    private void RunFormThread()
    {
        try
        {
            nint hInput = OpenInputDesktop(0, false, 0x01FF);
            if (hInput != nint.Zero)
            {
                SetThreadDesktop(hInput);
                CloseDesktop(hInput);
            }

            _form = new Form
            {
                Text = DefaultTitle,
                Width = 800,
                Height = 450,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(50, 50),
                BackColor = Color.White,
                TopMost = true,
                ShowInTaskbar = true
            };

            _label = new Label
            {
                Text = InitialText,
                ForeColor = Color.Black,
                Font = new Font("Arial", 28, FontStyle.Bold),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter
            };

            _form.Controls.Add(_label);

            _form.Shown += (s, e) =>
            {
                WindowHandle = _form.Handle;
                _form.Activate();
                _readyTcs.TrySetResult(WindowHandle);
            };

            System.Windows.Forms.Application.Run(_form);
        }
        catch (Exception ex)
        {
            _readyTcs.TrySetException(ex);
        }
    }

    public void UpdateText(string newText)
    {
        if (_form is null || _label is null || _form.IsDisposed)
        {
            return;
        }

        CurrentText = newText;
        if (_form.InvokeRequired)
        {
            _form.Invoke(() =>
            {
                _label.Text = newText;
                _label.Invalidate();
                _label.Update();
                _label.Refresh();
                _form.Invalidate(true);
                _form.Update();
                _form.Refresh();
            });
        }
        else
        {
            _label.Text = newText;
            _label.Invalidate();
            _label.Update();
            _label.Refresh();
            _form.Invalidate(true);
            _form.Update();
            _form.Refresh();
        }
    }

    public void Close()
    {
        if (_form is not null && !_form.IsDisposed)
        {
            try
            {
                if (_form.InvokeRequired)
                {
                    _form.Invoke(_form.Close);
                }
                else
                {
                    _form.Close();
                }
            }
            catch
            {
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        Close();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        Close();
    }
}

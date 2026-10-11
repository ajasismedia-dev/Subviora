using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace Subviora.UiTestBot.Automation;

public static class UiAutomationHelper
{
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(nint hwnd, nint hdcBlt, uint nFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public static AutomationElement? WaitForElementById(AutomationElement root, string automationId, int timeoutMs = 5000)
    {
        var condition = new PropertyCondition(AutomationElement.AutomationIdProperty, automationId);
        var sw = Stopwatch.StartNew();

        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            try
            {
                var element = root.FindFirst(TreeScope.Descendants, condition);
                if (element is not null)
                {
                    return element;
                }
            }
            catch
            {
            }

            Thread.Sleep(100);
        }

        return null;
    }

    public static bool ClickButton(AutomationElement element)
    {
        try
        {
            if (!element.Current.IsEnabled)
            {
                return false;
            }

            if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var patternObj) && patternObj is InvokePattern invokePattern)
            {
                invokePattern.Invoke();
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    public static string GetElementText(AutomationElement element)
    {
        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var valObj) && valObj is ValuePattern valPattern)
        {
            return valPattern.Current.Value;
        }

        return element.Current.Name ?? string.Empty;
    }

    public static bool SelectComboBoxItem(AutomationElement comboBox, string substringMatch)
    {
        if (comboBox.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var expandObj) && expandObj is ExpandCollapsePattern expand)
        {
            expand.Expand();
            Thread.Sleep(200);

            var condition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem);
            var items = comboBox.FindAll(TreeScope.Descendants, condition);

            if (items.Count == 0)
            {
                var popupCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window);
                var windows = AutomationElement.RootElement.FindAll(TreeScope.Children, popupCondition);
                foreach (AutomationElement win in windows)
                {
                    if (win.Current.ClassName == "Popup" || string.IsNullOrEmpty(win.Current.Name))
                    {
                        var popupItems = win.FindAll(TreeScope.Descendants, condition);
                        if (popupItems.Count > 0)
                        {
                            items = popupItems;
                            break;
                        }
                    }
                }
            }

            foreach (AutomationElement item in items)
            {
                string name = item.Current.Name ?? string.Empty;
                if (name.Contains(substringMatch, StringComparison.OrdinalIgnoreCase))
                {
                    if (item.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var selObj) && selObj is SelectionItemPattern sel)
                    {
                        sel.Select();
                        Thread.Sleep(100);
                        expand.Collapse();
                        return true;
                    }
                }
            }

            expand.Collapse();
        }

        return false;
    }

    public static string? CaptureWindowScreenshot(nint hwnd, string outputPath)
    {
        if (hwnd == nint.Zero)
        {
            return null;
        }

        try
        {
            if (!GetWindowRect(hwnd, out var rect))
            {
                return null;
            }

            int width = Math.Max(1, rect.Right - rect.Left);
            int height = Math.Max(1, rect.Bottom - rect.Top);

            using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                var hdc = g.GetHdc();
                try
                {
                    PrintWindow(hwnd, hdc, 2); // PW_RENDERFULLCONTENT
                }
                finally
                {
                    g.ReleaseHdc(hdc);
                }
            }

            string? dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            bmp.Save(outputPath, ImageFormat.Png);
            return outputPath;
        }
        catch
        {
            return null;
        }
    }
}

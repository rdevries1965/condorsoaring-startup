using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace GoZCCondorLauncher;

internal static class CondorAutomation
{
    private const uint BmClick = 0x00F5;
    private const uint WmGetText = 0x000D;
    private const uint WmMouseMove = 0x0200;
    private const uint WmLButtonDown = 0x0201;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmClose = 0x0010;
    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const int VkReturn = 0x0D;
    private const int MkLButton = 0x0001;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessageText(IntPtr hWnd, uint msg, IntPtr wParam, StringBuilder lParam);

    public static async Task StartFlightAsync(AppSettings appSettings, UserSettings userSettings, CancellationToken token)
    {
        _ = Process.Start(new ProcessStartInfo(userSettings.CondorExe) { WorkingDirectory = userSettings.CondorMainFolder })
            ?? throw new InvalidOperationException("Condor kon niet worden gestart.");
        Logger.Info("Condor gestart.");
        if (!appSettings.AutomateCondorMenus) return;

        // Condor kan na het opstarten een ander proces gebruiken. Net als het oude
        // AHK-script zoeken we daarom op venstertitel en niet op het eerste proces-id.
        var main = await WaitForWindowAsync(
            t => t.Contains("Condor version", StringComparison.OrdinalIgnoreCase),
            "Condor-hoofdscherm ('Condor version ...')", appSettings.WindowTimeoutSeconds, token);
        await ClickChildAsync(main, "FREE FLIGHT", appSettings.WindowTimeoutSeconds, token, usePhysicalMouse: true);

        var planner = await WaitForWindowAsync(
            t => t.Contains("FLIGHT PLANNER", StringComparison.OrdinalIgnoreCase),
            "FLIGHT PLANNER", appSettings.WindowTimeoutSeconds, token);
        await ClickChildAsync(planner, "Start flight", appSettings.WindowTimeoutSeconds, token, usePhysicalMouse: false);
        Logger.Info("Vluchtopdracht gestart.");
    }

    public static async Task FinishFlightAndCloseCondorAsync(AppSettings appSettings, CancellationToken token)
    {
        Logger.Info("Wachten tot de vlucht wordt afgesloten en DEBRIEFING verschijnt.");
        var debriefing = await WaitForWindowAsync(
            t => t.Contains("DEBRIEFING", StringComparison.OrdinalIgnoreCase),
            "DEBRIEFING", 12 * 60 * 60, token);

        await ClickChildAsync(debriefing, "MAIN MENU", appSettings.WindowTimeoutSeconds, token, usePhysicalMouse: true);

        var main = await WaitForWindowAsync(
            t => t.Contains("Condor version", StringComparison.OrdinalIgnoreCase),
            "Condor-hoofdscherm na debriefing", appSettings.WindowTimeoutSeconds, token);
        SetForegroundWindow(main);
        PostMessage(main, WmClose, IntPtr.Zero, IntPtr.Zero);
        Logger.Info("Condor-hoofdvenster is gesloten; wachten op afsluitbevestiging.");

        var confirmation = await TryWaitForWindowAsync(
            t => t.Equals("Condor 3", StringComparison.OrdinalIgnoreCase), 15, token);
        if (confirmation != IntPtr.Zero)
        {
            SetForegroundWindow(confirmation);
            var confirmButton = FindChildByClass(confirmation, "TspSkinButton2");
            var keyTarget = confirmButton != IntPtr.Zero ? confirmButton : confirmation;
            PostMessage(keyTarget, WmKeyDown, (IntPtr)VkReturn, IntPtr.Zero);
            await Task.Delay(80, token);
            PostMessage(keyTarget, WmKeyUp, (IntPtr)VkReturn, IntPtr.Zero);
            Logger.Info("Afsluiten van Condor bevestigd met Enter.");
        }
        else
        {
            Logger.Info("Geen afsluitbevestiging gevonden; Condor lijkt direct te zijn afgesloten.");
        }
    }

    private static async Task<IntPtr> WaitForWindowAsync(
        Func<string, bool> match, string description, int timeoutSeconds, CancellationToken token)
    {
        var until = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < until)
        {
            token.ThrowIfCancellationRequested();
            IntPtr found = IntPtr.Zero;
            EnumWindows((handle, _) =>
            {
                var title = Text(handle);
                if (IsWindowVisible(handle) && match(title)) { found = handle; return false; }
                return true;
            }, IntPtr.Zero);
            if (found != IntPtr.Zero)
            {
                SetForegroundWindow(found);
                Logger.Info($"Venster gevonden: {Text(found)}");
                return found;
            }
            await Task.Delay(250, token);
        }
        throw new TimeoutException($"Het verwachte venster verscheen niet binnen {timeoutSeconds} seconden: {description}.");
    }

    private static async Task<IntPtr> TryWaitForWindowAsync(
        Func<string, bool> match, int timeoutSeconds, CancellationToken token)
    {
        var until = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < until)
        {
            token.ThrowIfCancellationRequested();
            IntPtr found = IntPtr.Zero;
            EnumWindows((handle, _) =>
            {
                if (IsWindowVisible(handle) && match(Text(handle)))
                {
                    found = handle;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            if (found != IntPtr.Zero) return found;
            await Task.Delay(250, token);
        }
        return IntPtr.Zero;
    }

    private static async Task ClickChildAsync(
        IntPtr parent, string caption, int timeoutSeconds, CancellationToken token, bool usePhysicalMouse)
    {
        var until = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < until)
        {
            IntPtr found = IntPtr.Zero;
            EnumChildWindows(parent, (handle, _) =>
            {
                if (Normalized(Text(handle)).Contains(Normalized(caption), StringComparison.OrdinalIgnoreCase))
                {
                    found = handle;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            if (found != IntPtr.Zero)
            {
                SetForegroundWindow(parent);
                if (usePhysicalMouse)
                    await PhysicalClickAsync(found, token);
                else
                    await ClickLikeAutoHotkeyAsync(found, token);
                Logger.Info($"{(usePhysicalMouse ? "Fysieke muisklik" : "Klik")} verzonden naar: {caption} (gevonden als '{Text(found)}').");
                return;
            }
            await Task.Delay(250, token);
        }
        var controls = ChildTexts(parent);
        Logger.Error($"Knop '{caption}' niet gevonden in '{Text(parent)}'. Gevonden controlteksten: {controls}");
        throw new TimeoutException($"De knop '{caption}' is niet gevonden in het Condor-venster '{Text(parent)}'. " +
            $"Controleer het logbestand voor de gevonden controlteksten.");
    }

    private static string Text(IntPtr handle)
    {
        var buffer = new StringBuilder(512);
        GetWindowText(handle, buffer, buffer.Capacity);
        if (buffer.Length == 0)
            SendMessageText(handle, WmGetText, (IntPtr)buffer.Capacity, buffer);
        return buffer.ToString().Trim();
    }

    private static async Task ClickLikeAutoHotkeyAsync(IntPtr control, CancellationToken token)
    {
        if (!GetClientRect(control, out var rect))
        {
            // Standaard Windows-knoppen ondersteunen BM_CLICK; behoud dit als fallback.
            SendMessage(control, BmClick, IntPtr.Zero, IntPtr.Zero);
            return;
        }

        var x = Math.Max(1, (rect.Right - rect.Left) / 2);
        var y = Math.Max(1, (rect.Bottom - rect.Top) / 2);
        var point = (IntPtr)((y << 16) | (x & 0xFFFF));

        // Dit bootst AHK ControlClick na. Condors TspSkinButton reageert niet op
        // BM_CLICK, maar wel op muis-down/up in het midden van de control.
        PostMessage(control, WmMouseMove, IntPtr.Zero, point);
        PostMessage(control, WmLButtonDown, (IntPtr)MkLButton, point);
        await Task.Delay(80, token);
        PostMessage(control, WmLButtonUp, IntPtr.Zero, point);
    }

    private static async Task PhysicalClickAsync(IntPtr control, CancellationToken token)
    {
        if (!GetWindowRect(control, out var rect) || rect.Right <= rect.Left || rect.Bottom <= rect.Top)
        {
            await ClickLikeAutoHotkeyAsync(control, token);
            return;
        }

        GetCursorPos(out var original);
        var x = rect.Left + (rect.Right - rect.Left) / 2;
        var y = rect.Top + (rect.Bottom - rect.Top) / 2;

        await Task.Delay(300, token); // geef SetForegroundWindow tijd om Condor te activeren
        SetCursorPos(x, y);
        await Task.Delay(120, token);
        mouse_event(MouseEventLeftDown, 0, 0, 0, UIntPtr.Zero);
        await Task.Delay(100, token);
        mouse_event(MouseEventLeftUp, 0, 0, 0, UIntPtr.Zero);
        await Task.Delay(250, token);
        SetCursorPos(original.X, original.Y);
        Logger.Info($"Fysieke klikpositie voor FREE FLIGHT: X={x}, Y={y}.");
    }

    private static string Normalized(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string ChildTexts(IntPtr parent)
    {
        var texts = new List<string>();
        EnumChildWindows(parent, (handle, _) =>
        {
            var text = Text(handle);
            if (!string.IsNullOrWhiteSpace(text)) texts.Add(text);
            return true;
        }, IntPtr.Zero);
        return texts.Count == 0 ? "(geen teksten)" : string.Join(" | ", texts.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static IntPtr FindChildByClass(IntPtr parent, string className)
    {
        IntPtr found = IntPtr.Zero;
        EnumChildWindows(parent, (handle, _) =>
        {
            var buffer = new StringBuilder(256);
            GetClassName(handle, buffer, buffer.Capacity);
            if (buffer.ToString().Equals(className, StringComparison.OrdinalIgnoreCase))
            {
                found = handle;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}

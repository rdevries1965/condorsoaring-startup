using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace GoZCCondorLauncher;

internal static class CondorAutomation
{
    private const uint WmSetFocus = 0x0007, WmGetText = 0x000D, WmClose = 0x0010, WmKeyDown = 0x0100, WmKeyUp = 0x0101, BmClick = 0x00F5;
    private const uint MouseLeftDown = 0x0002, MouseLeftUp = 0x0004;
    private const int VkReturn = 0x0D, SwRestore = 9;
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowTitle);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessageText(IntPtr hWnd, uint msg, IntPtr wParam, StringBuilder lParam);

    public static Process StartCondor(UserSettings settings, string sessionId)
    {
        var process = Process.Start(new ProcessStartInfo(settings.CondorExe) { WorkingDirectory = settings.CondorMainFolder })
            ?? throw new InvalidOperationException("Condor kon niet worden gestart.");
        Logger.SessionInfo(sessionId, $"Condor gestart; initiële process-ID: {process.Id}.");
        return process;
    }

    public static async Task AutomateStartAsync(AppSettings settings, string sessionId, CancellationToken token)
    {
        if (!settings.AutomateCondorMenus) return;
        var main = await WaitForMainMenuWindowAsync("Condor-hoofdscherm", settings.WindowTimeoutSeconds, sessionId, token);
        await ClickChildAsync(main, "FREE FLIGHT", settings.WindowTimeoutSeconds, sessionId, token);

        var planner = await WaitForCondorWindowAsync(IsPlannerTitle, "FLIGHT PLANNER", settings.WindowTimeoutSeconds, sessionId, token);
        await Task.Delay(2000, token);
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            if (!WindowStillMatches(planner, IsPlannerTitle)) return;
            Logger.SessionInfo(sessionId, $"Start flight-poging {attempt}.");
            await ClickChildAsync(planner, "Start flight", settings.WindowTimeoutSeconds, sessionId, token);
            if (await WaitUntilWindowGoneAsync(planner, IsPlannerTitle, TimeSpan.FromSeconds(6), token))
            {
                Logger.SessionInfo(sessionId, "FLIGHT PLANNER gesloten; vluchtstart geaccepteerd.");
                return;
            }
            await Task.Delay(1000, token);
        }
        throw new InvalidOperationException("Condor bleef in FLIGHT PLANNER staan nadat 'Start flight' drie keer is aangeklikt.");
    }

    public static async Task<CondorEndReason> WaitForEndAsync(string sessionId, CancellationToken token)
    {
        Logger.SessionInfo(sessionId, "Condor-sessie wordt bewaakt; wachten op DEBRIEFING of procesbeëindiging.");
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (!CondorProcessService.AnyRunning())
            {
                Logger.SessionInfo(sessionId, "Condor-processen zijn handmatig beëindigd of verdwenen.");
                return CondorEndReason.ProcessExited;
            }
            if (TryFindCondorWindow(IsDebriefingTitle, out _))
            {
                Logger.SessionInfo(sessionId, "DEBRIEFING verschenen.");
                return CondorEndReason.Debriefing;
            }
            await Task.Delay(300, token);
        }
    }

    public static async Task CloseAfterDebriefingAsync(AppSettings settings, string sessionId, CancellationToken token)
    {
        var debriefing = await WaitForCondorWindowAsync(IsDebriefingTitle, "DEBRIEFING", settings.WindowTimeoutSeconds, sessionId, token);
        await ClickChildAsync(debriefing, "MAIN MENU", settings.WindowTimeoutSeconds, sessionId, token);
        var main = await WaitForMainMenuWindowAsync("Condor-hoofdscherm na DEBRIEFING", settings.WindowTimeoutSeconds, sessionId, token);
        Activate(main); PostMessage(main, WmClose, IntPtr.Zero, IntPtr.Zero);
        Logger.SessionInfo(sessionId, "Verzoek tot sluiten van Condor-hoofdvenster verzonden.");

        var confirmation = await TryWaitForAnyWindowAsync(IsConfirmationTitle, 15, token);
        if (confirmation != IntPtr.Zero)
        {
            Logger.SessionInfo(sessionId, $"Afsluitbevestiging verschenen: '{Text(confirmation)}'.");
            Activate(confirmation);
            await Task.Delay(100, token);
            var okButton = FindChildByCaption(confirmation, "OK");
            // AHK ClassNN 'TspSkinButton2' betekent: de tweede child met
            // werkelijke classnaam 'TspSkinButton'. Alleen DIRECTE children tellen;
            // EnumChildWindows telt ook kleinkinderen en leverde daardoor soms de verkeerde knop.
            var skinButton = FindDirectClassNN(confirmation, "TspSkinButton", 2);
            var target = skinButton != IntPtr.Zero ? skinButton : okButton;

            // Dit is de letterlijke C#-variant van de werkende AHK-regel:
            // ControlSend, TspSkinButton2, {Enter}, Condor 3
            if (skinButton != IntPtr.Zero)
            {
                Logger.SessionInfo(sessionId, $"AHK-doel TspSkinButton2 gevonden: {DescribeControl(skinButton)}.");
                SendMessage(skinButton, WmSetFocus, IntPtr.Zero, IntPtr.Zero);
                PostMessage(skinButton, WmKeyDown, (IntPtr)VkReturn, IntPtr.Zero);
                await Task.Delay(100, token);
                PostMessage(skinButton, WmKeyUp, (IntPtr)VkReturn, IntPtr.Zero);
                Logger.SessionInfo(sessionId, "AHK-equivalent ControlSend naar TspSkinButton2 verzonden.");
            }

            if (await WaitUntilWindowGoneAsync(confirmation, IsConfirmationTitle, TimeSpan.FromSeconds(3), token))
            {
                Logger.SessionInfo(sessionId, "Afsluitbevestiging gesloten; AHK-equivalent is geaccepteerd.");
                return;
            }

            if (target != IntPtr.Zero)
            {
                Logger.SessionInfo(sessionId, $"Afsluitknop gevonden: {DescribeControl(target)}.");
                await PhysicalClickAsync(target, token);
                Logger.SessionInfo(sessionId, $"Fysieke klik op afsluitknop '{Text(target)}' verzonden.");
            }
            else
            {
                Logger.SessionError(sessionId, $"Geen OK/TspSkinButton2 gevonden. Controls: {ChildTexts(confirmation)}");
            }

            if (await WaitUntilWindowGoneAsync(confirmation, IsConfirmationTitle, TimeSpan.FromSeconds(2), token))
            {
                Logger.SessionInfo(sessionId, "Afsluitbevestiging gesloten; fysieke OK-klik is geaccepteerd.");
                return;
            }

            if (target != IntPtr.Zero)
            {
                PostMessage(target, BmClick, IntPtr.Zero, IntPtr.Zero);
                Logger.SessionInfo(sessionId, "Afsluitbevestiging bleef zichtbaar; BM_CLICK naar OK-control verzonden.");
            }
            if (await WaitUntilWindowGoneAsync(confirmation, IsConfirmationTitle, TimeSpan.FromSeconds(2), token))
            {
                Logger.SessionInfo(sessionId, "Afsluitbevestiging gesloten; BM_CLICK is geaccepteerd.");
                return;
            }

            // Boots de werkende AHK-regel exact na: ControlSend naar de
            // TspSkinButton-control zelf, niet naar het bovenliggende venster.
            if (target != IntPtr.Zero)
            {
                Activate(confirmation);
                PostMessage(target, WmKeyDown, (IntPtr)VkReturn, IntPtr.Zero);
                await Task.Delay(80, token);
                PostMessage(target, WmKeyUp, (IntPtr)VkReturn, IntPtr.Zero);
                Logger.SessionInfo(sessionId, "Enter rechtstreeks naar OK/TspSkinButton-control verzonden.");
            }

            if (await WaitUntilWindowGoneAsync(confirmation, IsConfirmationTitle, TimeSpan.FromSeconds(3), token))
            {
                Logger.SessionInfo(sessionId, "Afsluitbevestiging gesloten; control-Enter is geaccepteerd.");
                return;
            }

            Logger.SessionError(sessionId, "Afsluitbevestiging bleef na fysieke klik, BM_CLICK en control-Enter zichtbaar.");
        }
    }

    public static void BringRunningCondorToFront()
    {
        if (TryFindCondorWindow(_ => true, out var window)) Activate(window);
    }

    private static async Task<IntPtr> WaitForCondorWindowAsync(Func<string, bool> titleMatch, string description,
        int timeoutSeconds, string sessionId, CancellationToken token)
    {
        var until = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < until)
        {
            token.ThrowIfCancellationRequested();
            if (TryFindCondorWindow(titleMatch, out var found))
            {
                Activate(found);
                Logger.SessionInfo(sessionId, $"Venster gevonden: '{Text(found)}'.");
                return found;
            }
            await Task.Delay(250, token);
        }
        throw new TimeoutException($"Het verwachte Condor-venster verscheen niet binnen {timeoutSeconds} seconden: {description}.");
    }

    private static async Task<IntPtr> WaitForMainMenuWindowAsync(
        string description, int timeoutSeconds, string sessionId, CancellationToken token)
    {
        var until = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < until)
        {
            token.ThrowIfCancellationRequested();
            IntPtr found = IntPtr.Zero;
            EnumWindows((handle, _) =>
            {
                if (!IsWindowVisible(handle) || !BelongsToCondor(handle)) return true;
                var title = Text(handle);
                if (!IsMainTitle(title)) return true;

                // 'Condor 3' is ook de titel van een tijdelijk splashvenster zonder
                // controls. Alleen een version-titel of een venster met FREE FLIGHT
                // is het echte hoofdmenu.
                var isVersionWindow = title.Contains("Condor version", StringComparison.OrdinalIgnoreCase);
                if (!isVersionWindow && FindChildByCaption(handle, "FREE FLIGHT") == IntPtr.Zero) return true;
                found = handle;
                return false;
            }, IntPtr.Zero);

            if (found != IntPtr.Zero)
            {
                Activate(found);
                Logger.SessionInfo(sessionId, $"Condor-hoofdmenu gevonden: '{Text(found)}'.");
                return found;
            }
            await Task.Delay(250, token);
        }
        throw new TimeoutException($"Het verwachte Condor-hoofdmenu verscheen niet binnen {timeoutSeconds} seconden: {description}.");
    }

    private static bool TryFindCondorWindow(Func<string, bool> titleMatch, out IntPtr found)
    {
        var result = IntPtr.Zero;
        EnumWindows((handle, _) =>
        {
            var title = Text(handle);
            if (!IsWindowVisible(handle) || !titleMatch(title) || !BelongsToCondor(handle)) return true;
            result = handle; return false;
        }, IntPtr.Zero);
        found = result; return result != IntPtr.Zero;
    }

    private static bool BelongsToCondor(IntPtr handle)
    {
        GetWindowThreadProcessId(handle, out var processId);
        if (processId == 0 || processId == Environment.ProcessId) return false;
        try { return Process.GetProcessById((int)processId).ProcessName.Equals("Condor", StringComparison.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }

    private static bool IsMainTitle(string title) => title.Contains("Condor version", StringComparison.OrdinalIgnoreCase)
        || title.Equals("Condor 3", StringComparison.OrdinalIgnoreCase)
        || (title.StartsWith("Condor", StringComparison.OrdinalIgnoreCase) && !IsPlannerTitle(title) && !IsDebriefingTitle(title));
    private static bool IsPlannerTitle(string title) => title.Contains("FLIGHT PLANNER", StringComparison.OrdinalIgnoreCase);
    private static bool IsDebriefingTitle(string title) => title.Contains("DEBRIEFING", StringComparison.OrdinalIgnoreCase);
    private static bool IsConfirmationTitle(string title) => title.Equals("Condor 3", StringComparison.OrdinalIgnoreCase);

    private static void Activate(IntPtr window) { if (IsIconic(window)) ShowWindow(window, SwRestore); SetForegroundWindow(window); }
    private static bool WindowStillMatches(IntPtr window, Func<string, bool> match) => IsWindowVisible(window) && match(Text(window));
    private static async Task<bool> WaitUntilWindowGoneAsync(IntPtr window, Func<string, bool> match, TimeSpan timeout, CancellationToken token)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until) { if (!WindowStillMatches(window, match)) return true; await Task.Delay(250, token); }
        return !WindowStillMatches(window, match);
    }

    private static async Task<IntPtr> TryWaitForAnyWindowAsync(Func<string, bool> match, int seconds, CancellationToken token)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < until)
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows((handle, _) => { if (IsWindowVisible(handle) && match(Text(handle))) { found = handle; return false; } return true; }, IntPtr.Zero);
            if (found != IntPtr.Zero) return found;
            await Task.Delay(250, token);
        }
        return IntPtr.Zero;
    }

    private static async Task ClickChildAsync(IntPtr parent, string caption, int timeoutSeconds, string sessionId, CancellationToken token)
    {
        var until = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < until)
        {
            IntPtr found = IntPtr.Zero;
            EnumChildWindows(parent, (handle, _) =>
            {
                if (Normalize(Text(handle)).Contains(Normalize(caption), StringComparison.OrdinalIgnoreCase)) { found = handle; return false; }
                return true;
            }, IntPtr.Zero);
            if (found != IntPtr.Zero)
            {
                Activate(parent); await PhysicalClickAsync(found, token);
                Logger.SessionInfo(sessionId, $"Fysieke klik op '{caption}' (controltekst '{Text(found)}')."); return;
            }
            await Task.Delay(250, token);
        }
        Logger.SessionError(sessionId, $"Knop '{caption}' niet gevonden in '{Text(parent)}'. Controls: {ChildTexts(parent)}");
        throw new TimeoutException($"De knop '{caption}' is niet gevonden in het Condor-venster '{Text(parent)}'.");
    }

    private static async Task PhysicalClickAsync(IntPtr control, CancellationToken token)
    {
        if (!GetWindowRect(control, out var rect) || rect.Right <= rect.Left || rect.Bottom <= rect.Top)
            throw new InvalidOperationException("De actuele positie van de Condor-knop kon niet worden bepaald.");
        GetCursorPos(out var original);
        var x = rect.Left + (rect.Right - rect.Left) / 2; var y = rect.Top + (rect.Bottom - rect.Top) / 2;
        await Task.Delay(300, token); SetCursorPos(x, y); await Task.Delay(120, token);
        mouse_event(MouseLeftDown, 0, 0, 0, UIntPtr.Zero); await Task.Delay(100, token);
        mouse_event(MouseLeftUp, 0, 0, 0, UIntPtr.Zero); await Task.Delay(250, token); SetCursorPos(original.X, original.Y);
    }

    private static string Text(IntPtr handle)
    {
        var buffer = new StringBuilder(512); GetWindowText(handle, buffer, buffer.Capacity);
        if (buffer.Length == 0) SendMessageText(handle, WmGetText, (IntPtr)buffer.Capacity, buffer);
        return buffer.ToString().Trim();
    }
    private static string Normalize(string value) => string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    private static string ChildTexts(IntPtr parent)
    {
        var texts = new List<string>(); EnumChildWindows(parent, (handle, _) => { var text = Text(handle); if (text.Length > 0) texts.Add(text); return true; }, IntPtr.Zero);
        return texts.Count == 0 ? "(geen teksten)" : string.Join(" | ", texts.Distinct(StringComparer.OrdinalIgnoreCase));
    }
    private static IntPtr FindDirectClassNN(IntPtr parent, string className, int occurrence)
    {
        var found = IntPtr.Zero;
        for (var count = 0; count < occurrence; count++)
        {
            found = FindWindowEx(parent, found, className, null);
            if (found == IntPtr.Zero) break;
        }
        return found;
    }

    private static string DescribeControl(IntPtr control)
    {
        var className = new StringBuilder(256);
        GetClassName(control, className, className.Capacity);
        return GetWindowRect(control, out var rect)
            ? $"tekst='{Text(control)}', class='{className}', positie=({rect.Left},{rect.Top})-({rect.Right},{rect.Bottom})"
            : $"tekst='{Text(control)}', class='{className}', positie=onbekend";
    }

    private static IntPtr FindChildByCaption(IntPtr parent, string caption)
    {
        IntPtr found = IntPtr.Zero;
        EnumChildWindows(parent, (handle, _) =>
        {
            if (Normalize(Text(handle)).Contains(Normalize(caption), StringComparison.OrdinalIgnoreCase))
            {
                found = handle;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}

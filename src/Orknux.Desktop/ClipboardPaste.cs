using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace Orknux.Desktop;

/// <summary>
/// Puts text into the focused control by pasting it, and gives the person's
/// clipboard back afterwards.
///
/// For text a keyboard cannot be trusted with. Simulated keystrokes for
/// anything past ASCII went wrong in Windows 11 Notepad three ways in three
/// runs - "Hello from Orknux - zażółć gęślą jaźń" arrived as "Hello aźń", then
/// as "Hello from Orknux   śśślą jaźń" - while a paste is one event the control
/// takes whole. Plain ASCII is still typed, key by key, which has arrived
/// intact every time and works where pasting is refused.
///
/// Must run on an STA thread, as the clipboard requires; UiaRunner's is.
/// </summary>
internal static class ClipboardPaste
{
    /** Whether <paramref name="text"/> is better pasted than typed: anything a US keyboard would not type as it is. */
    public static bool Needed(string text) => text.Any(unit => unit > 0x7E || (unit < 0x20 && unit != '\n' && unit != '\t'));

    public static void Paste(string text)
    {
        var kept = Keep();
        Retry(() => Clipboard.SetText(text, TextDataFormat.UnicodeText));
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_V);
        Wait.UntilInputIsProcessed();
        // The target reads the clipboard when it handles the paste, which is not the moment the keys go in.
        Thread.Sleep(250);
        Retry(() =>
        {
            if (kept is null) Clipboard.Clear();
            else Clipboard.SetDataObject(kept, copy: true);
        });
    }

    /** A copy of what the clipboard holds, every format that can be read back; null when it held nothing. */
    private static DataObject? Keep()
    {
        try
        {
            var held = Clipboard.GetDataObject();
            if (held is null) return null;
            var copy = new DataObject();
            var any = false;
            foreach (var format in held.GetFormats(autoConvert: false))
            {
                try
                {
                    if (held.GetData(format, autoConvert: false) is { } data)
                    {
                        copy.SetData(format, autoConvert: false, data);
                        any = true;
                    }
                }
                catch (Exception) { /* a format its owner will not render now is left behind, not the whole clipboard */ }
            }
            return any ? copy : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /** The clipboard is shared, and another program may have it open for a moment. */
    private static void Retry(Action action)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (System.Runtime.InteropServices.ExternalException) when (attempt < 10)
            {
                Thread.Sleep(50);
            }
        }
    }
}

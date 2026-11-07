namespace Orknux.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // One per person: two would both answer the same server's commands.
        using var only = new Mutex(true, @"Local\OrknuxForWindows", out var first);
        if (!first)
        {
            MessageBox.Show("Orknux for Windows is already running - look for it by the clock.", "Orknux");
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

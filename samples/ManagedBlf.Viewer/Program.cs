namespace ManagedBlf.Viewer;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 0)
        {
            if (args.Length != 2 || args[0] != "--save-demo") return 2;
            // Explicit new file only; command-line generation never overwrites an existing log.
            try
            {
                using var file = new FileStream(args[1], FileMode.CreateNew, FileAccess.Write);
                file.Write(DemoSample.Create());
                return 0;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                return 1;
            }
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }
}

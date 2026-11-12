using System.Text;

namespace DualSenseCompanion.App;

//Captures Console output into a log file and an in-memory buffer for the log window
public static class Log
{
    private const int MaxRecentLines = 1000;

    private static readonly object _lock = new object();
    private static readonly Queue<string> _recent = new Queue<string>();
    private static StreamWriter? _file;

    public static string FilePath { get; } = Path.Combine(Settings.Directory, "log.txt");

    public static event Action<string>? LineAdded;

    public static void Initialize()
    {
        try
        {
            Directory.CreateDirectory(Settings.Directory);
            _file = new StreamWriter(FilePath, append: false, Encoding.UTF8) { AutoFlush = true };
        }
        catch
        {
            //Logging to file is optional
        }

        Console.SetOut(TextWriter.Synchronized(new LogTextWriter()));
    }

    public static void Write(string message)
    {
        string line = $"{DateTime.Now:HH:mm:ss} {message}";

        lock (_lock)
        {
            _recent.Enqueue(line);

            while (_recent.Count > MaxRecentLines)
            {
                _recent.Dequeue();
            }

            try
            {
                _file?.WriteLine(line);
            }
            catch
            {
                //Ignore file errors
            }
        }

        LineAdded?.Invoke(line);
    }

    public static string[] RecentLines()
    {
        lock (_lock)
        {
            return _recent.ToArray();
        }
    }

    private sealed class LogTextWriter : TextWriter
    {
        private readonly StringBuilder _buffer = new StringBuilder();

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            if (value == '\n')
            {
                Flush();
            }
            else if (value != '\r')
            {
                _buffer.Append(value);
            }
        }

        public override void Write(string? value)
        {
            if (value == null)
            {
                return;
            }

            foreach (char c in value)
            {
                Write(c);
            }
        }

        public override void WriteLine(string? value)
        {
            _buffer.Append(value);
            Flush();
        }

        public override void Flush()
        {
            if (_buffer.Length == 0)
            {
                return;
            }

            string line = _buffer.ToString();
            _buffer.Clear();
            Log.Write(line);
        }
    }
}

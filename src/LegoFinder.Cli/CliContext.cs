using System.Text;

namespace LegoFinder.Cli;

/// <summary>Everything a command touches outside the process, so tests can run commands in-process.</summary>
public sealed class CliContext
{
    public required TextReader In { get; init; }

    /// <summary>Data only (JSON or HTML). Never logs.</summary>
    public required TextWriter Out { get; init; }

    /// <summary>Logs, progress, warnings and errors.</summary>
    public required TextWriter Error { get; init; }

    /// <summary>True when stdin is a pipe or file rather than an interactive terminal.</summary>
    public bool InputRedirected { get; init; } = true;

    public Func<string, string?> GetEnvironmentVariable { get; init; } = Environment.GetEnvironmentVariable;

    public Func<HttpClient> HttpClientFactory { get; init; } = () => SharedHttp.Value;

    private static readonly Lazy<HttpClient> SharedHttp = new(() =>
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("legofinder/0.1");
        return http;
    });

    /// <summary>Real process streams, as UTF-8 without BOM regardless of the console code page.</summary>
    public static CliContext FromConsole()
    {
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        return new CliContext
        {
            In = new StreamReader(Console.OpenStandardInput(), utf8, detectEncodingFromByteOrderMarks: true),
            Out = new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true },
            Error = new StreamWriter(Console.OpenStandardError(), utf8) { AutoFlush = true },
            InputRedirected = Console.IsInputRedirected,
            GetEnvironmentVariable = DotEnv.ForCurrentDirectory(),
        };
    }

    public void Log(string message) => Error.WriteLine(message);
}

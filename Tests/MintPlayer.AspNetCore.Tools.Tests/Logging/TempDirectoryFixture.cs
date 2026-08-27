namespace MintPlayer.AspNetCore.Tools.Tests.Logging;

/// <summary>
/// A private, empty directory under <see cref="Path.GetTempPath"/>, deleted on dispose.
/// </summary>
/// <remarks>
/// The LoggerProviders package <i>is</i> the sink, so its tests cannot substitute a fake logger —
/// they have to write real files. Every test therefore gets its own directory so that xunit's
/// per-class parallelism cannot make two tests share a log file, and so that a leaked
/// <c>FileStream</c> in one test cannot fail another.
///
/// Usable either per-test (<c>using var temp = new TempDirectoryFixture();</c>) or per-class via
/// <c>IClassFixture&lt;TempDirectoryFixture&gt;</c>.
/// </remarks>
public sealed class TempDirectoryFixture : IDisposable
{
    public TempDirectoryFixture()
    {
        // Path.GetTempPath() keeps this off any hard-coded drive letter or separator, so the
        // suite behaves the same on the ubuntu-latest CI runner as on a Windows dev box.
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mp-logger-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    /// <summary>Full path to <paramref name="fileName"/> inside this directory. The file is not created.</summary>
    public string GetPath(string fileName) => System.IO.Path.Combine(Path, fileName);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A temp directory that outlives the run is not worth failing a test over; the OS
            // reclaims it. Swallowing here keeps a file-handle race from masking the real result.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

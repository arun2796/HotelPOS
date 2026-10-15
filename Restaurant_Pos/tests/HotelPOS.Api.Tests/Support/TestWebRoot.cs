namespace HotelPOS.Api.Tests.Support;

/// <summary>
/// After publish the browser client's files sit under the API's wwwroot/app. The test host has no wwwroot and no
/// static-web-assets manifest (tests build into a redirected OutDir), so this builds a tiny web root with the real
/// index.html from src/HotelPOS.Web/wwwroot — enough to prove the /app routing without the Blazor framework files.
/// Several test classes create their own ApiFactory in parallel, so the copy happens once per process, under a lock.
/// </summary>
internal static class TestWebRoot
{
    private static readonly object Gate = new();
    private static string? _webRoot;

    public static string Prepare()
    {
        lock (Gate)
        {
            if (_webRoot is not null)
            {
                return _webRoot;
            }

            var webRoot = Path.Combine(Path.GetTempPath(), "hotelpos-tests", $"wwwroot-{Environment.ProcessId}");
            var appFolder = Path.Combine(webRoot, "app");
            Directory.CreateDirectory(appFolder);
            File.Copy(Path.Combine(RepositoryRoot(), "src", "HotelPOS.Web", "wwwroot", "index.html"), Path.Combine(appFolder, "index.html"), overwrite: true);
            _webRoot = webRoot;
            return webRoot;
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "HotelPOS.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The tests must run inside the repository.");
    }
}

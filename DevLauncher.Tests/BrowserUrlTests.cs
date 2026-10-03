using DevLauncher.Services.Hosting;
using DevLauncher.Services;
using Xunit;

namespace DevLauncher.Tests;

public class BrowserUrlTests
{
    [Theory]
    [InlineData("   INFO  Server running on [http://127.0.0.1:8000].", "http://127.0.0.1:8000")]
    [InlineData("  ➜  Local:   http://localhost:5173/", "http://localhost:5173/")]
    [InlineData("Starting development server at http://127.0.0.1:8000/", "http://127.0.0.1:8000/")]
    [InlineData("info: Microsoft.Hosting.Lifetime[14] Now listening on: http://localhost:5021", "http://localhost:5021")]
    [InlineData("   - Local:        http://0.0.0.0:3000", "http://localhost:3000")]
    [InlineData("Listening on http://[::]:8080, press Ctrl+C", "http://localhost:8080")]
    public void LocalUrlIsReadFromServerOutput(string outputLine, string expectedUrl)
        => Assert.Equal(expectedUrl, ApplicationUrlParser.TryExtractLocalUrl(outputLine));

    [Theory]
    [InlineData("Compiled successfully in 812ms")]
    [InlineData("See https://laravel.com/docs for the documentation")]
    public void LinesWithoutLocalUrlAreIgnored(string outputLine)
        => Assert.Null(ApplicationUrlParser.TryExtractLocalUrl(outputLine));

    [Fact]
    public void ApacheUrlIsTheProjectPathInsideTheDocumentRoot()
    {
        Assert.Equal("http://localhost/templateSite/", ProjectUrlResolver.BuildApacheUrl(@"C:\xampp\htdocs\templateSite", @"C:\xampp\htdocs", 80));
        Assert.Equal("http://localhost:8080/clients/mon%20site/", ProjectUrlResolver.BuildApacheUrl(@"C:\xampp\htdocs\clients\mon site", @"C:\xampp\htdocs\", 8080));
        Assert.Null(ProjectUrlResolver.BuildApacheUrl(@"D:\dev\api", @"C:\xampp\htdocs", 80));
    }
}

using Xunit;
using Iwesun.Runtime.Web;

namespace Iwesun.Runtime.WebView2.Tests;

public sealed class AtomicHtmlRuntimeXamlDocumentWriterTests
{
	[Fact]
	public async Task WriteAsync_CreatesPrimaryAndAuxiliaryWithoutOverwrite()
	{
		var directory = Path.Combine(
			Path.GetTempPath(),
			$"iwesun-xaml-writer-{Guid.NewGuid():N}");
		try
		{
			var writer = new AtomicHtmlRuntimeXamlDocumentWriter(directory);
			var documents = new[]
			{
				new HtmlRuntimeXamlDocument(
					"document",
					"/html",
					"<Grid />"),
				new HtmlRuntimeXamlDocument(
					"/html/body/iframe",
					"/html",
					"<Grid />")
			};

			var paths = await writer.WriteAsync(documents);

			Assert.Equal(2, paths.Count);
			Assert.Equal("GeneratedSnapshot.xaml", Path.GetFileName(paths[0]));
			Assert.Equal("GeneratedSnapshot.1.xaml", Path.GetFileName(paths[1]));
			Assert.All(paths, static path => Assert.True(File.Exists(path)));
			await Assert.ThrowsAsync<IOException>(
				async () => await writer.WriteAsync(documents));
		}
		finally
		{
			if (Directory.Exists(directory))
				Directory.Delete(directory, recursive: true);
		}
	}
}

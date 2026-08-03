using System.Text;
using Iwesun.Runtime.WebView2;

namespace Iwesun.Runtime.Web;

public sealed class AtomicHtmlRuntimeXamlDocumentWriter :
	IHtmlRuntimeXamlDocumentWriter
{
	private readonly string _outputDirectory;
	private readonly string _primaryFileName;

	public AtomicHtmlRuntimeXamlDocumentWriter(
		string outputDirectory,
		string primaryFileName = "GeneratedSnapshot.xaml")
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
		ArgumentException.ThrowIfNullOrWhiteSpace(primaryFileName);
		if (!Path.GetFileName(primaryFileName).Equals(
			primaryFileName,
			StringComparison.Ordinal)
			|| !Path.GetExtension(primaryFileName).Equals(
				".xaml",
				StringComparison.OrdinalIgnoreCase))
		{
			throw new ArgumentException(
				"The primary XAML file name must be a simple .xaml name.",
				nameof(primaryFileName));
		}
		_outputDirectory = Path.GetFullPath(outputDirectory);
		_primaryFileName = primaryFileName;
	}

	public async ValueTask<IReadOnlyList<string>> WriteAsync(
		IReadOnlyList<HtmlRuntimeXamlDocument> documents,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(documents);
		if (documents.Count == 0)
			throw new InvalidDataException("No XAML documents were supplied.");
		Directory.CreateDirectory(_outputDirectory);
		var targets = documents
			.Select((_, index) => Path.Combine(
				_outputDirectory,
				index == 0
					? _primaryFileName
					: AuxiliaryFileName(index)))
			.ToArray();
		if (targets.Any(File.Exists))
		{
			throw new IOException(
				"The XAML writer refuses to overwrite an existing result.");
		}
		var staged = targets
			.Select(target => $"{target}.tmp-{Guid.NewGuid():N}")
			.ToArray();
		try
		{
			for (var index = 0; index < documents.Count; index++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				ValidateDocument(documents[index]);
				await using var stream = new FileStream(
					staged[index],
					FileMode.CreateNew,
					FileAccess.Write,
					FileShare.None,
					4096,
					FileOptions.Asynchronous | FileOptions.WriteThrough);
				await using var writer = new StreamWriter(
					stream,
					new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
				await writer.WriteAsync(
					documents[index].Xaml.AsMemory(),
					cancellationToken);
				await writer.FlushAsync(cancellationToken);
			}
			for (var index = 0; index < targets.Length; index++)
				File.Move(staged[index], targets[index]);
			return targets;
		}
		catch
		{
			foreach (var path in staged)
			{
				if (File.Exists(path))
					File.Delete(path);
			}
			throw;
		}
	}

	private string AuxiliaryFileName(int index)
	{
		var stem = Path.GetFileNameWithoutExtension(_primaryFileName);
		return $"{stem}.{index}.xaml";
	}

	private static void ValidateDocument(HtmlRuntimeXamlDocument document)
	{
		ArgumentNullException.ThrowIfNull(document);
		ArgumentException.ThrowIfNullOrWhiteSpace(document.DocumentScope);
		ArgumentException.ThrowIfNullOrWhiteSpace(document.RootXPath);
		ArgumentException.ThrowIfNullOrWhiteSpace(document.Xaml);
		if (!document.RootXPath.StartsWith("/", StringComparison.Ordinal)
			|| !document.Xaml.TrimStart().StartsWith("<", StringComparison.Ordinal))
		{
			throw new InvalidDataException(
				"The HTML runtime XAML document is malformed.");
		}
	}
}

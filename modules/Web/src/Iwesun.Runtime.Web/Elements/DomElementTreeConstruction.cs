namespace Iwesun.Runtime.Web;
using Iwesun.Runtime.Diagnostics;

public sealed record DomElementConstructionNode(
	string DocumentScope,
	string XPath,
	string TagName,
	string? ParentXPath,
	IReadOnlyList<string> ChildXPaths,
	string? LeftSiblingXPath,
	string? RightSiblingXPath)
{
	public IReadOnlyList<string> AttributeNames { get; init; } = [];

	public IReadOnlyDictionary<string, string> AttributeValues { get; init; } =
		new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	public int NodeId { get; init; }

	public int BackendNodeId { get; init; }

	public string OwnText { get; init; } = string.Empty;

	public int OwnTextElementInsertionIndex { get; init; } = -1;

	public string TextContent { get; init; } = string.Empty;

	public IReadOnlyList<DomElementRuntimePropertyDefinition>
		AdditionalRuntimeProperties { get; init; } = [];
}

public sealed record DomElementRuntimePropertyDefinition(
	string Name,
	ElementSlotCategory Category,
	ElementEvidenceKind EvidenceKind);

public sealed record DomElementTreeConstructionResult(
	DomElement HtmlRootElement,
	IReadOnlyList<DomElement> DocumentRoots,
	IReadOnlyList<DomElement> Elements);

public static class DomElementTreeBuilder
{
	public static DomElementTreeConstructionResult Build(
		string primaryDocumentScope,
		IReadOnlyList<DomElementConstructionNode> nodes,
		int? maximumHierarchyLevel = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(primaryDocumentScope);
		ArgumentNullException.ThrowIfNull(nodes);
		if (maximumHierarchyLevel is <= 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(maximumHierarchyLevel),
				"Maximum hierarchy level must be positive.");
		}
		if (nodes.Count == 0)
			throw new InvalidDataException("The live DOM tree contains no elements.");
		foreach (var node in nodes)
			ValidateNode(node);
		var byIdentity = nodes.ToDictionary(
			static node => new NodeIdentity(node.DocumentScope, node.XPath));
		if (byIdentity.Count != nodes.Count)
		{
			throw new InvalidDataException(
				"The live DOM tree contains duplicate document-scope/XPath identities.");
		}
		ValidateRelationships(nodes, byIdentity);
		if (maximumHierarchyLevel is { } limit)
		{
			nodes = ProjectHierarchy(
				primaryDocumentScope,
				nodes,
				byIdentity,
				limit);
			byIdentity = nodes.ToDictionary(
				static node => new NodeIdentity(
					node.DocumentScope,
					node.XPath));
			ValidateRelationships(nodes, byIdentity);
		}
		var elements = nodes.ToDictionary(
			static node => new NodeIdentity(node.DocumentScope, node.XPath),
			static node => CreateElement(node));
		var roots = new List<DomElement>();
		foreach (var node in nodes)
		{
			var element = elements[new(node.DocumentScope, node.XPath)];
			if (node.ParentXPath is null)
			{
				roots.Add(element);
				continue;
			}
			var parent = elements[new(node.DocumentScope, node.ParentXPath)];
			parent.AddChild(element);
		}
		foreach (var root in roots.Where(root =>
			!root.DocumentScope.Equals(
				primaryDocumentScope,
				StringComparison.Ordinal)))
		{
			var ownerIdentity = EmbeddingOwnerIdentity(root.DocumentScope);
			var frameOwner = elements
				.Where(pair => ElementIdentity(
					primaryDocumentScope,
					pair.Key).Equals(
						ownerIdentity,
						StringComparison.Ordinal))
				.Select(static pair => pair.Value)
				.SingleOrDefault();
			if (frameOwner is null)
			{
				throw new InvalidDataException(
					$"Detached DOM scope '{root.DocumentScope}' has no "
						+ "matching embedding owner in the DOM element tree.");
			}
			root.AttachEmbeddingOwner(
				frameOwner,
				root.DocumentScope.EndsWith(
					"#shadow-root",
					StringComparison.Ordinal)
						? DomEmbeddingOwnerKind.ShadowHost
						: DomEmbeddingOwnerKind.Iframe);
		}
		foreach (var root in roots)
			root.MaximumHierarchyLevel = maximumHierarchyLevel;
		var orderedElements = nodes.Select(node =>
			elements[new(node.DocumentScope, node.XPath)]).ToArray();
		if (RuntimeOutput.Enabled)
		{
			var actualMaximumLevel = orderedElements.Max(
				static element => element.HierarchyLevel);
			for (var index = 0; index < orderedElements.Length; index++)
			{
				var element = orderedElements[index];
				var progress = new HtmlElementTraversalProgress(
					"BuildTree",
					index + 1,
					orderedElements.Length,
					element.HierarchyLevel,
					actualMaximumLevel,
					element.DocumentScope,
					element.XPath);
				RuntimeOutput.TracePoint(
					"log.pipeline",
					"pipeline",
					"log.html-reconstruction.tree-progress",
					$"DOM tree {progress.ProcessedElements}/"
						+ $"{progress.TotalElements} "
						+ $"L{progress.CurrentHierarchyLevel}/"
						+ $"{progress.MaximumHierarchyLevel} "
						+ $"{progress.Percentage:F2}%",
					progress);
			}
		}
		var primaryRoots = roots
			.Where(root => root.DocumentScope.Equals(
				primaryDocumentScope,
				StringComparison.Ordinal))
			.ToArray();
		var htmlRoot = primaryRoots.FirstOrDefault(static root =>
				root.TagName.Equals("html", StringComparison.OrdinalIgnoreCase))
			?? (primaryRoots.Length == 1
				? primaryRoots[0]
				: throw new InvalidDataException(
					$"Document scope '{primaryDocumentScope}' does not have "
					+ "one unambiguous HTML root."));
		return new(
			htmlRoot,
			roots,
			orderedElements);
	}

	private static IReadOnlyList<DomElementConstructionNode> ProjectHierarchy(
		string primaryDocumentScope,
		IReadOnlyList<DomElementConstructionNode> nodes,
		IReadOnlyDictionary<NodeIdentity, DomElementConstructionNode> byIdentity,
		int maximumHierarchyLevel)
	{
		var depths = new Dictionary<NodeIdentity, int>();
		var visiting = new HashSet<NodeIdentity>();
		int ResolveDepth(DomElementConstructionNode node)
		{
			var identity = new NodeIdentity(node.DocumentScope, node.XPath);
			if (depths.TryGetValue(identity, out var existing))
				return existing;
			if (!visiting.Add(identity))
			{
				throw new InvalidDataException(
					$"DOM hierarchy contains a parent cycle at "
						+ $"{node.DocumentScope}::{node.XPath}.");
			}
			int depth;
			if (node.ParentXPath is not null)
			{
				depth = ResolveDepth(byIdentity[
					new(node.DocumentScope, node.ParentXPath)]) + 1;
			}
			else
			{
				var ownerIdentity = EmbeddingOwnerIdentity(node.DocumentScope);
				var frameOwner = byIdentity
					.Where(pair => ElementIdentity(
						primaryDocumentScope,
						pair.Key).Equals(
							ownerIdentity,
							StringComparison.Ordinal))
					.Select(static pair => pair.Value)
					.SingleOrDefault();
				depth = frameOwner is null
					? 1
					: ResolveDepth(frameOwner) + 1;
			}
			visiting.Remove(identity);
			depths.Add(identity, depth);
			return depth;
		}
		foreach (var node in nodes)
			ResolveDepth(node);
		var selected = nodes
			.Where(node => depths[
				new(node.DocumentScope, node.XPath)]
				<= maximumHierarchyLevel)
			.Select(static node => new NodeIdentity(
				node.DocumentScope,
				node.XPath))
			.ToHashSet();
		return nodes
			.Where(node => selected.Contains(
				new(node.DocumentScope, node.XPath)))
			.Select(node => node with
			{
				ChildXPaths = node.ChildXPaths
					.Where(path => selected.Contains(
						new(node.DocumentScope, path)))
					.ToArray(),
				LeftSiblingXPath = node.LeftSiblingXPath is { } left
					&& selected.Contains(new(node.DocumentScope, left))
						? left
						: null,
				RightSiblingXPath = node.RightSiblingXPath is { } right
					&& selected.Contains(new(node.DocumentScope, right))
						? right
						: null
			})
			.ToArray();
	}

	private static string ElementIdentity(
		string primaryDocumentScope,
		NodeIdentity identity) =>
		identity.DocumentScope.Equals(
			primaryDocumentScope,
			StringComparison.Ordinal)
				? identity.XPath
				: $"{identity.DocumentScope}::{identity.XPath}";

	private static string EmbeddingOwnerIdentity(string documentScope) =>
		documentScope.EndsWith("#shadow-root", StringComparison.Ordinal)
			? documentScope[..^"#shadow-root".Length]
			: documentScope;

	private static void ValidateNode(DomElementConstructionNode node)
	{
		ArgumentNullException.ThrowIfNull(node);
		ArgumentException.ThrowIfNullOrWhiteSpace(node.DocumentScope);
		ArgumentException.ThrowIfNullOrWhiteSpace(node.XPath);
		ArgumentException.ThrowIfNullOrWhiteSpace(node.TagName);
		ArgumentNullException.ThrowIfNull(node.ChildXPaths);
		if (!node.XPath.StartsWith("/", StringComparison.Ordinal)
			|| node.ParentXPath is not null
				&& !node.ParentXPath.StartsWith("/", StringComparison.Ordinal)
			|| node.ChildXPaths.Any(static path =>
				string.IsNullOrWhiteSpace(path)
					|| !path.StartsWith("/", StringComparison.Ordinal))
			|| node.ChildXPaths.Distinct(StringComparer.Ordinal).Count()
				!= node.ChildXPaths.Count)
		{
			throw new InvalidDataException(
				"DOM construction nodes require absolute, unique XPath relationships.");
		}
	}

	private static void ValidateRelationships(
		IReadOnlyList<DomElementConstructionNode> nodes,
		IReadOnlyDictionary<NodeIdentity, DomElementConstructionNode> byIdentity)
	{
		foreach (var node in nodes)
		{
			if (node.ParentXPath is not null)
			{
				var parentIdentity = new NodeIdentity(
					node.DocumentScope,
					node.ParentXPath);
				if (!byIdentity.TryGetValue(parentIdentity, out var parent)
					|| !parent.ChildXPaths.Contains(
						node.XPath,
						StringComparer.Ordinal))
				{
					throw new InvalidDataException(
						$"DOM parent/child relationship is incomplete for "
						+ $"{node.DocumentScope}::{node.XPath}.");
				}
			}
			foreach (var childPath in node.ChildXPaths)
			{
				var childIdentity = new NodeIdentity(
					node.DocumentScope,
					childPath);
				if (!byIdentity.TryGetValue(childIdentity, out var child)
					|| !string.Equals(
						child.ParentXPath,
						node.XPath,
						StringComparison.Ordinal))
				{
					throw new InvalidDataException(
						$"DOM child/parent relationship is incomplete for "
						+ $"{node.DocumentScope}::{childPath}.");
				}
			}
		}
	}

	private static DomElement CreateElement(DomElementConstructionNode node)
	{
		var element = HtmlDomElementTypeCatalog.Create(
			node.TagName,
			new(
				node.DocumentScope,
				node.XPath,
				node.ParentXPath,
				node.ChildXPaths,
				node.LeftSiblingXPath,
				node.RightSiblingXPath,
				node.OwnText,
				node.TextContent,
				node.NodeId,
				node.BackendNodeId,
				node.OwnTextElementInsertionIndex)
			{
				CapturedAttributeValues = node.AttributeValues
			});
		foreach (var attributeName in node.AttributeNames)
		{
			switch (element)
			{
				case HtmlDomElementDefinition html:
					html.AddExtensionAttribute(attributeName);
					break;
				case SvgDomElementDefinition svg:
					svg.AddExtensionAttribute(attributeName);
					break;
			}
			if (HtmlEventHandlerCatalog.TryGet(
				node.TagName,
				attributeName,
				out var eventDefinition)
				&& eventDefinition is not null)
			{
				element.Events.Add(new HtmlDomEventHandler(eventDefinition));
			}
		}
		if (node.AdditionalRuntimeProperties.Count != 0)
		{
			throw new InvalidDataException(
				$"{node.DocumentScope}::{node.XPath} attempted to mutate the "
				+ "compile-time element property contract. Runtime CSS, layout, "
				+ "state, and effect evidence must be captured by the HTML-root "
				+ "global processors; element types cannot acquire properties "
				+ "from page data.");
		}
		return element;
	}

	private sealed record NodeIdentity(string DocumentScope, string XPath);
}

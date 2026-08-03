using System.Collections.Frozen;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
namespace Iwesun.Runtime.WebView2;

public readonly record struct WebRuntimeCdpDomNodeIdentity(
	string DocumentScope,
	string XPath);

/// <summary>
/// One element in the primary in-memory DOM object tree. Indexes are derived
/// from these linked nodes only after every relationship has been validated.
/// </summary>
public sealed class WebRuntimeCdpDomTreeNode
{
	private WebRuntimeCdpDomTreeNode? _parent;
	private IReadOnlyList<WebRuntimeCdpDomTreeNode> _children = [];
	private bool _relationshipsAttached;

	internal WebRuntimeCdpDomTreeNode(
		string documentScope,
		string xpath,
		string tagName,
		int nodeId,
		int backendNodeId,
		IReadOnlyDictionary<string, string> attributes)
	{
		DocumentScope = documentScope;
		XPath = xpath;
		TagName = tagName;
		NodeId = nodeId;
		BackendNodeId = backendNodeId;
		Attributes = attributes;
	}

	public string DocumentScope { get; }

	public string XPath { get; }

	public string TagName { get; }

	public int NodeId { get; }

	public int BackendNodeId { get; }

	public WebRuntimeCdpDomTreeNode? Parent => _parent;

	public IReadOnlyList<WebRuntimeCdpDomTreeNode> Children => _children;

	public string? ParentXPath => Parent?.XPath;

	public IReadOnlyList<string> ChildXPaths =>
		Children.Select(static child => child.XPath).ToArray();

	public IReadOnlyDictionary<string, string> Attributes { get; }

	internal void AttachRelationships(
		WebRuntimeCdpDomTreeNode? parent,
		IReadOnlyList<WebRuntimeCdpDomTreeNode> children)
	{
		if (_relationshipsAttached)
		{
			throw new InvalidOperationException(
				"DOM node relationships can be attached only once.");
		}
		_relationshipsAttached = true;
		_parent = parent;
		_children = children;
	}
}

/// <summary>
/// Atomically published DOM tree and its three identity indexes.
/// </summary>
public sealed class WebRuntimeCdpDomTreeIndex
{
	private static readonly Regex StepPattern = new(
		"""^(?<tag>[A-Za-z_][A-Za-z0-9_.:-]*|\*)(?<predicates>(?:\[[^\]]+\])*)$""",
		RegexOptions.Compiled | RegexOptions.CultureInvariant);
	private static readonly Regex PredicatePattern = new(
		"""\[(?:(?<position>[1-9][0-9]*)|@(?<attribute>[A-Za-z_][A-Za-z0-9_.:-]*)(?:\s*=\s*(?<quote>['"])(?<value>.*?)\k<quote>)?)\]""",
		RegexOptions.Compiled | RegexOptions.CultureInvariant);
	private readonly FrozenDictionary<WebRuntimeCdpDomNodeIdentity,
		WebRuntimeCdpDomTreeNode> _byIdentity;
	private readonly FrozenDictionary<int, WebRuntimeCdpDomTreeNode> _byNodeId;
	private readonly FrozenDictionary<int, WebRuntimeCdpDomTreeNode> _byBackendNodeId;
	private readonly ConcurrentDictionary<WebRuntimeCdpDomNodeIdentity,
		WebRuntimeCdpDomTreeNode> _resolvedXPathCache = new();

	private WebRuntimeCdpDomTreeIndex(
		WebRuntimeDomTreeSnapshot snapshot,
		IReadOnlyList<WebRuntimeCdpDomTreeNode> roots,
		IReadOnlyList<WebRuntimeCdpDomTreeNode> nodes)
	{
		Snapshot = snapshot;
		Roots = roots;
		_byIdentity = nodes.ToFrozenDictionary(static node =>
			new WebRuntimeCdpDomNodeIdentity(
				node.DocumentScope,
				node.XPath));
		_byNodeId = nodes.ToFrozenDictionary(static node => node.NodeId);
		_byBackendNodeId = nodes.ToFrozenDictionary(
			static node => node.BackendNodeId);
	}

	public WebRuntimeDomTreeSnapshot Snapshot { get; }

	public long Revision => Snapshot.Revision;

	public IReadOnlyList<WebRuntimeCdpDomTreeNode> Roots { get; }

	public IReadOnlyCollection<WebRuntimeCdpDomTreeNode> Nodes =>
		_byIdentity.Values;

	public static WebRuntimeCdpDomTreeIndex Create(
		WebRuntimeDomTreeSnapshot snapshot)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		if (snapshot.Revision <= 0 || snapshot.Elements.Count == 0)
		{
			throw new InvalidDataException(
				"The CDP DOM tree snapshot is incomplete.");
		}
		var nodes = BuildTree(snapshot.Elements);
		if (nodes.Any(static node =>
			node.NodeId <= 0 || node.BackendNodeId <= 0))
		{
			throw new InvalidDataException(
				"Every indexed DOM element requires nodeId and backendNodeId.");
		}
		if (nodes.Select(static node => new WebRuntimeCdpDomNodeIdentity(
				node.DocumentScope,
				node.XPath)).Distinct().Count() != nodes.Length
			|| nodes.Select(static node => node.NodeId).Distinct().Count()
				!= nodes.Length
			|| nodes.Select(static node => node.BackendNodeId).Distinct().Count()
				!= nodes.Length)
		{
			throw new InvalidDataException(
				"The CDP DOM tree contains duplicate node identities.");
		}
		var roots = nodes.Where(static node => node.Parent is null).ToArray();
		if (roots.Length == 0)
		{
			throw new InvalidDataException(
				"The CDP DOM object tree contains no roots.");
		}
		return new(snapshot, roots, nodes);
	}

	public WebRuntimeCdpDomTreeNode ResolveXPath(
		string documentScope,
		string xpath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(documentScope);
		ArgumentException.ThrowIfNullOrWhiteSpace(xpath);
		if (!xpath.StartsWith("/", StringComparison.Ordinal))
			throw new ArgumentException("XPath must be absolute.", nameof(xpath));

		if (_byIdentity.TryGetValue(
			new(documentScope, xpath),
			out var exact))
		{
			return exact;
		}
		var queryIdentity = new WebRuntimeCdpDomNodeIdentity(
			documentScope,
			xpath);
		if (_resolvedXPathCache.TryGetValue(queryIdentity, out var cached))
			return cached;
		if (xpath.Contains('(')
			|| xpath.Contains('|')
			|| xpath.Contains("..", StringComparison.Ordinal)
			|| xpath.Contains("::", StringComparison.Ordinal))
		{
			throw new NotSupportedException(
				$"XPath '{xpath}' uses an expression outside the tracked "
				+ "DOM tree query subset.");
		}
		var resolved = ResolveQueryXPath(documentScope, xpath);
		_resolvedXPathCache.TryAdd(queryIdentity, resolved);
		return resolved;
	}

	public bool TryResolveXPath(
		string documentScope,
		string xpath,
		out WebRuntimeCdpDomTreeNode? node)
	{
		try
		{
			node = ResolveXPath(documentScope, xpath);
			return true;
		}
		catch (Exception exception)
			when (exception is ArgumentException
				or KeyNotFoundException
				or InvalidOperationException
				or NotSupportedException)
		{
			node = null;
			return false;
		}
	}

	public int ResolveNodeId(
		string documentScope,
		string xpath) =>
		ResolveXPath(documentScope, xpath).NodeId;

	private WebRuntimeCdpDomTreeNode ResolveQueryXPath(
		string documentScope,
		string xpath)
	{
		var descendantStart = xpath.StartsWith("//", StringComparison.Ordinal);
		var steps = SplitSteps(xpath[(descendantStart ? 2 : 1)..])
			.Select(ParseStep)
			.ToArray();
		if (steps.Length == 0)
			throw new ArgumentException("XPath has no element steps.", nameof(xpath));

		IReadOnlyList<WebRuntimeCdpDomTreeNode> candidates;
		if (descendantStart)
		{
			candidates = ApplyStep(
				_byIdentity.Values.Where(node =>
					node.DocumentScope.Equals(
						documentScope,
						StringComparison.Ordinal)),
				steps[0],
				groupByParent: true);
		}
		else
		{
			candidates = ApplyStep(
				_byIdentity.Values.Where(node =>
					node.DocumentScope.Equals(
						documentScope,
						StringComparison.Ordinal)
					&& node.ParentXPath is null),
				steps[0],
				groupByParent: false);
		}
		for (var index = 1; index < steps.Length; index++)
		{
			var next = new List<WebRuntimeCdpDomTreeNode>();
			foreach (var parent in candidates)
			{
				next.AddRange(ApplyStep(
					parent.Children,
					steps[index],
					groupByParent: false));
			}
			candidates = next;
		}
		return candidates.Count switch
		{
			1 => candidates[0],
			0 => throw new KeyNotFoundException(
				$"XPath '{documentScope}::{xpath}' is absent from DOM "
				+ $"revision {Revision}."),
			_ => throw new InvalidOperationException(
				$"XPath '{documentScope}::{xpath}' matched "
				+ $"{candidates.Count} nodes in DOM revision {Revision}.")
		};
	}

	private static IReadOnlyList<WebRuntimeCdpDomTreeNode> ApplyStep(
		IEnumerable<WebRuntimeCdpDomTreeNode> source,
		XPathStep step,
		bool groupByParent)
	{
		var matches = source.Where(node =>
			(step.TagName == "*"
				|| node.TagName.Equals(
					step.TagName,
					StringComparison.OrdinalIgnoreCase))
			&& (step.AttributeName is null
				|| node.Attributes.TryGetValue(
					step.AttributeName,
					out var attributeValue)
				&& (step.AttributeValue is null
					|| attributeValue.Equals(
						step.AttributeValue,
						StringComparison.Ordinal)))).ToArray();
		if (step.Position is null)
			return matches;
		var position = step.Position.Value - 1;
		if (!groupByParent)
			return position < matches.Length ? [matches[position]] : [];
		return matches
			.GroupBy(static node => node.ParentXPath, StringComparer.Ordinal)
			.Where(group => position < group.Count())
			.Select(group => group.ElementAt(position))
			.ToArray();
	}

	private static IReadOnlyList<string> SplitSteps(string expression)
	{
		var result = new List<string>();
		var start = 0;
		var bracketDepth = 0;
		char quote = '\0';
		for (var index = 0; index < expression.Length; index++)
		{
			var current = expression[index];
			if (quote != '\0')
			{
				if (current == quote)
					quote = '\0';
				continue;
			}
			if (current is '\'' or '"')
			{
				quote = current;
				continue;
			}
			if (current == '[')
				bracketDepth++;
			else if (current == ']')
				bracketDepth--;
			else if (current == '/' && bracketDepth == 0)
			{
				if (index == start)
				{
					throw new NotSupportedException(
						"Descendant axes are supported only at the beginning.");
				}
				result.Add(expression[start..index]);
				start = index + 1;
			}
			if (bracketDepth < 0)
				throw new ArgumentException("XPath predicates are unbalanced.");
		}
		if (quote != '\0' || bracketDepth != 0 || start == expression.Length)
			throw new ArgumentException("XPath is incomplete or unbalanced.");
		result.Add(expression[start..]);
		return result;
	}

	private static XPathStep ParseStep(string expression)
	{
		var match = StepPattern.Match(expression);
		if (!match.Success)
		{
			throw new NotSupportedException(
				$"XPath step '{expression}' is outside the tracked DOM "
				+ "tree query subset.");
		}
		int? position = null;
		string? attributeName = null;
		string? attributeValue = null;
		var predicates = match.Groups["predicates"].Value;
		var consumed = 0;
		foreach (Match predicate in PredicatePattern.Matches(predicates))
		{
			if (predicate.Index != consumed)
			{
				throw new NotSupportedException(
					$"XPath predicate in '{expression}' is unsupported.");
			}
			consumed += predicate.Length;
			if (predicate.Groups["position"].Success)
			{
				if (position is not null)
					throw new NotSupportedException(
						"An XPath step cannot contain multiple positions.");
				position = int.Parse(
					predicate.Groups["position"].Value,
					System.Globalization.CultureInfo.InvariantCulture);
			}
			else
			{
				if (attributeName is not null)
					throw new NotSupportedException(
						"An XPath step currently supports one attribute predicate.");
				attributeName = predicate.Groups["attribute"].Value;
				attributeValue = predicate.Groups["value"].Success
					? predicate.Groups["value"].Value
					: null;
			}
		}
		if (consumed != predicates.Length)
		{
			throw new NotSupportedException(
				$"XPath predicate in '{expression}' is unsupported.");
		}
		return new(
			match.Groups["tag"].Value,
			position,
			attributeName,
			attributeValue);
	}

	private WebRuntimeCdpDomTreeNode ResolveExactTreePath(
		string documentScope,
		string xpath)
	{
		var root = _byIdentity.Values.SingleOrDefault(node =>
			node.DocumentScope.Equals(documentScope, StringComparison.Ordinal)
			&& node.ParentXPath is null
			&& (xpath.Equals(node.XPath, StringComparison.Ordinal)
				|| xpath.StartsWith(
					node.XPath + "/",
					StringComparison.Ordinal)));
		if (root is null)
		{
			throw new KeyNotFoundException(
				$"DOM document scope '{documentScope}' has no matching root.");
		}
		var current = root;
		while (!current.XPath.Equals(xpath, StringComparison.Ordinal))
		{
			var nextPath = current.ChildXPaths
				.Where(path =>
					xpath.Equals(path, StringComparison.Ordinal)
					|| xpath.StartsWith(path + "/", StringComparison.Ordinal))
				.OrderByDescending(static path => path.Length)
				.FirstOrDefault();
			if (nextPath is null
				|| !_byIdentity.TryGetValue(
					new(documentScope, nextPath),
					out current!))
			{
				throw new KeyNotFoundException(
					$"XPath '{documentScope}::{xpath}' is absent from DOM "
					+ $"revision {Revision}.");
			}
		}
		return current;
	}

	public bool TryGetNode(
		int nodeId,
		out WebRuntimeCdpDomTreeNode node) =>
		_byNodeId.TryGetValue(nodeId, out node!);

	public bool TryGetBackendNode(
		int backendNodeId,
		out WebRuntimeCdpDomTreeNode node) =>
		_byBackendNodeId.TryGetValue(backendNodeId, out node!);

	private static WebRuntimeCdpDomTreeNode[] BuildTree(
		IReadOnlyList<WebRuntimeDomTreeElement> sources)
	{
		var byIdentity = sources.ToDictionary(
			static source => new WebRuntimeCdpDomNodeIdentity(
				source.DocumentScope,
				source.XPath),
			static source => new WebRuntimeCdpDomTreeNode(
				source.DocumentScope,
				source.XPath,
				source.TagName,
				source.NodeId,
				source.BackendNodeId,
				new Dictionary<string, string>(
					source.AttributeValues,
					StringComparer.OrdinalIgnoreCase)));
		if (byIdentity.Count != sources.Count)
		{
			throw new InvalidDataException(
				"The CDP DOM source contains duplicate XPath identities.");
		}
		foreach (var source in sources)
		{
			var identity = new WebRuntimeCdpDomNodeIdentity(
				source.DocumentScope,
				source.XPath);
			var node = byIdentity[identity];
			WebRuntimeCdpDomTreeNode? parent = null;
			if (source.ParentXPath is not null
				&& !byIdentity.TryGetValue(
					new(source.DocumentScope, source.ParentXPath),
					out parent))
			{
				throw new InvalidDataException(
					$"DOM parent '{source.DocumentScope}::"
					+ $"{source.ParentXPath}' is missing.");
			}
			var children = source.ChildXPaths.Select(childXPath =>
			{
				if (!byIdentity.TryGetValue(
					new(source.DocumentScope, childXPath),
					out var child))
				{
					throw new InvalidDataException(
						$"DOM child '{source.DocumentScope}::"
						+ $"{childXPath}' is missing.");
				}
				return child;
			}).ToArray();
			node.AttachRelationships(parent, children);
		}
		foreach (var node in byIdentity.Values)
		{
			foreach (var child in node.Children)
			{
				if (!ReferenceEquals(child.Parent, node))
				{
					throw new InvalidDataException(
						$"DOM relationship '{node.DocumentScope}::"
						+ $"{node.XPath}' -> '{child.XPath}' is not reciprocal.");
				}
			}
		}
		return byIdentity.Values.ToArray();
	}

	private sealed record XPathStep(
		string TagName,
		int? Position,
		string? AttributeName,
		string? AttributeValue);
}

using System.Globalization;

namespace Iwesun.Runtime.Diagnostics;

public readonly struct RuntimeState :
	IEquatable<RuntimeState>,
	IRuntimeStateExactMatch<RuntimeState>,
	IRuntimeStateHierarchy<RuntimeState>
{
	public RuntimeState(
		int code,
		string name,
		RuntimeStateGroup group = RuntimeStateGroup.Custom,
		string? displayName = null,
		int level = 0,
		RuntimeStateKey? key = null,
		int? parentCode = null,
		RuntimeStateCatalog? catalog = null)
	{
		if (code <= 0)
			throw new ArgumentOutOfRangeException(nameof(code));

		ArgumentException.ThrowIfNullOrWhiteSpace(name);

		Code = code;
		Name = name;
		Group = group;
		DisplayName = string.IsNullOrWhiteSpace(displayName) ? name : displayName;
		Level = Math.Max(0, level);
		Key = key;
		ParentCode = parentCode;
		Catalog = catalog;
	}

	public int Code { get; }
	public string Name { get; }
	public RuntimeStateGroup Group { get; }
	public string DisplayName { get; }
	public int Level { get; }
	public RuntimeStateKey? Key { get; }
	public int? ParentCode { get; }
	internal RuntimeStateCatalog? Catalog { get; }
	public bool HasParent => ParentCode.HasValue;
	public bool HasKey => Key.HasValue;

	public bool ExactEquals(RuntimeState other) => Code == other.Code;

	public bool Is(RuntimeState other)
	{
		if (ExactEquals(other))
			return true;

		var currentCode = ParentCode;
		while (currentCode.HasValue)
		{
			if (currentCode.Value == other.Code)
				return true;

			if (Catalog == null || !Catalog.TryGetByCode(currentCode.Value, out var current))
				break;

			currentCode = current.ParentCode;
		}

		return false;
	}

	public bool Equals(RuntimeState other) => ExactEquals(other);

	public override bool Equals(object? obj) => obj is RuntimeState other && Equals(other);

	public override int GetHashCode() => Code;

	public string ToDisplayString(CultureInfo? culture = null)
	{
		culture ??= CultureInfo.CurrentUICulture;
		if (Catalog != null && Catalog.TryGetDisplayName(this, culture, out var displayName))
			return displayName;

		return DisplayName;
	}

	public override string ToString() => ToDisplayString();

	public static bool operator ==(RuntimeState left, RuntimeState right) => left.Equals(right);
	public static bool operator !=(RuntimeState left, RuntimeState right) => !left.Equals(right);
}
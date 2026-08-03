using System.Reflection;
using System.Text.Json;

namespace Iwesun.Runtime.Diagnostics;

public sealed class ReflectionRuntimeDiagnosticTarget : RuntimeDiagnosticTargetBase
{
	private readonly object _instance;
	private readonly RuntimeDiagnosticObjectAccess _access;

	public ReflectionRuntimeDiagnosticTarget(
		string targetId,
		object instance,
		RuntimeDiagnosticObjectAccess? access = null)
		: base(targetId)
	{
		_instance = instance ?? throw new ArgumentNullException(nameof(instance));
		_access = access ?? new RuntimeDiagnosticObjectAccess();
	}

	public override object Snapshot()
	{
		var values = new SortedDictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
		foreach (var member in EnumerateReadableMembers())
		{
			try
			{
				values[member.Name] = GetMemberValue(member);
			}
			catch (Exception ex)
			{
				values[member.Name] = new { error = ex.GetType().Name, ex.Message };
			}
		}

		return new
		{
			type = _instance.GetType().FullName,
			values
		};
	}

	public override Task<RuntimeDiagnosticActionResult> ExecuteAsync(RuntimeDiagnosticAction command, CancellationToken ct)
	{
		if (command.Action.Equals("snapshot", StringComparison.OrdinalIgnoreCase))
			return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, Snapshot()));

		if (command.Action.Equals("get", StringComparison.OrdinalIgnoreCase))
			return Task.FromResult(GetValue(command));

		if (command.Action.Equals("set", StringComparison.OrdinalIgnoreCase))
			return Task.FromResult(SetValue(command));

		#if DEBUG
		if (command.Action.Equals("invoke", StringComparison.OrdinalIgnoreCase))
			return Task.FromResult(Invoke(command));
		#else
		if (command.Action.Equals("invoke", StringComparison.OrdinalIgnoreCase))
			return Task.FromResult(RuntimeDiagnosticActionResult.Fail(
				TargetId,
				command.Action,
				"Reflection invocation is not compiled into Release Diagnostics.",
				"CAPABILITY_DEBUG_ONLY"));
		#endif

		if (command.Action.Equals("navigate", StringComparison.OrdinalIgnoreCase))
			return Task.FromResult(NavigatePath(command.Member));

		return base.ExecuteAsync(command, ct);
	}

	// ── Path navigation ──────────────────────────────────────────────
	//   Supports dot-notation paths like:
	//     "Profile.Addresses[0].City"
	//     "Settings.Timeout"
	//   Navigates properties, fields, and indexers using BindingFlags.NonPublic.

	private RuntimeDiagnosticActionResult NavigatePath(string? path)
	{
		if (string.IsNullOrWhiteSpace(path))
			return RuntimeDiagnosticActionResult.Fail(TargetId, "navigate", "Path is required.");

		var parts = path.Split('.');
		object? current = _instance;

		foreach (var part in parts)
		{
			if (current == null)
				return RuntimeDiagnosticActionResult.Fail(TargetId, "navigate", $"Null encountered at '{part}'");

			// Check for indexer: PropertyName[index]
			var bracketPos = part.IndexOf('[');
			if (bracketPos > 0 && part.EndsWith(']'))
			{
				var propName = part[..bracketPos];
				var indexStr = part[(bracketPos + 1)..^1];

				if (!int.TryParse(indexStr, out var index))
					return RuntimeDiagnosticActionResult.Fail(TargetId, "navigate", $"Invalid index: {indexStr}");

				var prop = current.GetType().GetProperty(propName,
					BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (prop == null)
					return RuntimeDiagnosticActionResult.Fail(TargetId, "navigate", $"Property not found: {propName}");

				var collection = prop.GetValue(current);
				current = collection switch
				{
					System.Collections.IList list => list[index],
					System.Collections.IDictionary dict when dict.Keys.Count > index =>
						dict[dict.Keys.Cast<object>().ElementAt(index)],
					_ => null
				};
			}
			else
			{
				// Regular property or field
				var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
				var prop = current.GetType().GetProperty(part, flags);
				if (prop != null)
				{
					current = prop.GetValue(current);
					continue;
				}

				var field = current.GetType().GetField(part, flags);
				if (field != null)
				{
					current = field.GetValue(current);
					continue;
				}

				return RuntimeDiagnosticActionResult.Fail(TargetId, "navigate", $"Member not found: {part}");
			}
		}

		return RuntimeDiagnosticActionResult.Ok(TargetId, "navigate", new
		{
			path,
			type = current?.GetType().FullName,
			value = current
		});
	}

	private RuntimeDiagnosticActionResult GetValue(RuntimeDiagnosticAction command)
	{
		if (string.IsNullOrWhiteSpace(command.Member))
			return RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, "Member is required.");

		var member = FindMember(command.Member);
		if (member == null || !CanRead(member.Name))
			return RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, $"Member is not readable: {command.Member}");

		return RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, GetMemberValue(member));
	}

	private RuntimeDiagnosticActionResult SetValue(RuntimeDiagnosticAction command)
	{
		if (string.IsNullOrWhiteSpace(command.Member))
			return RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, "Member is required.");

		if (!_access.WritableMembers.Contains(command.Member, StringComparer.OrdinalIgnoreCase))
			return RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, $"Member is not writable: {command.Member}");

		var member = FindMember(command.Member);
		if (member == null)
			return RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, $"Member not found: {command.Member}");

		try
		{
			SetMemberValue(member, command.Value);
			return RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, GetMemberValue(member));
		}
		catch (Exception ex)
		{
			return RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, $"{ex.GetType().Name}: {ex.Message}");
		}
	}

	#if DEBUG
	private RuntimeDiagnosticActionResult Invoke(RuntimeDiagnosticAction command)
	{
		if (string.IsNullOrWhiteSpace(command.Member))
			return RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, "Member is required.");

		if (!_access.InvokableMembers.Contains(command.Member, StringComparer.OrdinalIgnoreCase))
			return RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, $"Member is not invokable: {command.Member}");

		var method = _instance.GetType().GetMethod(command.Member, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (method == null)
			return RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, $"Method not found: {command.Member}");
		if (method.GetParameters().Length != 0)
			return RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, "Only parameterless method invocation is supported by the reflection target.");

		try
		{
			var value = method.Invoke(_instance, null);
			return RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, value);
		}
		catch (TargetInvocationException ex) when (ex.InnerException != null)
		{
			return RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, $"{ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
		}
		catch (Exception ex)
		{
			return RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, $"{ex.GetType().Name}: {ex.Message}");
		}
	}
	#endif

	private IEnumerable<MemberInfo> EnumerateReadableMembers()
	{
		var type = _instance.GetType();
		foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
		{
			if (property.GetIndexParameters().Length == 0 && CanRead(property.Name))
				yield return property;
		}

		foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
		{
			if (CanRead(field.Name))
				yield return field;
		}
	}

	private bool CanRead(string memberName)
	{
		if (_access.ReadableMembers.Contains(memberName, StringComparer.OrdinalIgnoreCase))
			return true;
		return _access.AllowReadAllPublic;
	}

	private MemberInfo? FindMember(string memberName)
	{
		var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
		var type = _instance.GetType();
		return type.GetProperty(memberName, flags)
			?? (MemberInfo?)type.GetField(memberName, flags);
	}

	private object? GetMemberValue(MemberInfo member) =>
		member switch
		{
			PropertyInfo property => property.GetValue(_instance),
			FieldInfo field => field.GetValue(_instance),
			_ => null
		};

	private void SetMemberValue(MemberInfo member, JsonElement? value)
	{
		switch (member)
		{
			case PropertyInfo { CanWrite: true } property:
				property.SetValue(_instance, ConvertValue(value, property.PropertyType));
				break;
			case FieldInfo field:
				field.SetValue(_instance, ConvertValue(value, field.FieldType));
				break;
			default:
				throw new InvalidOperationException($"Member cannot be written: {member.Name}");
		}
	}

	private static object? ConvertValue(JsonElement? value, Type targetType)
	{
		if (value == null)
			return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;

		return value.Value.Deserialize(targetType);
	}
}

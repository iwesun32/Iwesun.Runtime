using System.Diagnostics;
using System.Reflection;

namespace Iwesun.Runtime.Diagnostics;

public static class RuntimeHostScanner
{
	private static readonly string InstanceId = Guid.NewGuid().ToString("N");
	public static RuntimeHostSnapshot Scan(
		IEnumerable<string> registeredTargets,
		RuntimeHostScanOptions? options = null)
	{
		options ??= new RuntimeHostScanOptions();
		var process = Process.GetCurrentProcess();
		var assemblies = AppDomain.CurrentDomain
			.GetAssemblies()
			.Where(assembly => ShouldIncludeAssembly(assembly.GetName().Name ?? "", options))
			.OrderBy(assembly => assembly.GetName().Name, StringComparer.OrdinalIgnoreCase)
			.Select(assembly => ScanAssembly(assembly, options))
			.ToArray();

		return new RuntimeHostSnapshot
		{
			InstanceId = InstanceId,
			ProcessStartTimeUtc = process.StartTime.ToUniversalTime(),
			ProcessName = process.ProcessName,
			ProcessId = process.Id,
			RuntimeVersion = Environment.Version.ToString(),
			BaseDirectory = AppContext.BaseDirectory,
			Assemblies = assemblies,
			RegisteredTargets = registeredTargets.Order(StringComparer.OrdinalIgnoreCase).ToArray()
		};
	}

	private static bool ShouldIncludeAssembly(string name, RuntimeHostScanOptions options)
	{
		if (options.IncludeAssemblyPrefixes.Count > 0
			&& !options.IncludeAssemblyPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
		{
			return false;
		}

		return !options.ExcludeAssemblyPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
	}

	private static RuntimeAssemblySnapshot ScanAssembly(Assembly assembly, RuntimeHostScanOptions options)
	{
		try
		{
			var allTypes = GetLoadableTypes(assembly);
			var selected = allTypes
				.Where(type => options.IncludeNonPublicTypes || type.IsPublic || type.IsNestedPublic)
				.OrderBy(type => type.FullName, StringComparer.OrdinalIgnoreCase)
				.Take(Math.Max(1, options.MaxTypesPerAssembly))
				.Select(ScanType)
				.ToArray();

			return new RuntimeAssemblySnapshot
			{
				Name = assembly.GetName().Name ?? "",
				FullName = assembly.FullName ?? "",
				Location = SafeLocation(assembly),
				TypeCount = allTypes.Length,
				Types = selected
			};
		}
		catch (Exception ex)
		{
			return new RuntimeAssemblySnapshot
			{
				Name = assembly.GetName().Name ?? "",
				FullName = assembly.FullName ?? "",
				Location = SafeLocation(assembly),
				Error = $"{ex.GetType().Name}: {ex.Message}"
			};
		}
	}

	private static Type[] GetLoadableTypes(Assembly assembly)
	{
		try
		{
			return assembly.GetTypes();
		}
		catch (ReflectionTypeLoadException ex)
		{
			return ex.Types.Where(type => type != null).Cast<Type>().ToArray();
		}
	}

	private static RuntimeTypeSnapshot ScanType(Type type)
	{
		const BindingFlags publicInstance = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public;
		var interfaces = type.GetInterfaces()
			.Select(t => t.FullName ?? t.Name)
			.Order(StringComparer.OrdinalIgnoreCase)
			.Take(32)
			.ToArray();

		return new RuntimeTypeSnapshot
		{
			Name = type.Name,
			FullName = type.FullName ?? type.Name,
			Namespace = type.Namespace ?? "",
			Kind = GetKind(type),
			IsPublic = type.IsPublic || type.IsNestedPublic,
			IsAbstract = type.IsAbstract,
			IsGeneric = type.IsGenericType,
			PublicPropertyCount = type.GetProperties(publicInstance).Length,
			PublicFieldCount = type.GetFields(publicInstance).Length,
			PublicMethodCount = type.GetMethods(publicInstance).Count(method => !method.IsSpecialName),
			PublicEventCount = type.GetEvents(publicInstance).Length,
			Interfaces = interfaces
		};
	}

	private static string GetKind(Type type)
	{
		if (type.IsInterface) return "interface";
		if (type.IsEnum) return "enum";
		if (type.IsValueType) return "struct";
		if (typeof(Delegate).IsAssignableFrom(type)) return "delegate";
		return "class";
	}

	private static string? SafeLocation(Assembly assembly)
	{
		try
		{
			return assembly.IsDynamic ? null : assembly.Location;
		}
		catch
		{
			return null;
		}
	}
}

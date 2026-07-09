using System.Globalization;
using System.Collections.Generic;

namespace Iwesun.Runtime.Diagnostics;

public sealed class RuntimeStateCatalog : IRuntimeStateExtension<RuntimeState>
,
	IRuntimeStateTextConverter<RuntimeState>
{
	private readonly object _gate = new();
	private readonly Dictionary<int, RuntimeState> _statesByCode = new();
	private readonly Dictionary<string, RuntimeState> _statesByName = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<RuntimeStateKey, RuntimeState> _statesByKey = new();

	public static RuntimeStateCatalog CreateLifecycleDefaults()
	{
		var catalog = new RuntimeStateCatalog();
		catalog.AddRoot(1, "Start", RuntimeStateGroup.Lifecycle, "Start", RuntimeStateKey.Start);
		catalog.AddRoot(2, "Working", RuntimeStateGroup.Lifecycle, "Working", RuntimeStateKey.Working);
		catalog.AddRoot(3, "Stop", RuntimeStateGroup.Lifecycle, "Stop", RuntimeStateKey.Stop);
		return catalog;
	}

	public static RuntimeStateCatalog CreateOnlineDefaults()
	{
		var catalog = CreateLifecycleDefaults();
		var start = catalog.RequireByName("Start");
		var working = catalog.RequireByName("Working");
		var stop = catalog.RequireByName("Stop");

		catalog.Add(11, working, "Idle", RuntimeStateGroup.WorkPhase, "Idle", RuntimeStateKey.WorkingIdle);
		catalog.Add(12, working, "Collecting", RuntimeStateGroup.WorkPhase, "Collecting", RuntimeStateKey.WorkingCollecting);
		catalog.Add(13, working, "Analyzing", RuntimeStateGroup.WorkPhase, "Analyzing", RuntimeStateKey.WorkingAnalyzing);
		catalog.Add(14, working, "NetworkAccess", RuntimeStateGroup.WorkPhase, "NetworkAccess", RuntimeStateKey.WorkingNetworkAccess);

		catalog.Add(21, stop, "Requested", RuntimeStateGroup.ShutdownPhase, "Requested", RuntimeStateKey.StopRequested);
		catalog.Add(22, stop, "Draining", RuntimeStateGroup.ShutdownPhase, "Draining", RuntimeStateKey.StopDraining);
		catalog.Add(23, stop, "Completed", RuntimeStateGroup.ShutdownPhase, "Completed", RuntimeStateKey.StopCompleted);
		catalog.Add(24, stop, "Timeout", RuntimeStateGroup.ShutdownPhase, "Timeout", RuntimeStateKey.StopTimeout);

		return catalog;
	}

	public RuntimeState AddRoot(int code, string name, RuntimeStateGroup group = RuntimeStateGroup.Custom, string? displayName = null)
		=> AddRoot(code, name, group, displayName, null);

	public RuntimeState AddRoot(int code, string name, RuntimeStateGroup group, string? displayName, RuntimeStateKey? key)
	{
		var state = new RuntimeState(code, name, group, displayName, 0, key, null, this);
		return Register(state);
	}

	public RuntimeState Add(int code, RuntimeState parent, string name, RuntimeStateGroup group = RuntimeStateGroup.Custom, string? displayName = null)
		=> Add(code, parent, name, group, displayName, null);

	public RuntimeState Add(int code, RuntimeState parent, string name, RuntimeStateGroup group, string? displayName, RuntimeStateKey? key)
	{
		var state = new RuntimeState(code, name, group, displayName, parent.Level + 1, key, parent.Code, this);
		return Register(state);
	}

	public bool TryGetByCode(int code, out RuntimeState state)
	{
		lock (_gate)
		{
			return _statesByCode.TryGetValue(code, out state);
		}
	}

	public bool TryGetByName(string name, out RuntimeState state)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			state = default;
			return false;
		}

		lock (_gate)
		{
			return _statesByName.TryGetValue(name, out state);
		}
	}

	public bool TryGetByKey(RuntimeStateKey key, out RuntimeState state)
	{
		lock (_gate)
		{
			return _statesByKey.TryGetValue(key, out state);
		}
	}

	public RuntimeState RequireByName(string name)
	{
		if (TryGetByName(name, out var state))
			return state;

		throw new KeyNotFoundException($"State name not found: {name}");
	}

	public RuntimeState RequireByCode(int code)
	{
		if (TryGetByCode(code, out var state))
			return state;

		throw new KeyNotFoundException($"State code not found: {code}");
	}

	public RuntimeState RequireByKey(RuntimeStateKey key)
	{
		if (TryGetByKey(key, out var state))
			return state;

		throw new KeyNotFoundException($"State key not found: {key}");
	}

	public bool TryGetDisplayName(RuntimeState state, CultureInfo culture, out string displayName)
	{
		if (state.Key.HasValue)
		{
			displayName = GetLocalizedDisplayName(state.Key.Value, culture);
			return true;
		}

		displayName = state.DisplayName;
		return !string.IsNullOrWhiteSpace(displayName);
	}

	public string ToDisplayString(RuntimeState state, CultureInfo? culture = null)
	{
		culture ??= CultureInfo.CurrentUICulture;
		return TryGetDisplayName(state, culture, out var displayName)
			? displayName
			: state.DisplayName;
	}

	public bool TryParse(string text, out RuntimeState state, CultureInfo? culture = null)
	{
		culture ??= CultureInfo.CurrentUICulture;
		if (TryGetByName(text, out state))
			return true;

		if (TryGetByDisplayName(text, culture, out state))
			return true;

		if (Enum.TryParse<RuntimeStateKey>(text, true, out var key) && TryGetByKey(key, out state))
			return true;

		state = default;
		return false;
	}

	public bool TryGetByDisplayName(string text, CultureInfo culture, out RuntimeState state)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			state = default;
			return false;
		}

		lock (_gate)
		{
			foreach (var item in _statesByCode.Values)
			{
				if (TryGetDisplayName(item, culture, out var displayName)
					&& displayName.Equals(text, StringComparison.OrdinalIgnoreCase))
				{
					state = item;
					return true;
				}
			}
		}

		state = default;
		return false;
	}

	public IReadOnlyList<RuntimeState> Snapshot()
	{
		lock (_gate)
		{
			return _statesByCode.Values
				.OrderBy(state => state.Code)
				.ToArray();
		}
	}

	private RuntimeState Register(RuntimeState state)
	{
		lock (_gate)
		{
			if (_statesByCode.ContainsKey(state.Code))
				throw new InvalidOperationException($"State code already exists: {state.Code}");

			if (_statesByName.ContainsKey(state.Name))
				throw new InvalidOperationException($"State name already exists: {state.Name}");

			if (state.Key.HasValue && _statesByKey.ContainsKey(state.Key.Value))
				throw new InvalidOperationException($"State key already exists: {state.Key.Value}");

			_statesByCode[state.Code] = state;
			_statesByName[state.Name] = state;
			if (state.Key.HasValue)
				_statesByKey[state.Key.Value] = state;
			return state;
		}
	}

	private static string GetLocalizedDisplayName(RuntimeStateKey key, CultureInfo culture)
	{
		var isChinese = culture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase);
		return key switch
		{
			RuntimeStateKey.Start => isChinese ? "启动" : "Start",
			RuntimeStateKey.Working => isChinese ? "工作" : "Working",
			RuntimeStateKey.Stop => isChinese ? "停止" : "Stop",
			RuntimeStateKey.WorkingIdle => isChinese ? "空闲" : "Idle",
			RuntimeStateKey.WorkingCollecting => isChinese ? "数据收集" : "Collecting",
			RuntimeStateKey.WorkingAnalyzing => isChinese ? "分析" : "Analyzing",
			RuntimeStateKey.WorkingNetworkAccess => isChinese ? "网络访问" : "Network Access",
			RuntimeStateKey.StopRequested => isChinese ? "停止请求" : "Requested",
			RuntimeStateKey.StopDraining => isChinese ? "收尾中" : "Draining",
			RuntimeStateKey.StopCompleted => isChinese ? "已完成" : "Completed",
			RuntimeStateKey.StopTimeout => isChinese ? "超时" : "Timeout",
			_ => key.ToString()
		};
	}
}
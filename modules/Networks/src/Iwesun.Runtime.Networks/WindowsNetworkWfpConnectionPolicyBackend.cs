using System.Buffers.Binary;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Iwesun.Runtime.Networks;

/// <summary>
/// Installs one non-persistent WFP connection policy in one dynamic engine session.
/// Closing the lease closes the session and removes the policy; Windows also closes the
/// session when the owning process exits, so no persistent crash remnant is created.
/// </summary>
internal sealed class WindowsNetworkWfpConnectionPolicyBackend : INetworkWfpConnectionPolicyBackend
{
	private const uint DynamicSession = 0x00000001;
	private const uint RpcCAuthnWinnt = 10;
	private const int ErrorAccessDenied = 5;
	private const int NetworkConnectionPolicyContext = 13;
	private const ulong PolicyWeight = 0x100000000;
	private const uint ProviderContextNotFound = 0x80320006;

	private static readonly Guid ConditionAleAppId = new("d78e1e87-8644-4ea5-9437-d809ecefc971");
	private static readonly Guid ConditionLocalAddress = new("d9ee00de-c1ef-4617-bfe3-ffd8f5a08957");
	private static readonly Guid ConditionRemoteAddress = new("b235ae9a-1d64-49b8-a44c-5ff3d9095045");
	private static readonly Guid ConditionIpProtocol = new("3971ef2b-623e-4f9a-8cb1-6e79b806b9a7");
	private static readonly Guid ConditionLocalPort = new("0c1ba1af-5765-453f-af22-a8f791ac775b");
	private static readonly Guid ConditionRemotePort = new("c35a604d-d22b-4e1a-91b4-68f674ee674b");

	public static INetworkWfpConnectionPolicyBackend CreateDefault() => OperatingSystem.IsWindows()
		? new WindowsNetworkWfpConnectionPolicyBackend()
		: new UnsupportedNetworkWfpConnectionPolicyBackend();

	public NetworkWfpPolicyCapability QueryCapability()
	{
		if (!OperatingSystem.IsWindows() || !HasRequiredExports())
			return new(false, false, NetworkAccessFailureCodes.ExactNextHopBackendUnavailable, default);

		try
		{
			using var identity = WindowsIdentity.GetCurrent();
			var elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
			if (!elevated)
				return new(false, true, NetworkAccessFailureCodes.PlatformPermissionMissing, default);
		}
		catch (Exception ex)
		{
			return new(
				false,
				true,
				NetworkAccessFailureCodes.PlatformPermissionMissing,
				FromException(ex, "wfp-permission-query"));
		}

		var session = new FwpmSession();
		var openError = FwpmEngineOpen0(null, RpcCAuthnWinnt, IntPtr.Zero, in session, out var engine);
		if (openError != 0)
		{
			var missingPermission = openError == ErrorAccessDenied;
			return new(
				false,
				missingPermission,
				missingPermission
					? NetworkAccessFailureCodes.PlatformPermissionMissing
					: NetworkAccessFailureCodes.ExactNextHopBackendUnavailable,
				new NetworkPlatformError(
					"windows-wfp",
					unchecked((int)openError),
					0,
					"wfp-engine-open-capability"));
		}

		FwpmEngineClose0(engine);
		return new(true, false, string.Empty, default);
	}

	public NetworkWfpPolicyAcquireResult Acquire(in NetworkWfpConnectionPolicyRequest request)
	{
		if (!request.IsValid)
			return new(null, NetworkAccessFailureCodes.WfpPolicyRequestInvalid, default);

		var capability = QueryCapability();
		if (!capability.Supported)
			return new(null, capability.ReasonCode, capability.PlatformError);

		IntPtr engine = IntPtr.Zero;
		IntPtr appId = IntPtr.Zero;
		using var memory = new NativeMemoryScope();
		try
		{
			var session = new FwpmSession
			{
				SessionKey = Guid.NewGuid(),
				DisplayData = new FwpmDisplayData(
					memory.String("Iwesun.Runtime.Networks exact-next-hop"),
					memory.String(request.Identity.BranchId.ToString("D"))),
				Flags = DynamicSession,
				ProcessId = checked((uint)Environment.ProcessId),
			};
			var openError = FwpmEngineOpen0(null, RpcCAuthnWinnt, IntPtr.Zero, in session, out engine);
			if (openError != 0)
				return NativeFailure(openError, "wfp-engine-open");

			var appError = FwpmGetAppIdFromFileName0(request.ApplicationPath, out appId);
			if (appError != 0)
				return NativeFailure(appError, "wfp-app-id");

			var settings = CreateSettings(request, memory);
			var settingsPointer = memory.Array(settings);
			var policySettings = memory.Structure(new FwpmNetworkConnectionPolicySettings
			{
				NumberOfSettings = checked((uint)settings.Length),
				Settings = settingsPointer,
			});
			var providerContext = new FwpmProviderContext
			{
				ProviderContextKey = request.PolicyId,
				DisplayData = new FwpmDisplayData(
					memory.String("Iwesun.Runtime.Networks exact-next-hop policy"),
					memory.String(request.Identity.BranchId.ToString("D"))),
				Type = NetworkConnectionPolicyContext,
				Context = policySettings,
			};
			var conditions = CreateConditions(request, appId, memory);
			var conditionsPointer = memory.Array(conditions);
			var addError = FwpmConnectionPolicyAdd0(
				engine,
				in providerContext,
				request.Destination.IsIPv4 ? 0 : 1,
				PolicyWeight,
				checked((uint)conditions.Length),
				conditionsPointer,
				IntPtr.Zero);
			if (addError != 0)
				return NativeFailure(addError, "wfp-policy-add");

			var lease = new Lease(request, engine);
			engine = IntPtr.Zero;
			return new(lease, string.Empty, default);
		}
		catch (Exception ex)
		{
			return new(null, NetworkAccessFailureCodes.WfpPolicyAddFailed, FromException(ex, "wfp-policy-add"));
		}
		finally
		{
			if (appId != IntPtr.Zero) FwpmFreeMemory0(ref appId);
			if (engine != IntPtr.Zero) FwpmEngineClose0(engine);
		}
	}

	private static FwpmNetworkConnectionPolicySetting[] CreateSettings(
		in NetworkWfpConnectionPolicyRequest request,
		NativeMemoryScope memory) =>
	[
		new() { Type = 0, Value = AddressValue(request.Source, memory) },
		new() { Type = 1, Value = new FwpValue(FwpDataType.UInt64, memory.UInt64(request.Interface.Luid)) },
		new() { Type = 2, Value = AddressValue(request.NextHop, memory) },
	];

	private static FwpmFilterCondition[] CreateConditions(
		in NetworkWfpConnectionPolicyRequest request,
		IntPtr appId,
		NativeMemoryScope memory) =>
	[
		new(ConditionAleAppId, new FwpConditionValue(FwpDataType.ByteBlob, appId)),
		new(ConditionLocalAddress, ConditionAddressValue(request.Source, memory)),
		new(ConditionRemoteAddress, ConditionAddressValue(request.Destination, memory)),
		new(ConditionIpProtocol, new FwpConditionValue(FwpDataType.UInt8, (IntPtr)(byte)request.Protocol)),
		new(ConditionLocalPort, new FwpConditionValue(FwpDataType.UInt16, (IntPtr)request.LocalPort)),
		new(ConditionRemotePort, new FwpConditionValue(FwpDataType.UInt16, (IntPtr)request.RemotePort)),
	];

	private static FwpValue AddressValue(IpAddressValue address, NativeMemoryScope memory) => address.IsIPv4
		? new(FwpDataType.UInt32, (IntPtr)ToIpv4UInt32(address))
		: new(FwpDataType.ByteArray16, memory.Bytes(address.ToIPAddress().GetAddressBytes()));

	private static FwpConditionValue ConditionAddressValue(IpAddressValue address, NativeMemoryScope memory) => address.IsIPv4
		? new(FwpDataType.UInt32, (IntPtr)ToIpv4UInt32(address))
		: new(FwpDataType.ByteArray16, memory.Bytes(address.ToIPAddress().GetAddressBytes()));

	private static uint ToIpv4UInt32(IpAddressValue address) =>
		BinaryPrimitives.ReadUInt32BigEndian(address.ToIPAddress().GetAddressBytes());

	private static NetworkWfpPolicyAcquireResult NativeFailure(uint nativeCode, string category) => new(
		null,
		nativeCode == ErrorAccessDenied
			? NetworkAccessFailureCodes.PlatformPermissionMissing
			: NetworkAccessFailureCodes.WfpPolicyAddFailed,
		new NetworkPlatformError("windows-wfp", unchecked((int)nativeCode), 0, category));

	private static NetworkPlatformError FromException(Exception exception, string category) => new(
		"windows-wfp",
		0,
		exception.HResult,
		category);

	private static bool HasRequiredExports()
	{
		if (!NativeLibrary.TryLoad("fwpuclnt.dll", out var library)) return false;
		try
		{
			return NativeLibrary.TryGetExport(library, "FwpmEngineOpen0", out _) &&
				NativeLibrary.TryGetExport(library, "FwpmConnectionPolicyAdd0", out _) &&
				NativeLibrary.TryGetExport(library, "FwpmGetAppIdFromFileName0", out _);
		}
		finally
		{
			NativeLibrary.Free(library);
		}
	}

	internal static int[] GetNativeLayoutSizes() =>
	[
		Marshal.SizeOf<FwpmSession>(),
		Marshal.SizeOf<FwpValue>(),
		Marshal.SizeOf<FwpmNetworkConnectionPolicySetting>(),
		Marshal.SizeOf<FwpmNetworkConnectionPolicySettings>(),
		Marshal.SizeOf<FwpmProviderContext>(),
		Marshal.SizeOf<FwpmFilterCondition>(),
	];

	internal bool TryPolicyExists(
		Guid policyId,
		out bool exists,
		out NetworkPlatformError platformError)
	{
		exists = false;
		platformError = default;
		if (policyId == Guid.Empty || !OperatingSystem.IsWindows()) return false;

		var session = new FwpmSession();
		var openError = FwpmEngineOpen0(null, RpcCAuthnWinnt, IntPtr.Zero, in session, out var engine);
		if (openError != 0)
		{
			platformError = new("windows-wfp", unchecked((int)openError), 0, "wfp-engine-open-query");
			return false;
		}

		IntPtr context = IntPtr.Zero;
		try
		{
			var queryError = FwpmProviderContextGetByKey3(engine, in policyId, out context);
			if (queryError == 0)
			{
				exists = true;
				return true;
			}
			if (queryError == ProviderContextNotFound)
				return true;
			platformError = new(
				"windows-wfp", unchecked((int)queryError), 0, "wfp-provider-context-query");
			return false;
		}
		finally
		{
			if (context != IntPtr.Zero) FwpmFreeMemory0(ref context);
			FwpmEngineClose0(engine);
		}
	}

	private sealed class Lease(NetworkWfpConnectionPolicyRequest request, IntPtr engine)
		: INetworkWfpConnectionPolicyLease
	{
		private IntPtr _engine = engine;
		public Guid PolicyId => request.PolicyId;
		public NetworkWfpConnectionPolicyRequest Request => request;

		public void Dispose()
		{
			var handle = Interlocked.Exchange(ref _engine, IntPtr.Zero);
			if (handle == IntPtr.Zero) return;
			var error = FwpmEngineClose0(handle);
			if (error != 0)
				throw new NetworkWfpPolicyCleanupException(new NetworkPlatformError(
					"windows-wfp",
					unchecked((int)error),
					0,
					"wfp-engine-close"));
		}
	}

	private sealed class NativeMemoryScope : IDisposable
	{
		private readonly List<IntPtr> _items = [];

		public IntPtr String(string value) => Track(Marshal.StringToHGlobalUni(value));
		public IntPtr UInt64(ulong value)
		{
			var pointer = Track(Marshal.AllocHGlobal(sizeof(ulong)));
			Marshal.WriteInt64(pointer, unchecked((long)value));
			return pointer;
		}
		public IntPtr Bytes(byte[] value)
		{
			var pointer = Track(Marshal.AllocHGlobal(value.Length));
			Marshal.Copy(value, 0, pointer, value.Length);
			return pointer;
		}
		public IntPtr Structure<T>(T value) where T : struct
		{
			var pointer = Track(Marshal.AllocHGlobal(Marshal.SizeOf<T>()));
			Marshal.StructureToPtr(value, pointer, false);
			return pointer;
		}
		public IntPtr Array<T>(T[] values) where T : struct
		{
			var size = Marshal.SizeOf<T>();
			var pointer = Track(Marshal.AllocHGlobal(checked(size * values.Length)));
			for (var index = 0; index < values.Length; index++)
				Marshal.StructureToPtr(values[index], IntPtr.Add(pointer, index * size), false);
			return pointer;
		}
		private IntPtr Track(IntPtr pointer)
		{
			_items.Add(pointer);
			return pointer;
		}
		public void Dispose()
		{
			for (var index = _items.Count - 1; index >= 0; index--)
				Marshal.FreeHGlobal(_items[index]);
			_items.Clear();
		}
	}

	private enum FwpDataType : int { UInt8 = 1, UInt16 = 2, UInt32 = 3, UInt64 = 4, ByteArray16 = 11, ByteBlob = 12 }

	[StructLayout(LayoutKind.Sequential)]
	private readonly struct FwpmDisplayData(IntPtr name, IntPtr description)
	{
		public readonly IntPtr Name = name;
		public readonly IntPtr Description = description;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct FwpmSession
	{
		public Guid SessionKey;
		public FwpmDisplayData DisplayData;
		public uint Flags;
		public uint TransactionWaitTimeout;
		public uint ProcessId;
		public IntPtr Sid;
		public IntPtr Username;
		public int KernelMode;
	}

	[StructLayout(LayoutKind.Sequential)]
	private readonly struct FwpByteBlob(uint size, IntPtr data)
	{
		public readonly uint Size = size;
		public readonly IntPtr Data = data;
	}

	[StructLayout(LayoutKind.Sequential)]
	private readonly struct FwpValue(FwpDataType type, IntPtr value)
	{
		public readonly FwpDataType Type = type;
		public readonly IntPtr Value = value;
	}

	[StructLayout(LayoutKind.Sequential)]
	private readonly struct FwpConditionValue(FwpDataType type, IntPtr value)
	{
		public readonly FwpDataType Type = type;
		public readonly IntPtr Value = value;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct FwpmNetworkConnectionPolicySetting
	{
		public int Type;
		public FwpValue Value;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct FwpmNetworkConnectionPolicySettings
	{
		public uint NumberOfSettings;
		public IntPtr Settings;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct FwpmProviderContext
	{
		public Guid ProviderContextKey;
		public FwpmDisplayData DisplayData;
		public uint Flags;
		public IntPtr ProviderKey;
		public FwpByteBlob ProviderData;
		public int Type;
		public IntPtr Context;
		public ulong ProviderContextId;
	}

	[StructLayout(LayoutKind.Sequential)]
	private readonly struct FwpmFilterCondition(Guid fieldKey, FwpConditionValue value)
	{
		public readonly Guid FieldKey = fieldKey;
		public readonly int MatchType = 0;
		public readonly FwpConditionValue ConditionValue = value;
	}

	[DllImport("fwpuclnt.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
	private static extern uint FwpmEngineOpen0(
		string? serverName,
		uint authnService,
		IntPtr authIdentity,
		in FwpmSession session,
		out IntPtr engineHandle);

	[DllImport("fwpuclnt.dll", ExactSpelling = true)]
	private static extern uint FwpmEngineClose0(IntPtr engineHandle);

	[DllImport("fwpuclnt.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
	private static extern uint FwpmGetAppIdFromFileName0(string fileName, out IntPtr appId);

	[DllImport("fwpuclnt.dll", ExactSpelling = true)]
	private static extern void FwpmFreeMemory0(ref IntPtr pointer);

	[DllImport("fwpuclnt.dll", ExactSpelling = true)]
	private static extern uint FwpmConnectionPolicyAdd0(
		IntPtr engineHandle,
		in FwpmProviderContext connectionPolicy,
		int ipVersion,
		ulong weight,
		uint numberOfFilterConditions,
		IntPtr filterConditions,
		IntPtr securityDescriptor);

	[DllImport("fwpuclnt.dll", ExactSpelling = true)]
	private static extern uint FwpmProviderContextGetByKey3(
		IntPtr engineHandle,
		in Guid key,
		out IntPtr providerContext);
}

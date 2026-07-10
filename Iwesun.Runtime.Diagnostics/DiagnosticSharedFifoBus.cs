using System.IO.MemoryMappedFiles;
using System.Runtime.Versioning;
using System.Text;
using Iwesun.Runtime.Data;

namespace Iwesun.Runtime.Diagnostics;

internal enum DiagnosticSharedFifoChannelKind
{
	Monitor = 1,
	Breakpoint = 2,
	Command = 3,
	State = 4
}

[SupportedOSPlatform("windows")]
internal sealed class DiagnosticSharedFifoBus : IDisposable
{
	private readonly DiagnosticSharedFifoChannel _monitor;
	private readonly DiagnosticSharedFifoChannel _breakpoint;
	private readonly DiagnosticSharedFifoChannel _command;
	private readonly DiagnosticSharedFifoChannel _state;

	private DiagnosticSharedFifoBus(DiagnosticSharedFifoChannel monitor, DiagnosticSharedFifoChannel breakpoint, DiagnosticSharedFifoChannel command, DiagnosticSharedFifoChannel state)
	{
		_monitor = monitor;
		_breakpoint = breakpoint;
		_command = command;
		_state = state;
	}

	public static DiagnosticSharedFifoBus? TryCreate(string scope)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(scope);
		try
		{
			var monitor = new DiagnosticSharedFifoChannel(BuildName(scope, "monitor"), capacityBytes: 1024 * 1024, maxItemBytes: 16 * 1024);
			var breakpoint = new DiagnosticSharedFifoChannel(BuildName(scope, "breakpoint"), capacityBytes: 512 * 1024, maxItemBytes: 16 * 1024);
			var command = new DiagnosticSharedFifoChannel(BuildName(scope, "command"), capacityBytes: 256 * 1024, maxItemBytes: 4 * 1024);
			var state = new DiagnosticSharedFifoChannel(BuildName(scope, "state"), capacityBytes: 256 * 1024, maxItemBytes: 4 * 1024);
			return new DiagnosticSharedFifoBus(monitor, breakpoint, command, state);
		}
		catch (IOException)
		{
			return null;
		}
		catch (UnauthorizedAccessException)
		{
			return null;
		}
		catch (PlatformNotSupportedException)
		{
			return null;
		}
	}

	public bool TryEnqueue(DiagnosticSharedFifoChannelKind kind, string payload)
	{
		return Select(kind).TryEnqueue(payload);
	}

	public bool TryDequeue(out string payload)
	{
		if (_breakpoint.TryDequeue(out payload))
		{
			return true;
		}

		return _monitor.TryDequeue(out payload);
	}

	public bool TryEnqueueCommand(RuntimeCommandFrame frame) =>
		_command.TryEnqueue(RuntimeInjectorFrameCodec.EncodeCommandFrame(frame));

	public bool TryEnqueueState(RuntimeStateFrame frame) =>
		_state.TryEnqueue(RuntimeInjectorFrameCodec.EncodeStateFrame(frame));

	public bool TryDequeueCommand(out RuntimeCommandFrame frame)
	{
		frame = default;
		if (!_command.TryDequeue(out var payload))
		{
			return false;
		}

		return RuntimeInjectorFrameCodec.TryDecodeCommandFrame(payload, out frame);
	}

	public bool TryDequeueState(out RuntimeStateFrame frame)
	{
		frame = default;
		if (!_state.TryDequeue(out var payload))
		{
			return false;
		}

		return RuntimeInjectorFrameCodec.TryDecodeStateFrame(payload, out frame);
	}

	public void Dispose()
	{
		_monitor.Dispose();
		_breakpoint.Dispose();
		_command.Dispose();
		_state.Dispose();
	}

	private DiagnosticSharedFifoChannel Select(DiagnosticSharedFifoChannelKind kind) =>
		kind switch
		{
			DiagnosticSharedFifoChannelKind.Breakpoint => _breakpoint,
			DiagnosticSharedFifoChannelKind.Command => _command,
			DiagnosticSharedFifoChannelKind.State => _state,
			_ => _monitor
		};

	private static string BuildName(string scope, string kind)
	{
		var stable = RuntimeSharedFifoNameHash.Compute(scope);
		return $"Local\\{stable}.{kind}";
	}
}

[SupportedOSPlatform("windows")]
internal sealed class DiagnosticSharedFifoChannel : IDisposable
{
	private const int HeaderMagic = 0x44534651; // DSFQ
	private const int HeaderVersion = 1;
	private const int OffsetMagic = 0;
	private const int OffsetVersion = 4;
	private const int OffsetWrite = 8;
	private const int OffsetRead = 12;
	private const int OffsetUsed = 16;
	private const int OffsetDropped = 20;
	private const int HeaderSize = 24;

	private readonly int _capacityBytes;
	private readonly int _maxItemBytes;
	private readonly int _mapSize;
	private readonly MemoryMappedFile _mmf;
	private readonly MemoryMappedViewAccessor _view;
	private readonly Mutex _mutex;

	public DiagnosticSharedFifoChannel(string baseName, int capacityBytes, int maxItemBytes)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(baseName);
		_capacityBytes = Math.Max(64 * 1024, capacityBytes);
		_maxItemBytes = Math.Clamp(maxItemBytes, 256, 64 * 1024);
		_mapSize = HeaderSize + _capacityBytes;
		_mmf = MemoryMappedFile.CreateOrOpen($"{baseName}.mmf", _mapSize, MemoryMappedFileAccess.ReadWrite);
		_view = _mmf.CreateViewAccessor(0, _mapSize, MemoryMappedFileAccess.ReadWrite);
		_mutex = new Mutex(false, $"{baseName}.mtx");
		InitializeHeader();
	}

	public bool TryEnqueue(string payload)
	{
		payload ??= string.Empty;
		var bytes = Encoding.UTF8.GetBytes(payload);
		if (bytes.Length > _maxItemBytes)
		{
			Array.Resize(ref bytes, _maxItemBytes);
		}

		var entryBytes = 4 + bytes.Length;
		if (entryBytes >= _capacityBytes)
		{
			return false;
		}

		return WithLock(() =>
		{
			var writePos = ReadInt32(OffsetWrite);
			var readPos = ReadInt32(OffsetRead);
			var usedBytes = ReadInt32(OffsetUsed);
			var dropped = ReadInt32(OffsetDropped);

			while (_capacityBytes - usedBytes < entryBytes && usedBytes > 0)
			{
				var evictedLength = ReadInt32Ring(readPos);
				if (evictedLength <= 0 || evictedLength > _maxItemBytes)
				{
					writePos = 0;
					readPos = 0;
					usedBytes = 0;
					break;
				}

				var evictedBytes = 4 + evictedLength;
				readPos = Advance(readPos, evictedBytes);
				usedBytes -= evictedBytes;
				dropped++;
			}

			if (_capacityBytes - usedBytes < entryBytes)
			{
				return false;
			}

			WriteInt32Ring(writePos, bytes.Length);
			WriteBytesRing(Advance(writePos, 4), bytes);
			writePos = Advance(writePos, entryBytes);
			usedBytes += entryBytes;

			WriteInt32(OffsetWrite, writePos);
			WriteInt32(OffsetRead, readPos);
			WriteInt32(OffsetUsed, usedBytes);
			WriteInt32(OffsetDropped, dropped);
			return true;
		});
	}

	public bool TryDequeue(out string payload)
	{
		var localPayload = string.Empty;
		var ok = WithLock(() =>
		{
			var writePos = ReadInt32(OffsetWrite);
			var readPos = ReadInt32(OffsetRead);
			var usedBytes = ReadInt32(OffsetUsed);
			if (usedBytes <= 0)
			{
				return false;
			}

			var length = ReadInt32Ring(readPos);
			if (length <= 0 || length > _maxItemBytes || usedBytes < 4 + length)
			{
				WriteInt32(OffsetWrite, 0);
				WriteInt32(OffsetRead, 0);
				WriteInt32(OffsetUsed, 0);
				return false;
			}

			var bytes = new byte[length];
			ReadBytesRing(Advance(readPos, 4), bytes);
			readPos = Advance(readPos, 4 + length);
			usedBytes -= 4 + length;
			if (usedBytes == 0)
			{
				writePos = 0;
				readPos = 0;
			}

			WriteInt32(OffsetWrite, writePos);
			WriteInt32(OffsetRead, readPos);
			WriteInt32(OffsetUsed, usedBytes);
			localPayload = Encoding.UTF8.GetString(bytes);
			return true;
		});
		payload = ok ? localPayload : string.Empty;
		return ok;
	}

	public void Dispose()
	{
		_view.Dispose();
		_mmf.Dispose();
		_mutex.Dispose();
	}

	private bool WithLock(Func<bool> action)
	{
		var acquired = false;
		try
		{
			acquired = _mutex.WaitOne(TimeSpan.FromMilliseconds(10));
			if (!acquired)
			{
				return false;
			}

			return action();
		}
		catch (AbandonedMutexException)
		{
			return action();
		}
		finally
		{
			if (acquired)
			{
				_mutex.ReleaseMutex();
			}
		}
	}

	private void InitializeHeader()
	{
		WithLock(() =>
		{
			var magic = ReadInt32(OffsetMagic);
			var version = ReadInt32(OffsetVersion);
			if (magic == HeaderMagic && version == HeaderVersion)
			{
				return true;
			}

			WriteInt32(OffsetMagic, HeaderMagic);
			WriteInt32(OffsetVersion, HeaderVersion);
			WriteInt32(OffsetWrite, 0);
			WriteInt32(OffsetRead, 0);
			WriteInt32(OffsetUsed, 0);
			WriteInt32(OffsetDropped, 0);
			return true;
		});
	}

	private int Advance(int position, int delta)
	{
		var next = position + delta;
		if (next >= _capacityBytes)
		{
			next -= _capacityBytes;
		}

		return next;
	}

	private int ReadInt32(int offset) => _view.ReadInt32(offset);

	private void WriteInt32(int offset, int value) => _view.Write(offset, value);

	private int ReadInt32Ring(int ringPosition)
	{
		var bytes = new byte[4];
		ReadBytesRing(ringPosition, bytes);
		return BitConverter.ToInt32(bytes, 0);
	}

	private void WriteInt32Ring(int ringPosition, int value)
	{
		var bytes = BitConverter.GetBytes(value);
		WriteBytesRing(ringPosition, bytes);
	}

	private void ReadBytesRing(int ringPosition, byte[] destination)
	{
		var first = Math.Min(destination.Length, _capacityBytes - ringPosition);
		_view.ReadArray(HeaderSize + ringPosition, destination, 0, first);
		var remain = destination.Length - first;
		if (remain > 0)
		{
			_view.ReadArray(HeaderSize, destination, first, remain);
		}
	}

	private void WriteBytesRing(int ringPosition, byte[] source)
	{
		var first = Math.Min(source.Length, _capacityBytes - ringPosition);
		_view.WriteArray(HeaderSize + ringPosition, source, 0, first);
		var remain = source.Length - first;
		if (remain > 0)
		{
			_view.WriteArray(HeaderSize, source, first, remain);
		}
	}
}

internal static class RuntimeSharedFifoNameHash
{
	public static string Compute(string value)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(value);
		var normalized = value.Trim().ToLowerInvariant();
		ulong hash = 14695981039346656037UL;
		const ulong prime = 1099511628211UL;
		foreach (var ch in normalized)
		{
			hash ^= ch;
			hash *= prime;
		}

		return $"Iwesun.Runtime.Diag.{hash:x16}";
	}
}

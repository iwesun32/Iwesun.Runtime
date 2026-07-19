using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Iwesun.Runtime.Diagnostics;

public sealed class RuntimeDiagnosticsMonitor : BackgroundService
{
	private const int MaxCommandBytes = 16 * 1024 * 1024;
	private readonly RuntimeDiagnosticHub _hub;
	private readonly DiagnosticSwitchboardConfigStore _configStore;
	private readonly RuntimeNamedPipeAccessPolicy _accessPolicy;
	private readonly ILogger<RuntimeDiagnosticsMonitor> _logger;
	private readonly ConcurrentDictionary<int, NamedPipeServerStream> _activePipes = new();
	private int _nextPipeId;
	private volatile bool _isRunning;
	private string _pipeName = DiagnosticSwitchboardCompiledConfig.DefaultRuntimeDiagnosticsPipeName;

	public RuntimeDiagnosticsMonitor(
		RuntimeDiagnosticHub hub,
		DiagnosticSwitchboardConfigStore configStore,
		RuntimeNamedPipeAccessOptions accessOptions,
		ILogger<RuntimeDiagnosticsMonitor> logger)
	{
		_hub = hub ?? throw new ArgumentNullException(nameof(hub));
		_configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
		_accessPolicy = new RuntimeNamedPipeAccessPolicy(
			accessOptions ?? throw new ArgumentNullException(nameof(accessOptions)));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	public bool IsRunning => _isRunning;

	public string PipeName => _pipeName;

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		_pipeName = ResolvePipeName();
		_isRunning = true;
		_logger.LogInformation("Runtime diagnostics monitor thread starting on {PipeName}", _pipeName);

		try
		{
			while (!stoppingToken.IsCancellationRequested)
			{
				var server = RuntimeNamedPipeServerFactory.CreateDiagnosticsServer(_pipeName, _accessPolicy);
				var pipeId = Interlocked.Increment(ref _nextPipeId);
				_activePipes[pipeId] = server;

				try
				{
					await server.WaitForConnectionAsync(stoppingToken);
					_ = HandleClientAsync(pipeId, server, stoppingToken);
				}
				catch (OperationCanceledException)
				{
					_activePipes.TryRemove(pipeId, out _);
					await server.DisposeAsync();
					break;
				}
				catch (IOException) when (stoppingToken.IsCancellationRequested || !_isRunning)
				{
					_activePipes.TryRemove(pipeId, out _);
					await server.DisposeAsync();
					break;
				}
				catch (Exception ex)
				{
					_activePipes.TryRemove(pipeId, out _);
					_logger.LogWarning(ex, "Runtime diagnostics monitor accept failed on {PipeName}", _pipeName);
					await server.DisposeAsync();
					await DelayRetryAsync(stoppingToken);
				}
			}
		}
		finally
		{
			_isRunning = false;
		}
	}

	public override async Task StopAsync(CancellationToken cancellationToken)
	{
		_isRunning = false;
		foreach (var pipe in _activePipes.Values)
		{
			try { await pipe.DisposeAsync(); } catch { }
		}

		await DiagnosticSwitchboard.ShutdownAsync();
		await base.StopAsync(cancellationToken);
	}

	private string ResolvePipeName()
	{
		try
		{
			var config = _configStore.Load();
			return string.IsNullOrWhiteSpace(config.RuntimeDiagnosticsPipeName)
				? DiagnosticSwitchboardCompiledConfig.DefaultRuntimeDiagnosticsPipeName
				: config.RuntimeDiagnosticsPipeName.Trim();
		}
		catch
		{
			return DiagnosticSwitchboardCompiledConfig.DefaultRuntimeDiagnosticsPipeName;
		}
	}

	private async Task HandleClientAsync(int pipeId, NamedPipeServerStream pipe, CancellationToken ct)
	{
		try
		{
			while (!ct.IsCancellationRequested)
			{
				RuntimeDiagnosticFrame? requestFrame = null;
				RuntimeDiagnosticFrame responseFrame;
				var stopwatch = Stopwatch.StartNew();
				try
				{
					requestFrame = await RuntimeFramePipeCodec.ReadAsync(pipe, MaxCommandBytes, ct);

					if (!string.Equals(requestFrame.Header.Schema, RuntimeDiagnosticProtocol.V2Schema, StringComparison.OrdinalIgnoreCase)
						&& !string.Equals(requestFrame.Header.Schema, RuntimeDiagnosticProtocol.V3Schema, StringComparison.OrdinalIgnoreCase))
						throw new InvalidOperationException($"Unsupported schema: {requestFrame.Header.Schema}");

					if (!string.Equals(requestFrame.Header.FrameType, "request", StringComparison.OrdinalIgnoreCase))
						throw new InvalidOperationException($"Unsupported frameType: {requestFrame.Header.FrameType}");

					responseFrame = await _hub.ExecuteFrameAsync(requestFrame, ct);
				}
				catch (Exception ex)
				{
					responseFrame = BuildFrameErrorResponse(requestFrame, ex);
				}
				stopwatch.Stop();

				await RuntimeFramePipeCodec.WriteAsync(pipe, responseFrame, ct);
				break;
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (IOException)
		{
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Runtime diagnostics monitor handler failed on {PipeName}", _pipeName);
			try
			{
				await RuntimeFramePipeCodec.WriteAsync(pipe, BuildFrameErrorResponse(null, ex), CancellationToken.None);
			}
			catch
			{
				// The client may already have disconnected.
			}
		}
		finally
		{
			_activePipes.TryRemove(pipeId, out _);

			try { pipe.Disconnect(); } catch { }
			await pipe.DisposeAsync();
		}
	}

	private static RuntimeDiagnosticFrame BuildFrameErrorResponse(RuntimeDiagnosticFrame? request, Exception ex)
	{
		return new RuntimeDiagnosticFrame
		{
			Header = new RuntimeDiagnosticFrameHeader
			{
				Schema = RuntimeDiagnosticProtocol.V2Schema,
				FrameType = "response",
				Category = request?.Header.Category ?? "control",
				Operation = request?.Header.Operation ?? "error",
				RequestId = request?.Header.RequestId,
				CorrelationId = request?.Header.RequestId ?? request?.Header.CorrelationId,
				Timestamp = DateTimeOffset.UtcNow,
				Source = "runtime",
				Destination = request?.Header.Source
			},
			Status = new RuntimeDiagnosticFrameStatus
			{
				Ok = false,
				Code = "ERROR",
				Message = $"{ex.GetType().Name}: {ex.Message}",
				Retryable = false
			},
			ExtStatus = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
			{
				["module"] = JsonSerializer.SerializeToElement("diagnostics")
			}
		};
	}

	private static async Task DelayRetryAsync(CancellationToken ct)
	{
		try
		{
			await Task.Delay(TimeSpan.FromSeconds(2), ct);
		}
		catch (OperationCanceledException)
		{
		}
	}
}

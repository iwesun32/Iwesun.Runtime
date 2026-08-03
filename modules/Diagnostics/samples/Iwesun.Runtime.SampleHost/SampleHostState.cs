namespace Iwesun.Runtime.SampleHost;

public sealed class SampleHostState
{
	private readonly object _gate = new();
	private int _iteration;
	private string _currentPhase = "created";

	public event EventHandler<SampleHostStateChangedEventArgs>? StateChanged;

	public string CurrentPhase
	{
		get
		{
			lock (_gate)
			{
				return _currentPhase;
			}
		}
	}

	public int Iteration
	{
		get
		{
			lock (_gate)
			{
				return _iteration;
			}
		}
	}

	public bool IsPaused
	{
		get
		{
			lock (_gate)
			{
				return _isPaused;
			}
		}
		set
		{
			lock (_gate)
			{
				_isPaused = value;
			}
		}
	}

	public bool FaultRequested
	{
		get
		{
			lock (_gate)
			{
				return _faultRequested;
			}
		}
		set
		{
			lock (_gate)
			{
				_faultRequested = value;
			}
		}
	}

	public DateTimeOffset UpdatedAt
	{
		get
		{
			lock (_gate)
			{
				return _updatedAt;
			}
		}
		private set
		{
			lock (_gate)
			{
				_updatedAt = value;
			}
		}
	}

	private bool _isPaused;
	private bool _faultRequested;
	private DateTimeOffset _updatedAt = DateTimeOffset.UtcNow;

	public void Advance()
	{
		lock (_gate)
		{
			_iteration++;
		}
		SetPhase("running");
	}

	public void Pause()
	{
		IsPaused = true;
		SetPhase("paused");
	}

	public void Resume()
	{
		IsPaused = false;
		SetPhase("running");
	}

	public void Reset()
	{
		lock (_gate)
		{
			_iteration = 0;
			_isPaused = false;
			_faultRequested = false;
		}
		SetPhase("reset");
	}

	public void RequestFault()
	{
		FaultRequested = true;
		SetPhase("fault-requested");
	}

	public void ClearFault()
	{
		FaultRequested = false;
		SetPhase("running");
	}

	public SampleHostStateSnapshot Snapshot()
	{
		lock (_gate)
		{
			return new SampleHostStateSnapshot(_currentPhase, _iteration, _isPaused, _faultRequested, _updatedAt);
		}
	}

	private void SetPhase(string nextPhase)
	{
		SampleHostStateChangedEventArgs? args;
		lock (_gate)
		{
			var previousPhase = _currentPhase;
			_currentPhase = nextPhase;
			_updatedAt = DateTimeOffset.UtcNow;
			args = new SampleHostStateChangedEventArgs(previousPhase, nextPhase, _iteration, _updatedAt);
		}

		StateChanged?.Invoke(this, args);
	}
}

public sealed record SampleHostStateChangedEventArgs(
	string PreviousPhase,
	string CurrentPhase,
	int Iteration,
	DateTimeOffset UpdatedAt);

public sealed record SampleHostStateSnapshot(
	string CurrentPhase,
	int Iteration,
	bool IsPaused,
	bool FaultRequested,
	DateTimeOffset UpdatedAt);

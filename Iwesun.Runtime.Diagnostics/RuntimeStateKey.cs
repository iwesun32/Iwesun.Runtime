namespace Iwesun.Runtime.Diagnostics;

public enum RuntimeStateKey
{
	Start = 1,
	Working = 2,
	Stop = 3,
	WorkingIdle = 11,
	WorkingCollecting = 12,
	WorkingAnalyzing = 13,
	WorkingNetworkAccess = 14,
	StopRequested = 21,
	StopDraining = 22,
	StopCompleted = 23,
	StopTimeout = 24
}
using System.Net.Sockets;

namespace Iwesun.Runtime.Networks;

public readonly record struct BinaryNetworkError(byte Kind, int Code, int HResult)
{
	public static BinaryNetworkError None => new(0, 0, 0);

	public static BinaryNetworkError FromException(Exception? exception, bool cancelled, bool timedOut)
	{
		if (cancelled)
		{
			return new BinaryNetworkError(2, 0, 0);
		}

		if (timedOut)
		{
			return new BinaryNetworkError(1, 0, 0);
		}

		if (exception == null)
		{
			return None;
		}

		var code = exception is SocketException socketException
			? (int)socketException.SocketErrorCode
			: exception.HResult;
		return new BinaryNetworkError(3, code, exception.HResult);
	}
}

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Iwesun.Runtime.Diagnostics;

internal sealed class RuntimeWindowsServiceOptionsSetup(
	IOptions<RuntimeWindowsServiceOptions> runtimeOptions)
	: IConfigureOptions<WindowsServiceLifetimeOptions>
{
	public void Configure(WindowsServiceLifetimeOptions options)
	{
		var configured = runtimeOptions.Value;
		if (!string.IsNullOrWhiteSpace(configured.ServiceName))
			options.ServiceName = configured.ServiceName;
	}
}

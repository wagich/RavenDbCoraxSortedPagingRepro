using System.Runtime.CompilerServices;
using Raven.Embedded;
using Raven.TestDriver;

namespace CoraxSortedPagingRepro;

/// <summary>
/// Configures the shared embedded test server once, at assembly load, before any
/// <c>GetDocumentStore</c> starts it. The server is allowed to run without a license; if a developer
/// license sits at <c>~/ravendb-license.json</c> it is used.
/// </summary>
internal static class TestServer
{
	[ModuleInitializer]
	public static void Init() => new Configurator().Apply();

	private sealed class Configurator : RavenTestDriver
	{
		public void Apply()
		{
			var licensePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "ravendb-license.json");
			var licensing = new ServerOptions.LicensingOptions { ThrowOnInvalidOrMissingLicense = false };
			if (File.Exists(licensePath))
			{
				licensing.LicensePath = licensePath;
			}
			ConfigureServer(new TestServerOptions { Licensing = licensing });
		}
	}
}

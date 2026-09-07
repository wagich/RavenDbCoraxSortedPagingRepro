using Xunit;

namespace CoraxSortedPagingRepro;

/// <summary>An equality assertion whose failure message names the page and the setup that produced it.</summary>
internal static class Check
{
	public static void Page(string expected, string actual, string context)
		=> Assert.True(expected == actual, $"expected {expected} but got {actual} — {context}");
}

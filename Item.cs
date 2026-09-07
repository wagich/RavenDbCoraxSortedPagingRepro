namespace CoraxSortedPagingRepro;

/// <summary>The document under test: a category to filter on and an ISO date string to sort by.</summary>
public class Item
{
	public string Id { get; set; } = default!;
	public string Category { get; set; } = default!;
	public string Date { get; set; } = default!;
	public string Name { get; set; } = default!;
}

/// <summary>
/// The four documents every test stores: A is the newest, D the oldest. A storage order is spelled
/// as a string of their names, e.g. <c>"BDAC"</c>.
/// </summary>
public static class Fixture
{
	public static readonly string[] Names = ["A", "B", "C", "D"];
	public static readonly string[] Dates = ["2026-09-01", "2026-08-01", "2026-07-01", "2026-06-01"];

	public static IEnumerable<Item> ItemsIn(string storageOrder)
	{
		foreach (var name in storageOrder)
		{
			var i = Array.IndexOf(Names, name.ToString());
			yield return new Item { Id = $"items/{Names[i]}", Category = "News", Date = Dates[i], Name = Names[i] };
		}
	}
}

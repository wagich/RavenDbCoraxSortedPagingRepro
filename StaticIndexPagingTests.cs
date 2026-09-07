using Raven.Client.Documents;
using Raven.Client.Documents.Indexes;
using Raven.Client.ServerWide.Operations;
using Raven.TestDriver;
using Xunit;

namespace CoraxSortedPagingRepro;

/// <summary>
/// The defect. A static index over documents whose sort field holds ISO date strings; the query
/// filters on an equality, orders by the date descending, and pages with skip/take. On Corax the page
/// is not the first <c>take</c> of the sorted matches: it is the last <c>skip + take</c> matches in
/// storage order, sorted, then skipped. So the page is whatever was stored last, and it is right only
/// when the newest-dated documents happen to be the last stored.
/// </summary>
public class StaticIndexPagingTests : RavenTestDriver
{
	public class Items_ByCategory : AbstractIndexCreationTask<Item>
	{
		public Items_ByCategory()
		{
			Map = items => from item in items select new { item.Category, item.Date };
		}
	}

	[Theory]
	[InlineData("ABCD")]
	[InlineData("BDAC")]
	[InlineData("DCBA")]
	public async Task First_page_is_the_newest_documents_whatever_the_storage_order(string storageOrder)
	{
		using var store = GetDocumentStore();
		var index = new Items_ByCategory();
		index.Configuration["Indexing.Static.SearchEngineType"] = "Corax";
		await index.ExecuteAsync(store);

		using (var session = store.OpenAsyncSession())
		{
			foreach (var item in Fixture.ItemsIn(storageOrder))
			{
				await session.StoreAsync(item);
			}
			await session.SaveChangesAsync();
		}
		WaitForIndexing(store);

		var build = await store.Maintenance.Server.SendAsync(new GetBuildNumberOperation());
		var context = $"static index on Corax, stored {storageOrder}, server {build.FullVersion}";

		Check.Page("A,B,C,D", await Page(store, skip: 0, take: 10), $"ordering without paging, {context}");
		Check.Page("A,B", await Page(store, skip: 0, take: 2), $"limit 0, 2, {context}");
		Check.Page("C,D", await Page(store, skip: 2, take: 2), $"limit 2, 2, {context}");
	}

	static async Task<string> Page(IDocumentStore store, int skip, int take)
	{
		using var session = store.OpenAsyncSession();
		var items = await session.Query<Item, Items_ByCategory>()
			.Where(item => item.Category == "News")
			.OrderByDescending(item => item.Date)
			.Skip(skip)
			.Take(take)
			.ToListAsync();
		return string.Join(",", items.Select(item => item.Name));
	}
}

using Raven.Client.Documents;
using Raven.Client.Documents.Operations.Indexes;
using Raven.Client.ServerWide;
using Raven.Client.ServerWide.Operations;
using Raven.TestDriver;
using Xunit;

namespace CoraxSortedPagingRepro;

/// <summary>
/// The contrast: the same documents, the same storage orders and the same query, but as a collection
/// query served by a Corax auto index instead of the static index. The test checks which engine the
/// auto index actually got.
/// </summary>
public class AutoIndexPagingTests : RavenTestDriver
{
	[Theory]
	[InlineData("ABCD")]
	[InlineData("BDAC")]
	[InlineData("DCBA")]
	public async Task First_page_is_the_newest_documents_whatever_the_storage_order(string storageOrder)
	{
		using var store = GetDocumentStore();

		// A database of our own, so the auto-index engine can be pinned to Corax.
		var database = $"{store.Database}-Corax-{storageOrder}";
		await store.Maintenance.Server.SendAsync(new CreateDatabaseOperation(new DatabaseRecord(database)
		{
			Settings = { ["Indexing.Auto.SearchEngineType"] = "Corax" },
		}));
		try
		{
			using (var session = store.OpenAsyncSession(database))
			{
				foreach (var item in Fixture.ItemsIn(storageOrder))
				{
					await session.StoreAsync(item);
				}
				await session.SaveChangesAsync();
			}

			// The first query creates the auto index; wait for it before reading the pages.
			await Page(store, database, skip: 0, take: 10);
			WaitForIndexing(store, database);

			var indexNames = await store.Maintenance.ForDatabase(database).SendAsync(new GetIndexNamesOperation(0, 10));
			var autoIndex = indexNames.Single(name => name.StartsWith("Auto/"));
			var stats = await store.Maintenance.ForDatabase(database).SendAsync(new GetIndexStatisticsOperation(autoIndex));
			var build = await store.Maintenance.Server.SendAsync(new GetBuildNumberOperation());
			var context = $"{autoIndex} on {stats.SearchEngineType}, stored {storageOrder}, server {build.FullVersion}";

			Assert.True(stats.SearchEngineType.ToString() == "Corax", $"expected the auto index on Corax but it is on {stats.SearchEngineType}");
			Check.Page("A,B,C,D", await Page(store, database, skip: 0, take: 10), $"ordering without paging, {context}");
			Check.Page("A,B", await Page(store, database, skip: 0, take: 2), $"limit 0, 2, {context}");
			Check.Page("C,D", await Page(store, database, skip: 2, take: 2), $"limit 2, 2, {context}");
		}
		finally
		{
			await store.Maintenance.Server.SendAsync(new DeleteDatabasesOperation(database, hardDelete: true));
		}
	}

	static async Task<string> Page(IDocumentStore store, string database, int skip, int take)
	{
		using var session = store.OpenAsyncSession(database);
		var items = await session.Query<Item>()
			.Where(item => item.Category == "News")
			.OrderByDescending(item => item.Date)
			.Skip(skip)
			.Take(take)
			.ToListAsync();
		return string.Join(",", items.Select(item => item.Name));
	}
}

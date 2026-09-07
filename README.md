# Corax truncates textual sort candidates before resolving equal prefixes

## Summary

A filtered, sorted and paged query against a Corax static index can return the wrong page when the sort values share their first six encoded bytes. The result depends on index ingestion order rather than the complete sort values.

This repository reproduces the problem with RavenDB.TestDriver and server 6.2.17 (build 62092). The same failure was also reproduced from the current `release/v6.2` source at 6.2.20-custom-62 on 2026-09-07.

## Reproduction

Four documents use ISO date strings, with `A` newest and `D` oldest:

| Name | Category | Date         |
|------|----------|--------------|
| A    | News     | `2026-09-01` |
| B    | News     | `2026-08-01` |
| C    | News     | `2026-07-01` |
| D    | News     | `2026-06-01` |

The ISO strings are intentional rather than an artificial simplification. The same failure occurs when `Date` is a `NodaTime.LocalDate` configured with `RavenDB.Client.NodaTime` 6.2.13. That integration serializes `LocalDate` in the same date-only ISO form. Keeping this reproduction on strings removes an unrelated dependency while preserving the failing indexed representation.

The same experiment passes with `System.DateTime`, `System.DateOnly`, `NodaTime.Instant`, and `NodaTime.LocalDateTime`. Raven recognizes the date-time forms as `DateTime` values while executing the static map. Native `DateOnly` also has a dedicated indexing path. In both cases Raven writes numeric ticks, marks the index field as temporal, and uses Corax's numeric sorter. A serialized `LocalDate` remains a date-only string in this map, so it uses the affected textual `Sequence` sorter.

Converting the string inside the static index also avoids the bug: `Date = AsDateOnly(item.Date)` passes every storage-order case while the document property and query remain string-typed. This is a viable workaround because the indexed field then uses the native `DateOnly` path and tick ordering.

A Corax static index maps `Category` and `Date`. The query is:

```rql
from index 'Items/ByCategory'
where Category = 'News'
order by Date desc
limit 0, 2
```

Expected: `A,B`.

Actual results depend on storage order:

| Stored in order | Static index result | Auto index control |
|-----------------|---------------------|--------------------|
| `A B C D`       | **`C,D`**           | `A,B`              |
| `B D A C`       | **`A,C`**           | `A,B`              |
| `D C B A`       | `A,B`               | `A,B`              |

The unlimited static-index query returns the correct `A,B,C,D` order. The defect appears when a finite `take` is applied.

Run with .NET 8 or later:

```powershell
dotnet test
```

Expected test result demonstrating the bug: 6 total, 4 passed, 2 failed. The failures are the `ABCD` and `BDAC` static-index cases.

## Root cause

The query uses Corax's textual `Sequence` sorter. In:

```text
src/Corax/Querying/Matches/SortingMatches/SortingMatch.Comparers.cs
SortingMatch<TInner>.EntryComparerByTerm.SortByTerms
```

Corax constructs an approximate sort key from at most the first six bytes of each encoded term:

```csharp
Memory.Copy(&l, batchTerms[i].Address + 1,
	Math.Min(6, batchTerms[i].Length - 1));
```

It then performs these operations in this order:

```csharp
Sort.Run(buffer);

if (match._take >= 0 && buffer.Length > match._take)
	buffer = buffer[..match._take];

MaybeBreakTies(buffer, tieBreaker, isDescending);
```

All fixture values begin with the same six-byte prefix, `2026-0`. The approximate keys therefore tie. The candidate position embedded in the approximate key determines which two entries survive `_take`; only those entries are subsequently compared using the complete terms. This produces the observed ingestion-order-dependent page.

This is not specific to dates. Any textual sort values sharing the first six encoded bytes can cross the page boundary before their tie is resolved.

## Validated correction

As a diagnostic, moving `MaybeBreakTies(buffer, tieBreaker, isDescending)` before the `_take` truncation made all six tests pass against 6.2.20-custom-62. A more targeted implementation could instead include the entire approximate-key tie group crossing the cutoff, resolve that group with the full comparer, and then truncate.

The generic numerical heap sorter was tested separately with descending top-N input and passed; it is not the cause.

## Additional controls

During investigation, these variations returned correct results:

- the same query through a Corax auto index;
- the static-index query without `limit`;
- a range clause or no clause instead of the equality clause;
- textual values without a shared six-byte prefix;
- Lucene static and auto indexes;
- rewriting the documents after initial indexing.

## Files

- `StaticIndexPagingTests.cs`: failing Corax static-index reproduction.
- `AutoIndexPagingTests.cs`: passing Corax auto-index control.
- `Item.cs`: document and fixtures.
- `TestServer.cs`: embedded server configuration.
- `Check.cs`: assertion with setup details.

# Migration: token code overflow split point moves from 128 to 256

## Who this affects

Only databases **this server already wrote token search rows into**, before the change described here.

A database provisioned from `Resources/97.sql` and populated by `microsoft/fhir-server` is unaffected — that
server always split at the column width, which is what this change adopts. A brand-new database is
unaffected. There is nothing to run for either.

## What changed

`dbo.TokenSearchParam.Code` is `VARCHAR(256)`. A token code longer than the column is stored split: the
leading characters in `Code`, the remainder in `CodeOverflow`.

Ignixa's row generators used to divide that value at a hard-coded **128** rather than at the column's
declared width. They now divide at the width, which is where `microsoft/fhir-server` divides it and where
the search compiler has always expected to find it.

The same correction applies to `TokenStringCompositeSearchParam.TextOverflow2`, which used to hold the
*remainder* of an over-long string component and now holds the *whole* value, matching
`StringSearchParam.TextOverflow`.

## The consequence for existing rows

Rows written under the old convention hold `Code` = the first 128 characters. The read path now assumes the
first 256. The two disagree for one band of values:

| Stored token code length | Findable after this change | Why |
|---|---|---|
| ≤ 128 | yes | stored whole in `Code` under both conventions |
| **129 – 256** | **no** | search compares the whole value against `Code`, which holds only 128 characters |
| > 256 | yes | search reassembles `Code + CodeOverflow`, which is split-point agnostic |

The failure is silent: an affected search returns an empty result set, not an error.

`TokenStringCompositeSearchParam` rows with a string component longer than 256 characters are affected the
same way, because `TextOverflow2` holds a remainder that the read path now compares as a whole value.

## What to do

Reindex the affected resources. Any operation that re-runs search parameter extraction and rewrites the
search parameter tables is sufficient — the row generators produce the new layout on the next write, and a
row rewritten under the new convention needs nothing further.

Scoping the work: token codes longer than 128 characters are uncommon in practice. Before reindexing
wholesale, this identifies whether the affected band exists at all.

```sql
SELECT COUNT(*)
FROM dbo.TokenSearchParam
WHERE CodeOverflow IS NOT NULL
  AND LEN(Code) = 128
  AND LEN(Code) + LEN(CodeOverflow) <= 256;
```

A count of zero means no row falls in the affected band and no reindex is required. `LEN(Code) = 128` is
what identifies a row written under the old convention: under the new one a row with a non-null overflow
always has `LEN(Code) = 256`.

The composite equivalent:

```sql
SELECT COUNT(*)
FROM dbo.TokenStringCompositeSearchParam
WHERE TextOverflow2 IS NOT NULL
  AND LEN(Text2) = 128;
```

## Why the split moved up rather than the reader moving down

The package README offers adoption of an existing `microsoft/fhir-server` database without data migration.
That server splits at the column width. Holding Ignixa at 128 would have made every token code over 128
characters in such a database unfindable — a permanently wrong answer against a far larger population of
data than the one this note covers.

A reader tolerant of both conventions (`Code = @code OR Code + CodeOverflow = @code`) was considered and
rejected: it would carry an `OR` on the hottest search path in the server, permanently, to serve a
transitional state.

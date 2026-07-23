# Legacy SQL differential report

185 captured searches, each compiled and compared against the SQL the shipping engine executed.

## Summary

| Verdict | Count |
|---|---:|
| NotCompiled | 4 |
| Match | 69 |
| CompilerDoesLess | 45 |
| CompilerDoesMore | 14 |
| Divergent | 53 |

## Gaps -- queries the compiler cannot express

### build:InvalidSearchOperationException (3)

- **3x** The _include search is missing the type to search.
  - `/Patient?_id=ignixa-inc-pat1&_include=*`
  - `/Patient?_id=ignixa-inco-p1&_include=*&_revinclude=*`
  - `/Patient?_id=ignixa-inco-p1&_include=*&_revinclude=*&_includesCount=1`

### Lower (1)

- **1x** A resource-column predicate ('_id') reached the leaf/composite dispatch — only Lower.Run's top-level extraction pass (via ResourceColumnLoweringRule) handles these. Guarding here, at the dispatch choke point, covers every caller of Lower/LowerComposite structurally. Throwing rather than routing a resource column into an unrelated table, which would silently produce a wrong-scope or always-empty match.
  - `/Observation?identifier=http://ignixa.io/testscript/suite/token%7C&_id:not=ignixa-tok-o1,ignixa-tok-o2&_count=100`

## Divergences -- compiled, but asks the database for something different

### CompilerDoesLess: `/DocumentReference/$docref?patient=ignixa-docref-pat0`

Only the shipping engine does:
- `filter ReferenceResourceTypeId = <v> (x4)`
- `filter col:ReferenceResourceTypeId is-null`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op distinct`
- `legacy: op inner-join`
- `legacy: op or (x4)`
- `legacy: op order-by`
- `legacy: op top`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte1  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = ReferenceSearchParam  ReferenceResourceId = @p ReferenceResourceTypeId = <n> ReferenceResourceTypeId = <n> ReferenceResourceTypeId = <n> ReferenceResourceTypeId = <n> ResourceTypeId = <n> SearchParamId = <n> col:ReferenceResourceTypeId is-null  [or,or,or,or]
cte1 = <-cte0  [distinct,order-by,top]

compiler:
select0 = <-cte0  [order-by]
cte0 = ReferenceSearchParam  ReferenceResourceId = @p ResourceTypeId = <n> SearchParamId = <n>  [distinct]
```

</details>

### CompilerDoesLess: `/DocumentReference/$docref?patient=ignixa-docref-pat0&type=http://loinc.org%7C55107-7`

Only the shipping engine does:
- `filter ReferenceResourceTypeId = <v> (x4)`
- `filter col:ReferenceResourceTypeId is-null`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op or (x4)`
- `legacy: op order-by`
- `legacy: op top`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte2  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = ReferenceSearchParam  ReferenceResourceId = @p ReferenceResourceTypeId = <n> ReferenceResourceTypeId = <n> ReferenceResourceTypeId = <n> ReferenceResourceTypeId = <n> ResourceTypeId = <n> SearchParamId = <n> col:ReferenceResourceTypeId is-null  [or,or,or,or]
cte1 = TokenSearchParam  <-cte0  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte2 = <-cte1  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = ReferenceSearchParam  ReferenceResourceId = @p ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### CompilerDoesLess: `/DocumentReference/$docref?patient=ignixa-docref-pat0&unknown=unknownvalue`

Only the shipping engine does:
- `filter ReferenceResourceTypeId = <v> (x4)`
- `filter col:ReferenceResourceTypeId is-null`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op distinct`
- `legacy: op inner-join`
- `legacy: op or (x4)`
- `legacy: op order-by`
- `legacy: op top`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte1  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = ReferenceSearchParam  ReferenceResourceId = @p ReferenceResourceTypeId = <n> ReferenceResourceTypeId = <n> ReferenceResourceTypeId = <n> ReferenceResourceTypeId = <n> ResourceTypeId = <n> SearchParamId = <n> col:ReferenceResourceTypeId is-null  [or,or,or,or]
cte1 = <-cte0  [distinct,order-by,top]

compiler:
select0 = <-cte0  [order-by]
cte0 = ReferenceSearchParam  ReferenceResourceId = @p ResourceTypeId = <n> SearchParamId = <n>  [distinct]
```

</details>

### CompilerDoesLess: `/HealthcareService?_id=ignixa-id-b&active=true`

Only the shipping engine does:
- `table Resource`
- `filter IsDeleted = <v>`
- `filter IsHistory = <v>`
- `filter ResourceTypeId = <v>`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op distinct`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte2  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = Resource  IsDeleted = <n> IsHistory = <n> ResourceId = @p ResourceTypeId = <n>
cte1 = TokenSearchParam  <-cte0  Code = @p ResourceTypeId = <n> SearchParamId = <n>  [correlate,correlate,exists]
cte2 = <-cte1  [distinct,order-by,top]

compiler:
select0 = Resource  <-cte0  ResourceId = @p  [correlate,correlate,inner-join,order-by]
cte0 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n>  [distinct]
```

</details>

### CompilerDoesLess: `/HealthcareService?_id=ignixa-id-c&active=true`

Only the shipping engine does:
- `table Resource`
- `filter IsDeleted = <v>`
- `filter IsHistory = <v>`
- `filter ResourceTypeId = <v>`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op distinct`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte2  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = Resource  IsDeleted = <n> IsHistory = <n> ResourceId = @p ResourceTypeId = <n>
cte1 = TokenSearchParam  <-cte0  Code = @p ResourceTypeId = <n> SearchParamId = <n>  [correlate,correlate,exists]
cte2 = <-cte1  [distinct,order-by,top]

compiler:
select0 = Resource  <-cte0  ResourceId = @p  [correlate,correlate,inner-join,order-by]
cte0 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n>  [distinct]
```

</details>

### CompilerDoesLess: `/Observation?code-value-quantity=http://loinc.org%7C8310-5$gt38%7Chttp://unitsofmeasure.org%7CCel&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-impsrch-suite&_count=100`

Only the shipping engine does:
- `table TokenQuantityCompositeSearchParam`
- `filter Code1 = <v>`
- `filter QuantityCodeId2 = <v>`
- `filter ResourceTypeId = <v>`
- `filter SearchParamId = <v>`
- `filter SingleValue2 > <v>`
- `filter SystemId1 = <v>`
- `filter SystemId2 = <v>`
- `filter col:SingleValue2 is-not-null`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`
- `legacy: op union-all`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte3  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = TokenQuantityCompositeSearchParam  Code1 = @p QuantityCodeId2 = @p ResourceTypeId = <n> SearchParamId = <n> SingleValue2 > @p SystemId1 = @p SystemId2 = @p col:SingleValue2 is-not-null
cte1 = TokenQuantityCompositeSearchParam  <-cte0  Code1 = @p HighValue2 > @p QuantityCodeId2 = @p ResourceTypeId = <n> SearchParamId = <n> SystemId1 = @p SystemId2 = @p  [union-all]
cte2 = TokenSearchParam  <-cte1  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte3 = <-cte2  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = TokenQuantityCompositeSearchParam  Code1 = @p HighValue2 > @p QuantityCodeId2 = @p ResourceTypeId = <n> SearchParamId = <n> SystemId1 = @p SystemId2 = @p  [distinct]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### CompilerDoesLess: `/Observation?code-value-string=162806009$Lorem%20ipsum%20dolor%20sit%20amet%20consectetur%20adipiscing%20elit.%20Ut%20eget%20ultricies%20justo.%20Maecenas%20bibendum%20convallis%20sodales.%20Vestibulum%20quis%20molestie%20dui.%20Nulla%20porta%20elementum%20tristique.%20Aenean%20neque%20libero%20convallis%20sit%20amet%20dui%20ullamcorper%20congue%20lacinia%20erat.%20Sed%20finibus%20ex%20ac%20massa%20tincidunt%20tristique.%20In%20sed%20auctor%20massa.%20Proin%20cursus%20porttitor%20arcu.%20Maecenas%20a%20leo%20nunc.%20Sed%20pretium%20porta%20volutpat.%20In%20aliquet%20tempor%20sapien%20vitae%20laoreet%20nisl%20tempor%20ac.%20Vestibulum%20lacus%20leo%20luctus%20vitae%20pharetra%20at%20tempus%20ac%20diam.%20Integer%20at%20dui%20eu%20dolor%20gravida%20vehicula.%20Phasellus%20malesuada%20elit%20orci%20quis%20maximus%20purus%20consectetur%20ac.%20In%20semper%20consequat%20augue%20sit%20amet%20ultricies.&identifier=http://ignixa.io/testscript/suite/composite%7C`

Only the shipping engine does:
- `filter col:Text2 like <v>`
- `filter col:TextOverflow2 is-not-null`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte2  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = TokenStringCompositeSearchParam  Code1 = @p ResourceTypeId = <n> SearchParamId = <n> col:Text2 like @p col:TextOverflow2 is-not-null col:TextOverflow2 like @p
cte1 = TokenSearchParam  <-cte0  ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte2 = <-cte1  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = TokenStringCompositeSearchParam  Code1 = @p ResourceTypeId = <n> SearchParamId = <n> col:TextOverflow2 like @p  [distinct]
cte1 = TokenSearchParam  ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### CompilerDoesLess: `/Observation?code-value-string=162806009$Lorem%20ipsum%20dolor%20sit%20amet%20consectetur%20adipiscing%20elit.%20Ut%20eget%20ultricies%20justo.%20Maecenas%20bibendum%20convallis%20sodales.%20Vestibulum%20quis%20molestie%20dui.%20Nulla%20porta%20elementum%20tristique.%20Aenean%20neque%20libero%20convallis%20sit%20amet%20dui%20ullamcorper%20congue%20lacinia%20erat.%20Sed%20finibus%20ex%20ac%20massa%20tincidunt%20tristique.%20In%20sed%20auctor%20massa.%20Proin%20cursus%20porttitor%20arcu.%20Maecenas%20a%20leo%20nunc.%20Sed%20pretium%20porta%20volutpat.%20In%20aliquet%20tempor%20sapien%20vitae%20laoreet%20nisl%20tempor%20ac.%20Vestibulum%20lacus%20leo%20luctus%20vitae%20pharetra%20at%20tempus%20ac%20diam.%20Integer%20at%20dui%20eu%20dolor%20gravida%20vehicula.%20Phasellus%20malesuada%20elit%20orci%20quis%20maximus%20purus%20consectetur%20ac.%20In%20semper%20consequat%20augue%20sit%20amet%20ultriciesNot&identifier=http://ignixa.io/testscript/suite/composite%7C`

Only the shipping engine does:
- `filter col:Text2 like <v>`
- `filter col:TextOverflow2 is-not-null`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte2  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = TokenStringCompositeSearchParam  Code1 = @p ResourceTypeId = <n> SearchParamId = <n> col:Text2 like @p col:TextOverflow2 is-not-null col:TextOverflow2 like @p
cte1 = TokenSearchParam  <-cte0  ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte2 = <-cte1  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = TokenStringCompositeSearchParam  Code1 = @p ResourceTypeId = <n> SearchParamId = <n> col:TextOverflow2 like @p  [distinct]
cte1 = TokenSearchParam  ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### CompilerDoesLess: `/Observation?value-quantity=ge5&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-qty-suite&_count=100`

Only the shipping engine does:
- `table QuantitySearchParam`
- `filter ResourceTypeId = <v>`
- `filter SearchParamId = <v>`
- `filter SingleValue >= <v>`
- `filter col:SingleValue is-not-null`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`
- `legacy: op union-all`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte3  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = QuantitySearchParam  ResourceTypeId = <n> SearchParamId = <n> SingleValue >= @p col:SingleValue is-not-null
cte1 = QuantitySearchParam  <-cte0  HighValue >= @p ResourceTypeId = <n> SearchParamId = <n>  [union-all]
cte2 = TokenSearchParam  <-cte1  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte3 = <-cte2  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = QuantitySearchParam  HighValue >= @p ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### CompilerDoesLess: `/Observation?value-quantity=gt4.9&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-impsrch-suite&_count=100`

Only the shipping engine does:
- `table QuantitySearchParam`
- `filter ResourceTypeId = <v>`
- `filter SearchParamId = <v>`
- `filter SingleValue > <v>`
- `filter col:SingleValue is-not-null`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`
- `legacy: op union-all`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte3  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = QuantitySearchParam  ResourceTypeId = <n> SearchParamId = <n> SingleValue > @p col:SingleValue is-not-null
cte1 = QuantitySearchParam  <-cte0  HighValue > @p ResourceTypeId = <n> SearchParamId = <n>  [union-all]
cte2 = TokenSearchParam  <-cte1  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte3 = <-cte2  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = QuantitySearchParam  HighValue > @p ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### CompilerDoesLess: `/Observation?value-quantity=gt5&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-qty-suite&_count=100`

Only the shipping engine does:
- `table QuantitySearchParam`
- `filter ResourceTypeId = <v>`
- `filter SearchParamId = <v>`
- `filter SingleValue > <v>`
- `filter col:SingleValue is-not-null`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`
- `legacy: op union-all`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte3  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = QuantitySearchParam  ResourceTypeId = <n> SearchParamId = <n> SingleValue > @p col:SingleValue is-not-null
cte1 = QuantitySearchParam  <-cte0  HighValue > @p ResourceTypeId = <n> SearchParamId = <n>  [union-all]
cte2 = TokenSearchParam  <-cte1  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte3 = <-cte2  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = QuantitySearchParam  HighValue > @p ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### CompilerDoesLess: `/Observation?value-quantity=le5&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-qty-suite&_count=100`

Only the shipping engine does:
- `table QuantitySearchParam`
- `filter ResourceTypeId = <v>`
- `filter SearchParamId = <v>`
- `filter SingleValue <= <v>`
- `filter col:SingleValue is-not-null`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`
- `legacy: op union-all`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte3  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = QuantitySearchParam  ResourceTypeId = <n> SearchParamId = <n> SingleValue <= @p col:SingleValue is-not-null
cte1 = QuantitySearchParam  <-cte0  LowValue <= @p ResourceTypeId = <n> SearchParamId = <n>  [union-all]
cte2 = TokenSearchParam  <-cte1  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte3 = <-cte2  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = QuantitySearchParam  LowValue <= @p ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### CompilerDoesLess: `/Observation?value-quantity=lt5&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-qty-suite&_count=100`

Only the shipping engine does:
- `table QuantitySearchParam`
- `filter ResourceTypeId = <v>`
- `filter SearchParamId = <v>`
- `filter SingleValue < <v>`
- `filter col:SingleValue is-not-null`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`
- `legacy: op union-all`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte3  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = QuantitySearchParam  ResourceTypeId = <n> SearchParamId = <n> SingleValue < @p col:SingleValue is-not-null
cte1 = QuantitySearchParam  <-cte0  LowValue < @p ResourceTypeId = <n> SearchParamId = <n>  [union-all]
cte2 = TokenSearchParam  <-cte1  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte3 = <-cte2  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = QuantitySearchParam  LowValue < @p ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### CompilerDoesLess: `/Observation?value-quantity=ne5&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-qty-suite&_count=100`

Only the shipping engine does:
- `table QuantitySearchParam`
- `filter ResourceTypeId = <v>`
- `filter SearchParamId = <v>`
- `filter SingleValue < <v>`
- `filter SingleValue > <v>`
- `filter col:SingleValue is-not-null (x2)`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op or`
- `legacy: op order-by`
- `legacy: op top`
- `legacy: op union-all`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte3  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = QuantitySearchParam  ResourceTypeId = <n> SearchParamId = <n> SingleValue < @p SingleValue > @p col:SingleValue is-not-null col:SingleValue is-not-null  [or]
cte1 = QuantitySearchParam  <-cte0  HighValue > @p LowValue < @p ResourceTypeId = <n> SearchParamId = <n>  [or,union-all]
cte2 = TokenSearchParam  <-cte1  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte3 = <-cte2  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = QuantitySearchParam  HighValue > @p LowValue < @p ResourceTypeId = <n> SearchParamId = <n>  [distinct,or]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### CompilerDoesLess: `/Patient/ignixa-cmp-pat1/Observation?_include=Observation:performer:Practitioner&performer=Practitioner/ignixa-cmp-prac1`

Only the shipping engine does:
- `table ReferenceSearchParam (x3)`
- `table Resource`
- `filter IsHistory = <v>`
- `filter ReferenceResourceId = <v> (x2)`
- `filter ReferenceResourceTypeId = <v> (x13)`
- `filter ResourceTypeId = <v> (x3)`
- `filter Row < <v>`
- `filter SearchParamId = <v> (x4)`
- `filter col:ReferenceResourceTypeId is-null (x2)`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x12)`
- `legacy: op count-big`
- `legacy: op distinct (x4)`
- `legacy: op exists (x2)`
- `legacy: op in`
- `legacy: op inner-join (x4)`
- `legacy: op not`
- `legacy: op or (x11)`
- `legacy: op order-by`
- `legacy: op row-number`
- `legacy: op top (x3)`
- `legacy: op union-all`

<details><summary>shapes</summary>

```
legacy:
cte0 = ReferenceSearchParam  ReferenceResourceId = @p ReferenceResourceTypeId = <n> ReferenceResourceTypeId = <n> ReferenceResourceTypeId = <n> ReferenceResourceTypeId = <n> ReferenceResourceTypeId = <n> ReferenceResourceTypeId = <n> ReferenceResourceTypeId = <n> ReferenceResourceTypeId = <n> ReferenceResourceTypeId = <n> ReferenceResourceTypeId = <n> ReferenceResourceTypeId = <n> ResourceTypeId = <n> ResourceTypeId = <n> SearchParamId = <n> SearchParamId = <n> col:ReferenceResourceTypeId is-null col:ReferenceResourceTypeId is-null  [or,or,or,or,or,or,or,or,or,or,or]
cte1 = <-cte0
cte2 = Resource  <-cte1  IsHistory = <n> ResourceTypeId = <n>  [correlate,correlate,inner-join]
cte3 = ReferenceSearchParam  <-cte2  ReferenceResourceId = @p ReferenceResourceTypeId = <n> ResourceTypeId = <n> SearchParamId = <n>  [correlate,correlate,inner-join]
cte4 = <-cte3  [distinct,order-by,row-number,top]
select0 = Resource  <-cte7  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte4 = 
cte5 = ReferenceSearchParam+Resource  <-cte4  IsDeleted = <n> IsHistory = <n> ReferenceResourceTypeId = <n> SearchParamId = <n> sub:Row < @p  [correlate,correlate,correlate,correlate,distinct,exists,in,inner-join,top]
cte6 = <-cte5  [count-big,distinct,top]
cte7 = <-cte4,cte6  [correlate,correlate,exists,not,union-all]

compiler:
select0 = <-cte0  [order-by]
cte0 = Resource  IsDeleted = <n> IsHistory = <n> ResourceTypeId = @p
```

</details>

### CompilerDoesLess: `/Patient/ignixa-evx-pat/$everything?_count=100&foo=bar`

Only the shipping engine does:
- `table ReferenceSearchParam (x2)`
- `table Resource (x2)`
- `filter IsDeleted = <v> (x2)`
- `filter IsHistory = <v> (x2)`
- `filter ResourceId = <v>`
- `filter Row < <v> (x2)`
- `filter SearchParamId = <v> (x2)`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x14)`
- `legacy: op count-big (x2)`
- `legacy: op distinct (x6)`
- `legacy: op exists (x4)`
- `legacy: op in (x2)`
- `legacy: op inner-join (x3)`
- `legacy: op not (x2)`
- `legacy: op order-by`
- `legacy: op row-number`
- `legacy: op top (x5)`
- `legacy: op union-all (x2)`

<details><summary>shapes</summary>

```
legacy:
cte0 = Resource  IsDeleted = <n> IsHistory = <n> ResourceId = @p ResourceTypeId = <n>
cte1 = <-cte0  [distinct,order-by,row-number,top]
select0 = Resource  <-cte6  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte1 = 
cte2 = ReferenceSearchParam+Resource  <-cte1  IsDeleted = <n> IsHistory = <n> SearchParamId = <n> sub:Row < @p  [correlate,correlate,correlate,correlate,distinct,exists,in,inner-join,top]
cte3 = <-cte2  [count-big,distinct,top]
cte4 = ReferenceSearchParam+Resource  <-cte1  IsDeleted = <n> IsHistory = <n> SearchParamId = <n> sub:Row < @p  [correlate,correlate,correlate,correlate,distinct,exists,in,inner-join,top]
cte5 = <-cte4  [count-big,distinct,top]
cte6 = <-cte1,cte3,cte5  [correlate,correlate,correlate,correlate,exists,exists,not,not,union-all,union-all]

compiler:
select0 = <-cte0  [order-by]
cte0 = Resource  IsDeleted = <n> IsHistory = <n> ResourceTypeId = @p
```

</details>

### CompilerDoesLess: `/Patient/ignixa-evx-pat/$everything?_since=3000`

Only the shipping engine does:
- `table ReferenceSearchParam (x2)`
- `table Resource (x2)`
- `filter IsDeleted = <v> (x2)`
- `filter IsHistory = <v> (x2)`
- `filter ResourceId = <v>`
- `filter Row < <v> (x2)`
- `filter SearchParamId = <v> (x2)`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x14)`
- `legacy: op count-big (x2)`
- `legacy: op distinct (x6)`
- `legacy: op exists (x4)`
- `legacy: op in (x2)`
- `legacy: op inner-join (x3)`
- `legacy: op not (x2)`
- `legacy: op order-by`
- `legacy: op row-number`
- `legacy: op top (x5)`
- `legacy: op union-all (x2)`

<details><summary>shapes</summary>

```
legacy:
cte0 = Resource  IsDeleted = <n> IsHistory = <n> ResourceId = @p ResourceTypeId = <n>
cte1 = <-cte0  [distinct,order-by,row-number,top]
select0 = Resource  <-cte6  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte1 = 
cte2 = ReferenceSearchParam+Resource  <-cte1  IsDeleted = <n> IsHistory = <n> SearchParamId = <n> sub:Row < @p  [correlate,correlate,correlate,correlate,distinct,exists,in,inner-join,top]
cte3 = <-cte2  [count-big,distinct,top]
cte4 = ReferenceSearchParam+Resource  <-cte1  IsDeleted = <n> IsHistory = <n> SearchParamId = <n> sub:Row < @p  [correlate,correlate,correlate,correlate,distinct,exists,in,inner-join,top]
cte5 = <-cte4  [count-big,distinct,top]
cte6 = <-cte1,cte3,cte5  [correlate,correlate,correlate,correlate,exists,exists,not,not,union-all,union-all]

compiler:
select0 = <-cte0  [order-by]
cte0 = Resource  IsDeleted = <n> IsHistory = <n> ResourceTypeId = @p
```

</details>

### CompilerDoesLess: `/Patient/ignixa-evx-pat/$everything?_type=foo`

Only the shipping engine does:
- `table ReferenceSearchParam (x2)`
- `table Resource (x2)`
- `filter IsDeleted = <v> (x2)`
- `filter IsHistory = <v> (x2)`
- `filter ResourceId = <v>`
- `filter Row < <v> (x2)`
- `filter SearchParamId = <v> (x2)`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x14)`
- `legacy: op count-big (x2)`
- `legacy: op distinct (x6)`
- `legacy: op exists (x4)`
- `legacy: op in (x2)`
- `legacy: op inner-join (x3)`
- `legacy: op not (x2)`
- `legacy: op order-by`
- `legacy: op row-number`
- `legacy: op top (x5)`
- `legacy: op union-all (x2)`

<details><summary>shapes</summary>

```
legacy:
cte0 = Resource  IsDeleted = <n> IsHistory = <n> ResourceId = @p ResourceTypeId = <n>
cte1 = <-cte0  [distinct,order-by,row-number,top]
select0 = Resource  <-cte6  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte1 = 
cte2 = ReferenceSearchParam+Resource  <-cte1  IsDeleted = <n> IsHistory = <n> SearchParamId = <n> sub:Row < @p  [correlate,correlate,correlate,correlate,distinct,exists,in,inner-join,top]
cte3 = <-cte2  [count-big,distinct,top]
cte4 = ReferenceSearchParam+Resource  <-cte1  IsDeleted = <n> IsHistory = <n> SearchParamId = <n> sub:Row < @p  [correlate,correlate,correlate,correlate,distinct,exists,in,inner-join,top]
cte5 = <-cte4  [count-big,distinct,top]
cte6 = <-cte1,cte3,cte5  [correlate,correlate,correlate,correlate,exists,exists,not,not,union-all,union-all]

compiler:
select0 = <-cte0  [order-by]
cte0 = Resource  IsDeleted = <n> IsHistory = <n> ResourceTypeId = @p
```

</details>

### CompilerDoesLess: `/Patient?_expiryDate=gt2025&identifier=http://ignixa.io/testscript/suite/ms-param%7C&_count=100`

Only the shipping engine does:
- `table DateTimeSearchParam`
- `filter EndDateTime > <v>`
- `filter ResourceTypeId = <v>`
- `filter SearchParamId = <v>`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x4)`
- `legacy: op distinct`
- `legacy: op exists`
- `legacy: op inner-join`
- `legacy: op order-by`
- `legacy: op top`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte2  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = DateTimeSearchParam  EndDateTime > @p ResourceTypeId = <n> SearchParamId = <n>
cte1 = TokenSearchParam  <-cte0  ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte2 = <-cte1  [distinct,order-by,top]

compiler:
select0 = <-cte0  [order-by]
cte0 = TokenSearchParam  ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
```

</details>

### CompilerDoesLess: `/Patient?_expiryDate=lt2025&identifier=http://ignixa.io/testscript/suite/ms-param%7C&_count=100`

Only the shipping engine does:
- `table DateTimeSearchParam`
- `filter ResourceTypeId = <v>`
- `filter SearchParamId = <v>`
- `filter StartDateTime < <v>`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x4)`
- `legacy: op distinct`
- `legacy: op exists`
- `legacy: op inner-join`
- `legacy: op order-by`
- `legacy: op top`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte2  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = DateTimeSearchParam  ResourceTypeId = <n> SearchParamId = <n> StartDateTime < @p
cte1 = TokenSearchParam  <-cte0  ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte2 = <-cte1  [distinct,order-by,top]

compiler:
select0 = <-cte0  [order-by]
cte0 = TokenSearchParam  ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
```

</details>

### CompilerDoesLess: `/Patient?_id=e04c1b8a-fe84-4111-8b44-34135571d0ec,512ffdc7-8292-4acb-a9e4-94f664dc30b4,cad0864f-c442-49e1-a0a0-a7554e334d02&_sort=-birthdate`

Only the shipping engine does:
- `filter ResourceTypeId = <v>`

Operator differences (encoding, not semantics):
- `legacy: op distinct (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`
- `compiler: op inner-join`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte2  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = Resource  IsDeleted = <n> IsHistory = <n> ResourceId = @p ResourceId = @p ResourceId = @p ResourceTypeId = <n>  [or,or]
cte1 = DateTimeSearchParam  <-cte0  IsMax = <n> ResourceTypeId = <n> SearchParamId = <n>  [correlate,correlate,exists]
cte2 = <-cte1  [distinct,order-by,top]

compiler:
select0 = DateTimeSearchParam+Resource  <-cte0  IsMax = <n> ResourceId = @p ResourceId = @p ResourceId = @p SearchParamId = <n>  [correlate,correlate,correlate,correlate,inner-join,inner-join,or,or,order-by]
cte0 = Resource  IsDeleted = <n> IsHistory = <n> ResourceTypeId = @p
```

</details>

### CompilerDoesLess: `/Patient?_id=ignixa-inco-p1`

Only the shipping engine does:
- `table ReferenceSearchParam (x2)`
- `table Resource (x2)`
- `filter IsDeleted = <v> (x2)`
- `filter IsHistory = <v> (x2)`
- `filter Row < <v>`
- `filter SearchParamId = <v> (x2)`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x12)`
- `legacy: op count-big (x2)`
- `legacy: op distinct (x6)`
- `legacy: op exists (x4)`
- `legacy: op in (x2)`
- `legacy: op inner-join (x2)`
- `legacy: op not (x2)`
- `legacy: op order-by`
- `legacy: op row-number`
- `legacy: op top (x5)`
- `legacy: op union-all (x2)`

<details><summary>shapes</summary>

```
legacy:
cte0 = Resource  IsDeleted = <n> IsHistory = <n> ResourceId = @p ResourceTypeId = <n>
cte1 = <-cte0  [distinct,order-by,row-number,top]
select0 = Resource  <-cte6  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte1 = 
cte2 = ReferenceSearchParam+Resource  <-cte1  IsDeleted = <n> IsHistory = <n> SearchParamId = <n> sub:Row < @p  [correlate,correlate,correlate,correlate,distinct,exists,in,inner-join,top]
cte3 = <-cte2  [count-big,distinct,top]
cte4 = ReferenceSearchParam+Resource  <-cte3  IsDeleted = <n> IsHistory = <n> SearchParamId = <n>  [correlate,correlate,correlate,correlate,distinct,exists,in,inner-join,top]
cte5 = <-cte4  [count-big,distinct,top]
cte6 = <-cte1,cte3,cte5  [correlate,correlate,correlate,correlate,exists,exists,not,not,union-all,union-all]

compiler:
select0 = Resource  <-cte0  ResourceId = @p  [correlate,correlate,inner-join,order-by]
cte0 = Resource  IsDeleted = <n> IsHistory = <n> ResourceTypeId = @p
```

</details>

### CompilerDoesLess: `/Patient?_not-referenced=*:*&identifier=http://ignixa.io/testscript/suite/ms-not-referenced%7C&_count=100`

Only the shipping engine does:
- `filter IsHistory = <v>`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op distinct`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte2  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = TokenSearchParam  ResourceTypeId = <n> SearchParamId = <n> SystemId = @p
cte1 = Resource+sub:ReferenceSearchParam  <-cte0  IsDeleted = <n> IsHistory = <n> IsHistory = <n> ResourceTypeId = <n>  [correlate,correlate,correlate,correlate,exists,exists,not]
cte2 = <-cte1  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = Resource+sub:ReferenceSearchParam  IsDeleted = <n> IsHistory = <n> ResourceTypeId = @p  [correlate,correlate,exists,not]
cte1 = TokenSearchParam  ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### CompilerDoesLess: `/Patient?general-practitioner=ignixa-impsrch-ref-pract-untyped&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-impsrch-suite&_count=100`

Only the shipping engine does:
- `filter ReferenceResourceTypeId = <v> (x3)`
- `filter col:ReferenceResourceTypeId is-null`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op or (x3)`
- `legacy: op order-by`
- `legacy: op top`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte2  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = ReferenceSearchParam  ReferenceResourceId = @p ReferenceResourceTypeId = <n> ReferenceResourceTypeId = <n> ReferenceResourceTypeId = <n> ResourceTypeId = <n> SearchParamId = <n> col:ReferenceResourceTypeId is-null  [or,or,or]
cte1 = TokenSearchParam  <-cte0  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte2 = <-cte1  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = ReferenceSearchParam  ReferenceResourceId = @p ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### CompilerDoesLess: `/Patient?general-practitioner=ignixa-ref-p2&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-ref-suite&_count=100`

Only the shipping engine does:
- `filter ReferenceResourceTypeId = <v> (x3)`
- `filter col:ReferenceResourceTypeId is-null`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op or (x3)`
- `legacy: op order-by`
- `legacy: op top`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte2  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = ReferenceSearchParam  ReferenceResourceId = @p ReferenceResourceTypeId = <n> ReferenceResourceTypeId = <n> ReferenceResourceTypeId = <n> ResourceTypeId = <n> SearchParamId = <n> col:ReferenceResourceTypeId is-null  [or,or,or]
cte1 = TokenSearchParam  <-cte0  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte2 = <-cte1  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = ReferenceSearchParam  ReferenceResourceId = @p ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### CompilerDoesLess: `/Patient?organization=ignixa-impsrch-ref-org-untyped&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-impsrch-suite&_count=100`

Only the shipping engine does:
- `filter ReferenceResourceTypeId = <v>`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte2  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = ReferenceSearchParam  ReferenceResourceId = @p ReferenceResourceTypeId = <n> ResourceTypeId = <n> SearchParamId = <n>
cte1 = TokenSearchParam  <-cte0  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte2 = <-cte1  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = ReferenceSearchParam  ReferenceResourceId = @p ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### CompilerDoesLess: `/Patient?organization=ignixa-ref-ijk&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-ref-suite&_count=100`

Only the shipping engine does:
- `filter ReferenceResourceTypeId = <v>`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte2  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = ReferenceSearchParam  ReferenceResourceId = @p ReferenceResourceTypeId = <n> ResourceTypeId = <n> SearchParamId = <n>
cte1 = TokenSearchParam  <-cte0  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte2 = <-cte1  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = ReferenceSearchParam  ReferenceResourceId = @p ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### CompilerDoesLess: `/Patient?organization=ignixa-ref-org-123&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-ref-suite&_count=100`

Only the shipping engine does:
- `filter ReferenceResourceTypeId = <v>`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte2  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = ReferenceSearchParam  ReferenceResourceId = @p ReferenceResourceTypeId = <n> ResourceTypeId = <n> SearchParamId = <n>
cte1 = TokenSearchParam  <-cte0  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte2 = <-cte1  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = ReferenceSearchParam  ReferenceResourceId = @p ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### CompilerDoesLess: `/Patient?organization=organization/ignixa-ref-org-123&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-ref-suite&_count=100`

Only the shipping engine does:
- `filter ReferenceResourceTypeId = <v>`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte2  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = ReferenceSearchParam  ReferenceResourceId = @p ReferenceResourceTypeId = <n> ResourceTypeId = <n> SearchParamId = <n>
cte1 = TokenSearchParam  <-cte0  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte2 = <-cte1  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = ReferenceSearchParam  ReferenceResourceId = @p ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### CompilerDoesLess: `/PractitionerRole?_id=ignixa-not-pr-active&active:not=false`

Only the shipping engine does:
- `table TokenSearchParam`
- `filter ResourceTypeId = <v>`

Operator differences (encoding, not semantics):
- `legacy: op distinct`
- `legacy: op not-in`
- `legacy: op order-by`
- `legacy: op top`
- `compiler: op not`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte3  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = Resource  IsDeleted = <n> IsHistory = <n> ResourceId = @p ResourceTypeId = <n>
cte1 = TokenSearchParam  <-cte0  ResourceTypeId = <n>  [correlate,correlate,exists]
cte2 = sub:TokenSearchParam  <-cte1  sub:Code = @p sub:ResourceTypeId = <n> sub:SearchParamId = <n>  [not-in]
cte3 = <-cte2  [distinct,order-by,top]

compiler:
select0 = Resource  <-cte2  ResourceId = @p  [correlate,correlate,inner-join,order-by]
cte0 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte1 = Resource  IsDeleted = <n> IsHistory = <n> ResourceTypeId = @p
cte2 = <-cte0,cte1  [correlate,correlate,exists,not]
```

</details>

### CompilerDoesLess: `/PractitionerRole?_id=ignixa-not-pr-active&active:not=false&location:missing=false`

Only the shipping engine does:
- `table Resource`
- `filter IsDeleted = <v>`
- `filter IsHistory = <v>`
- `filter ResourceTypeId = <v>`

Operator differences (encoding, not semantics):
- `legacy: op not-in`
- `legacy: op order-by`
- `legacy: op top`
- `compiler: op not`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte3  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = Resource  IsDeleted = <n> IsHistory = <n> ResourceId = @p ResourceTypeId = <n>
cte1 = ReferenceSearchParam  <-cte0  ResourceTypeId = <n> SearchParamId = <n>  [correlate,correlate,exists]
cte2 = sub:TokenSearchParam  <-cte1  sub:Code = @p sub:ResourceTypeId = <n> sub:SearchParamId = <n>  [not-in]
cte3 = <-cte2  [distinct,order-by,top]

compiler:
select0 = Resource  <-cte2  ResourceId = @p  [correlate,correlate,inner-join,order-by]
cte0 = ReferenceSearchParam  ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,exists,not]
```

</details>

### CompilerDoesLess: `/PractitionerRole?_id=ignixa-not-pr-active&active:not=true`

Only the shipping engine does:
- `table TokenSearchParam`
- `filter ResourceTypeId = <v>`

Operator differences (encoding, not semantics):
- `legacy: op distinct`
- `legacy: op not-in`
- `legacy: op order-by`
- `legacy: op top`
- `compiler: op not`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte3  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = Resource  IsDeleted = <n> IsHistory = <n> ResourceId = @p ResourceTypeId = <n>
cte1 = TokenSearchParam  <-cte0  ResourceTypeId = <n>  [correlate,correlate,exists]
cte2 = sub:TokenSearchParam  <-cte1  sub:Code = @p sub:ResourceTypeId = <n> sub:SearchParamId = <n>  [not-in]
cte3 = <-cte2  [distinct,order-by,top]

compiler:
select0 = Resource  <-cte2  ResourceId = @p  [correlate,correlate,inner-join,order-by]
cte0 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte1 = Resource  IsDeleted = <n> IsHistory = <n> ResourceTypeId = @p
cte2 = <-cte0,cte1  [correlate,correlate,exists,not]
```

</details>

### CompilerDoesLess: `/PractitionerRole?_id=ignixa-not-pr-active,ignixa-not-pr-inactive&active:not=false`

Only the shipping engine does:
- `table TokenSearchParam`
- `filter ResourceTypeId = <v>`

Operator differences (encoding, not semantics):
- `legacy: op distinct`
- `legacy: op not-in`
- `legacy: op order-by`
- `legacy: op top`
- `compiler: op not`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte3  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = Resource  IsDeleted = <n> IsHistory = <n> ResourceId = @p ResourceId = @p ResourceTypeId = <n>  [or]
cte1 = TokenSearchParam  <-cte0  ResourceTypeId = <n>  [correlate,correlate,exists]
cte2 = sub:TokenSearchParam  <-cte1  sub:Code = @p sub:ResourceTypeId = <n> sub:SearchParamId = <n>  [not-in]
cte3 = <-cte2  [distinct,order-by,top]

compiler:
select0 = Resource  <-cte2  ResourceId = @p ResourceId = @p  [correlate,correlate,inner-join,or,order-by]
cte0 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte1 = Resource  IsDeleted = <n> IsHistory = <n> ResourceTypeId = @p
cte2 = <-cte0,cte1  [correlate,correlate,exists,not]
```

</details>

### CompilerDoesLess: `/PractitionerRole?_id=ignixa-not-pr-inactive&active:not=false`

Only the shipping engine does:
- `table TokenSearchParam`
- `filter ResourceTypeId = <v>`

Operator differences (encoding, not semantics):
- `legacy: op distinct`
- `legacy: op not-in`
- `legacy: op order-by`
- `legacy: op top`
- `compiler: op not`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte3  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = Resource  IsDeleted = <n> IsHistory = <n> ResourceId = @p ResourceTypeId = <n>
cte1 = TokenSearchParam  <-cte0  ResourceTypeId = <n>  [correlate,correlate,exists]
cte2 = sub:TokenSearchParam  <-cte1  sub:Code = @p sub:ResourceTypeId = <n> sub:SearchParamId = <n>  [not-in]
cte3 = <-cte2  [distinct,order-by,top]

compiler:
select0 = Resource  <-cte2  ResourceId = @p  [correlate,correlate,inner-join,order-by]
cte0 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte1 = Resource  IsDeleted = <n> IsHistory = <n> ResourceTypeId = @p
cte2 = <-cte0,cte1  [correlate,correlate,exists,not]
```

</details>

### CompilerDoesLess: `/PractitionerRole?_id=ignixa-not-pr-inactive&active:not=true`

Only the shipping engine does:
- `table TokenSearchParam`
- `filter ResourceTypeId = <v>`

Operator differences (encoding, not semantics):
- `legacy: op distinct`
- `legacy: op not-in`
- `legacy: op order-by`
- `legacy: op top`
- `compiler: op not`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte3  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = Resource  IsDeleted = <n> IsHistory = <n> ResourceId = @p ResourceTypeId = <n>
cte1 = TokenSearchParam  <-cte0  ResourceTypeId = <n>  [correlate,correlate,exists]
cte2 = sub:TokenSearchParam  <-cte1  sub:Code = @p sub:ResourceTypeId = <n> sub:SearchParamId = <n>  [not-in]
cte3 = <-cte2  [distinct,order-by,top]

compiler:
select0 = Resource  <-cte2  ResourceId = @p  [correlate,correlate,inner-join,order-by]
cte0 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte1 = Resource  IsDeleted = <n> IsHistory = <n> ResourceTypeId = @p
cte2 = <-cte0,cte1  [correlate,correlate,exists,not]
```

</details>

### CompilerDoesLess: `/PractitionerRole?_id=ignixa-not-pr-noloc&location:missing=false`

Only the shipping engine does:
- `table Resource`
- `filter IsDeleted = <v>`
- `filter IsHistory = <v>`
- `filter ResourceTypeId = <v>`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op distinct`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte2  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = Resource  IsDeleted = <n> IsHistory = <n> ResourceId = @p ResourceTypeId = <n>
cte1 = ReferenceSearchParam  <-cte0  ResourceTypeId = <n> SearchParamId = <n>  [correlate,correlate,exists]
cte2 = <-cte1  [distinct,order-by,top]

compiler:
select0 = Resource  <-cte0  ResourceId = @p  [correlate,correlate,inner-join,order-by]
cte0 = ReferenceSearchParam  ResourceTypeId = <n> SearchParamId = <n>  [distinct]
```

</details>

### CompilerDoesLess: `/PractitionerRole?_id=ignixa-not-pr-noloc&location:missing=true`

Only the shipping engine does:
- `table Resource`
- `filter IsDeleted = <v>`
- `filter IsHistory = <v>`
- `filter ResourceTypeId = <v>`

Operator differences (encoding, not semantics):
- `legacy: op distinct`
- `legacy: op not-in`
- `legacy: op order-by`
- `legacy: op top`
- `compiler: op correlate (x2)`
- `compiler: op exists`
- `compiler: op not`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte3  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = Resource  IsDeleted = <n> IsHistory = <n> ResourceId = @p ResourceTypeId = <n>
cte1 = Resource  IsDeleted = <n> IsHistory = <n> ResourceTypeId = <n>
cte2 = sub:ReferenceSearchParam  <-cte1  sub:ResourceTypeId = <n> sub:SearchParamId = <n>  [not-in]
cte3 = <-cte2  [distinct,order-by,top]

compiler:
select0 = Resource  <-cte2  ResourceId = @p  [correlate,correlate,inner-join,order-by]
cte0 = ReferenceSearchParam  ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte1 = Resource  IsDeleted = <n> IsHistory = <n> ResourceTypeId = @p
cte2 = <-cte0,cte1  [correlate,correlate,exists,not]
```

</details>

### CompilerDoesLess: `/RiskAssessment?probability=ge5&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-num-suite&_count=100`

Only the shipping engine does:
- `table NumberSearchParam`
- `filter ResourceTypeId = <v>`
- `filter SearchParamId = <v>`
- `filter SingleValue >= <v>`
- `filter col:SingleValue is-not-null`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`
- `legacy: op union-all`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte3  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = NumberSearchParam  ResourceTypeId = <n> SearchParamId = <n> SingleValue >= @p col:SingleValue is-not-null
cte1 = NumberSearchParam  <-cte0  HighValue >= @p ResourceTypeId = <n> SearchParamId = <n>  [union-all]
cte2 = TokenSearchParam  <-cte1  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte3 = <-cte2  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = NumberSearchParam  HighValue >= @p ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### CompilerDoesLess: `/RiskAssessment?probability=gt5&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-impsrch-suite&_count=100`

Only the shipping engine does:
- `table NumberSearchParam`
- `filter ResourceTypeId = <v>`
- `filter SearchParamId = <v>`
- `filter SingleValue > <v>`
- `filter col:SingleValue is-not-null`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`
- `legacy: op union-all`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte3  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = NumberSearchParam  ResourceTypeId = <n> SearchParamId = <n> SingleValue > @p col:SingleValue is-not-null
cte1 = NumberSearchParam  <-cte0  HighValue > @p ResourceTypeId = <n> SearchParamId = <n>  [union-all]
cte2 = TokenSearchParam  <-cte1  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte3 = <-cte2  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = NumberSearchParam  HighValue > @p ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### CompilerDoesLess: `/RiskAssessment?probability=gt5&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-num-suite&_count=100`

Only the shipping engine does:
- `table NumberSearchParam`
- `filter ResourceTypeId = <v>`
- `filter SearchParamId = <v>`
- `filter SingleValue > <v>`
- `filter col:SingleValue is-not-null`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`
- `legacy: op union-all`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte3  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = NumberSearchParam  ResourceTypeId = <n> SearchParamId = <n> SingleValue > @p col:SingleValue is-not-null
cte1 = NumberSearchParam  <-cte0  HighValue > @p ResourceTypeId = <n> SearchParamId = <n>  [union-all]
cte2 = TokenSearchParam  <-cte1  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte3 = <-cte2  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = NumberSearchParam  HighValue > @p ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### CompilerDoesLess: `/RiskAssessment?probability=le5&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-impsrch-suite&_count=100`

Only the shipping engine does:
- `table NumberSearchParam`
- `filter ResourceTypeId = <v>`
- `filter SearchParamId = <v>`
- `filter SingleValue <= <v>`
- `filter col:SingleValue is-not-null`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`
- `legacy: op union-all`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte3  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = NumberSearchParam  ResourceTypeId = <n> SearchParamId = <n> SingleValue <= @p col:SingleValue is-not-null
cte1 = NumberSearchParam  <-cte0  LowValue <= @p ResourceTypeId = <n> SearchParamId = <n>  [union-all]
cte2 = TokenSearchParam  <-cte1  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte3 = <-cte2  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = NumberSearchParam  LowValue <= @p ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### CompilerDoesLess: `/RiskAssessment?probability=le5&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-num-suite&_count=100`

Only the shipping engine does:
- `table NumberSearchParam`
- `filter ResourceTypeId = <v>`
- `filter SearchParamId = <v>`
- `filter SingleValue <= <v>`
- `filter col:SingleValue is-not-null`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`
- `legacy: op union-all`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte3  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = NumberSearchParam  ResourceTypeId = <n> SearchParamId = <n> SingleValue <= @p col:SingleValue is-not-null
cte1 = NumberSearchParam  <-cte0  LowValue <= @p ResourceTypeId = <n> SearchParamId = <n>  [union-all]
cte2 = TokenSearchParam  <-cte1  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte3 = <-cte2  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = NumberSearchParam  LowValue <= @p ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### CompilerDoesLess: `/RiskAssessment?probability=lt5&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-num-suite&_count=100`

Only the shipping engine does:
- `table NumberSearchParam`
- `filter ResourceTypeId = <v>`
- `filter SearchParamId = <v>`
- `filter SingleValue < <v>`
- `filter col:SingleValue is-not-null`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`
- `legacy: op union-all`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte3  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = NumberSearchParam  ResourceTypeId = <n> SearchParamId = <n> SingleValue < @p col:SingleValue is-not-null
cte1 = NumberSearchParam  <-cte0  LowValue < @p ResourceTypeId = <n> SearchParamId = <n>  [union-all]
cte2 = TokenSearchParam  <-cte1  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte3 = <-cte2  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = NumberSearchParam  LowValue < @p ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### CompilerDoesLess: `/RiskAssessment?probability=ne5&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-impsrch-suite&_count=100`

Only the shipping engine does:
- `table NumberSearchParam`
- `filter ResourceTypeId = <v>`
- `filter SearchParamId = <v>`
- `filter SingleValue < <v>`
- `filter SingleValue > <v>`
- `filter col:SingleValue is-not-null (x2)`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op or`
- `legacy: op order-by`
- `legacy: op top`
- `legacy: op union-all`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte3  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = NumberSearchParam  ResourceTypeId = <n> SearchParamId = <n> SingleValue < @p SingleValue > @p col:SingleValue is-not-null col:SingleValue is-not-null  [or]
cte1 = NumberSearchParam  <-cte0  HighValue > @p LowValue < @p ResourceTypeId = <n> SearchParamId = <n>  [or,union-all]
cte2 = TokenSearchParam  <-cte1  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte3 = <-cte2  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = NumberSearchParam  HighValue > @p LowValue < @p ResourceTypeId = <n> SearchParamId = <n>  [distinct,or]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### CompilerDoesLess: `/RiskAssessment?probability=ne5&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-num-suite&_count=100`

Only the shipping engine does:
- `table NumberSearchParam`
- `filter ResourceTypeId = <v>`
- `filter SearchParamId = <v>`
- `filter SingleValue < <v>`
- `filter SingleValue > <v>`
- `filter col:SingleValue is-not-null (x2)`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op or`
- `legacy: op order-by`
- `legacy: op top`
- `legacy: op union-all`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte3  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = NumberSearchParam  ResourceTypeId = <n> SearchParamId = <n> SingleValue < @p SingleValue > @p col:SingleValue is-not-null col:SingleValue is-not-null  [or]
cte1 = NumberSearchParam  <-cte0  HighValue > @p LowValue < @p ResourceTypeId = <n> SearchParamId = <n>  [or,union-all]
cte2 = TokenSearchParam  <-cte1  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte3 = <-cte2  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = NumberSearchParam  HighValue > @p LowValue < @p ResourceTypeId = <n> SearchParamId = <n>  [distinct,or]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### CompilerDoesMore: `/AllergyIntolerance?category=medication,biologic&identifier=http://fhir262/test%7C&_count=100`

Only the compiler does:
- `table TokenSearchParam`
- `filter ResourceTypeId = <v>`
- `filter SearchParamId = <v>`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op or`
- `legacy: op order-by`
- `legacy: op top`
- `compiler: op distinct`
- `compiler: op union`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte2  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = TokenSearchParam  Code = @p Code = @p ResourceTypeId = <n> SearchParamId = <n>  [or]
cte1 = TokenSearchParam  <-cte0  ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte2 = <-cte1  [distinct,order-by,top]

compiler:
select0 = <-cte4  [order-by]
cte0 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte2 = <-cte0,cte1  [union]
cte3 = TokenSearchParam  ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte4 = <-cte2,cte3  [correlate,correlate,inner-join]
```

</details>

### CompilerDoesMore: `/HealthcareService?active:not=false&name:missing=false&_has:PractitionerRole:service:active=true&identifier=http://ignixa.io/testscript/suite/escape%7C&_count=100`

Only the compiler does:
- `filter ReferenceResourceTypeId = <v>`
- `filter ResourceTypeId = <v>`
- `filter col:BaseUri is-null`

Operator differences (encoding, not semantics):
- `legacy: op in (x2)`
- `legacy: op inner-join`
- `legacy: op not-in`
- `legacy: op order-by`
- `legacy: op top`
- `compiler: op distinct (x3)`
- `compiler: op exists`
- `compiler: op not`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte5  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = ReferenceSearchParam+Resource  IsDeleted = <n> IsHistory = <n> ResourceTypeId = <n> SearchParamId = <n>  [correlate,correlate,in,in,inner-join]
cte1 = TokenSearchParam  <-cte0  Code = @p SearchParamId = <n>  [correlate,correlate,inner-join]
cte2 = TokenSearchParam  <-cte1  ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,inner-join]
cte3 = StringSearchParam  <-cte2  ResourceTypeId = <n> SearchParamId = <n>  [correlate,correlate,inner-join]
cte4 = sub:TokenSearchParam  <-cte3  sub:Code = @p sub:ResourceTypeId = <n> sub:SearchParamId = <n>  [not-in]
cte5 = <-cte4  [distinct,order-by,top]

compiler:
select0 = <-cte7  [order-by]
cte0 = StringSearchParam  ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte2 = ReferenceSearchParam+Resource  <-cte1  IsDeleted = <n> IsHistory = <n> ReferenceResourceTypeId = <n> ResourceTypeId = <n> SearchParamId = <n> col:BaseUri is-null  [correlate,correlate,correlate,correlate,distinct,inner-join,inner-join]
cte3 = TokenSearchParam  ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte4 = <-cte0,cte2  [correlate,correlate,inner-join]
cte5 = <-cte3,cte4  [correlate,correlate,inner-join]
cte6 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte7 = <-cte5,cte6  [correlate,correlate,exists,not]
```

</details>

### CompilerDoesMore: `/Observation?code-value-string=162806009$Lorem,162806009$blue&identifier=http://ignixa.io/testscript/suite/composite%7C`

Only the compiler does:
- `table TokenStringCompositeSearchParam`
- `filter ResourceTypeId = <v>`
- `filter SearchParamId = <v>`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op or`
- `legacy: op order-by`
- `legacy: op top`
- `compiler: op distinct`
- `compiler: op union`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte2  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = TokenStringCompositeSearchParam  Code1 = @p Code1 = @p ResourceTypeId = <n> SearchParamId = <n> col:Text2 like @p col:Text2 like @p  [or]
cte1 = TokenSearchParam  <-cte0  ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte2 = <-cte1  [distinct,order-by,top]

compiler:
select0 = <-cte4  [order-by]
cte0 = TokenStringCompositeSearchParam  Code1 = @p ResourceTypeId = <n> SearchParamId = <n> col:Text2 like @p  [distinct]
cte1 = TokenStringCompositeSearchParam  Code1 = @p ResourceTypeId = <n> SearchParamId = <n> col:Text2 like @p  [distinct]
cte2 = <-cte0,cte1  [union]
cte3 = TokenSearchParam  ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte4 = <-cte2,cte3  [correlate,correlate,inner-join]
```

</details>

### CompilerDoesMore: `/Observation?code=code1,http://ignixa.io/testscript/suite/token-sys-b%7Ccode2&identifier=http://ignixa.io/testscript/suite/token%7C&_count=100`

Only the compiler does:
- `table TokenSearchParam`
- `filter ResourceTypeId = <v>`
- `filter SearchParamId = <v>`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op or`
- `legacy: op order-by`
- `legacy: op top`
- `compiler: op distinct`
- `compiler: op union`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte2  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = TokenSearchParam  Code = @p Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [or]
cte1 = TokenSearchParam  <-cte0  ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte2 = <-cte1  [distinct,order-by,top]

compiler:
select0 = <-cte4  [order-by]
cte0 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [union]
cte3 = TokenSearchParam  ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte4 = <-cte2,cte3  [correlate,correlate,inner-join]
```

</details>

### CompilerDoesMore: `/Observation?code=ignixa-date-test&date=gt1980-05-10&date=lt1980-05-12`

Only the compiler does:
- `table DateTimeSearchParam`
- `filter ResourceTypeId = <v>`
- `filter SearchParamId = <v>`

Operator differences (encoding, not semantics):
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`
- `compiler: op distinct`
- `compiler: op inner-join`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte2  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n>
cte1 = DateTimeSearchParam  <-cte0  EndDateTime > @p ResourceTypeId = <n> SearchParamId = <n> StartDateTime < @p  [correlate,correlate,exists]
cte2 = <-cte1  [distinct,order-by,top]

compiler:
select0 = <-cte4  [order-by]
cte0 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte1 = DateTimeSearchParam  EndDateTime > @p ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte2 = DateTimeSearchParam  ResourceTypeId = <n> SearchParamId = <n> StartDateTime < @p  [distinct]
cte3 = <-cte0,cte1  [correlate,correlate,inner-join]
cte4 = <-cte2,cte3  [correlate,correlate,inner-join]
```

</details>

### CompilerDoesMore: `/Observation?combo-code-value-concept=http://snomed.info/sct%7C249227004$http://loinc.org/la%7CLA6722-8,http://snomed.info/sct%7C249227004$http://loinc.org/la%7CLA6724-4&identifier=http://ignixa.io/testscript/suite/composite%7C`

Only the compiler does:
- `table TokenTokenCompositeSearchParam`
- `filter ResourceTypeId = <v>`
- `filter SearchParamId = <v>`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op or`
- `legacy: op order-by`
- `legacy: op top`
- `compiler: op distinct`
- `compiler: op union`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte2  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = TokenTokenCompositeSearchParam  Code1 = @p Code1 = @p Code2 = @p Code2 = @p ResourceTypeId = <n> SearchParamId = <n> SystemId1 = @p SystemId1 = @p SystemId2 = @p SystemId2 = @p  [or]
cte1 = TokenSearchParam  <-cte0  ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte2 = <-cte1  [distinct,order-by,top]

compiler:
select0 = <-cte4  [order-by]
cte0 = TokenTokenCompositeSearchParam  Code1 = @p Code2 = @p ResourceTypeId = <n> SearchParamId = <n> SystemId1 = @p SystemId2 = @p  [distinct]
cte1 = TokenTokenCompositeSearchParam  Code1 = @p Code2 = @p ResourceTypeId = <n> SearchParamId = <n> SystemId1 = @p SystemId2 = @p  [distinct]
cte2 = <-cte0,cte1  [union]
cte3 = TokenSearchParam  ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte4 = <-cte2,cte3  [correlate,correlate,inner-join]
```

</details>

### CompilerDoesMore: `/Observation?subject:Patient.family=Smith&identifier=http://fhir262/test%7C&_count=100`

Only the compiler does:
- `filter ReferenceResourceTypeId = <v>`
- `filter ResourceTypeId = <v>`
- `filter col:BaseUri is-null`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op in (x2)`
- `legacy: op order-by`
- `legacy: op top`
- `compiler: op distinct`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte3  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = ReferenceSearchParam+Resource  IsDeleted = <n> IsHistory = <n> ResourceTypeId = <n> SearchParamId = <n>  [correlate,correlate,in,in,inner-join]
cte1 = StringSearchParam  <-cte0  SearchParamId = <n> col:Text like @p  [correlate,correlate,inner-join]
cte2 = TokenSearchParam  <-cte1  ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte3 = <-cte2  [distinct,order-by,top]

compiler:
select0 = <-cte3  [order-by]
cte0 = StringSearchParam  ResourceTypeId = <n> SearchParamId = <n> col:Text like @p  [distinct]
cte1 = ReferenceSearchParam+Resource  <-cte0  IsDeleted = <n> IsHistory = <n> ReferenceResourceTypeId = <n> ResourceTypeId = <n> SearchParamId = <n> col:BaseUri is-null  [correlate,correlate,correlate,correlate,distinct,inner-join,inner-join]
cte2 = TokenSearchParam  ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte3 = <-cte1,cte2  [correlate,correlate,inner-join]
```

</details>

### CompilerDoesMore: `/Organization?_id=ignixa-inc-org-a&_include:iterate=Organization:partof`

Only the compiler does:
- `table Resource`
- `filter ResourceTypeId = <v> (x2)`
- `filter col:BaseUri is-null`

Operator differences (encoding, not semantics):
- `legacy: op distinct (x3)`
- `legacy: op in`
- `legacy: op row-number`
- `legacy: op top`

<details><summary>shapes</summary>

```
legacy:
cte0 = Resource  IsDeleted = <n> IsHistory = <n> ResourceId = @p ResourceTypeId = <n>
cte1 = <-cte0  [distinct,order-by,row-number,top]
select0 = Resource  <-cte4  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte1 = 
cte2 = ReferenceSearchParam+Resource  <-cte1  IsDeleted = <n> IsHistory = <n> SearchParamId = <n>  [correlate,correlate,correlate,correlate,distinct,exists,in,inner-join,top]
cte3 = <-cte2  [count-big,distinct,top]
cte4 = <-cte1,cte3  [correlate,correlate,exists,not,union-all]

compiler:
select0 = <-cteMatchPage,inc0lim  [correlate,correlate,exists,not,union-all]
cte0 = Resource  IsDeleted = <n> IsHistory = <n> ResourceTypeId = @p
cteMatchPage = Resource  <-cte0  ResourceId = @p  [correlate,correlate,inner-join]
inc0 = ReferenceSearchParam+Resource  <-cteMatchPage  IsDeleted = <n> IsHistory = <n> ResourceTypeId = <n> ResourceTypeId = <n> SearchParamId = <n> col:BaseUri is-null  [correlate,correlate,correlate,correlate,distinct,exists,inner-join,order-by,top]
inc0lim = <-inc0  [count-big,order-by,top]
```

</details>

### CompilerDoesMore: `/Patient?family=Smith,Anderson&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-impsrch-suite&_count=100`

Only the compiler does:
- `table StringSearchParam`
- `filter ResourceTypeId = <v>`
- `filter SearchParamId = <v>`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op or`
- `legacy: op order-by`
- `legacy: op top`
- `compiler: op distinct`
- `compiler: op union`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte2  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = StringSearchParam  ResourceTypeId = <n> SearchParamId = <n> col:Text like @p col:Text like @p  [or]
cte1 = TokenSearchParam  <-cte0  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte2 = <-cte1  [distinct,order-by,top]

compiler:
select0 = <-cte4  [order-by]
cte0 = StringSearchParam  ResourceTypeId = <n> SearchParamId = <n> col:Text like @p  [distinct]
cte1 = StringSearchParam  ResourceTypeId = <n> SearchParamId = <n> col:Text like @p  [distinct]
cte2 = <-cte0,cte1  [union]
cte3 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte4 = <-cte2,cte3  [correlate,correlate,inner-join]
```

</details>

### CompilerDoesMore: `/Patient?family=Zephyr,Jones&identifier=http://ignixa.io/testscript/suite/escape%7C&_count=100`

Only the compiler does:
- `table StringSearchParam`
- `filter ResourceTypeId = <v>`
- `filter SearchParamId = <v>`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op or`
- `legacy: op order-by`
- `legacy: op top`
- `compiler: op distinct`
- `compiler: op union`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte2  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = StringSearchParam  ResourceTypeId = <n> SearchParamId = <n> col:Text like @p col:Text like @p  [or]
cte1 = TokenSearchParam  <-cte0  ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte2 = <-cte1  [distinct,order-by,top]

compiler:
select0 = <-cte4  [order-by]
cte0 = StringSearchParam  ResourceTypeId = <n> SearchParamId = <n> col:Text like @p  [distinct]
cte1 = StringSearchParam  ResourceTypeId = <n> SearchParamId = <n> col:Text like @p  [distinct]
cte2 = <-cte0,cte1  [union]
cte3 = TokenSearchParam  ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte4 = <-cte2,cte3  [correlate,correlate,inner-join]
```

</details>

### CompilerDoesMore: `/Patient?given=Alex,Bob&family=Smith&identifier=http://fhir262/test%7C&_count=100`

Only the compiler does:
- `table StringSearchParam`
- `filter ResourceTypeId = <v>`
- `filter SearchParamId = <v>`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists (x2)`
- `legacy: op or`
- `legacy: op order-by`
- `legacy: op top`
- `compiler: op distinct (x2)`
- `compiler: op inner-join`
- `compiler: op union`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte3  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = StringSearchParam  ResourceTypeId = <n> SearchParamId = <n> col:Text like @p col:Text like @p  [or]
cte1 = StringSearchParam  <-cte0  ResourceTypeId = <n> SearchParamId = <n> col:Text like @p  [correlate,correlate,exists]
cte2 = TokenSearchParam  <-cte1  ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte3 = <-cte2  [distinct,order-by,top]

compiler:
select0 = <-cte6  [order-by]
cte0 = StringSearchParam  ResourceTypeId = <n> SearchParamId = <n> col:Text like @p  [distinct]
cte1 = StringSearchParam  ResourceTypeId = <n> SearchParamId = <n> col:Text like @p  [distinct]
cte2 = <-cte0,cte1  [union]
cte3 = StringSearchParam  ResourceTypeId = <n> SearchParamId = <n> col:Text like @p  [distinct]
cte4 = TokenSearchParam  ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte5 = <-cte2,cte3  [correlate,correlate,inner-join]
cte6 = <-cte4,cte5  [correlate,correlate,inner-join]
```

</details>

### CompilerDoesMore: `/Patient?given=Alex,Carol&identifier=http://fhir262/test%7C&_count=100`

Only the compiler does:
- `table StringSearchParam`
- `filter ResourceTypeId = <v>`
- `filter SearchParamId = <v>`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op or`
- `legacy: op order-by`
- `legacy: op top`
- `compiler: op distinct`
- `compiler: op union`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte2  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = StringSearchParam  ResourceTypeId = <n> SearchParamId = <n> col:Text like @p col:Text like @p  [or]
cte1 = TokenSearchParam  <-cte0  ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte2 = <-cte1  [distinct,order-by,top]

compiler:
select0 = <-cte4  [order-by]
cte0 = StringSearchParam  ResourceTypeId = <n> SearchParamId = <n> col:Text like @p  [distinct]
cte1 = StringSearchParam  ResourceTypeId = <n> SearchParamId = <n> col:Text like @p  [distinct]
cte2 = <-cte0,cte1  [union]
cte3 = TokenSearchParam  ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte4 = <-cte2,cte3  [correlate,correlate,inner-join]
```

</details>

### CompilerDoesMore: `/Patient?name=Doe,Smith&identifier=http://fhir262/test%7C&_count=100`

Only the compiler does:
- `table StringSearchParam`
- `filter ResourceTypeId = <v>`
- `filter SearchParamId = <v>`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op or`
- `legacy: op order-by`
- `legacy: op top`
- `compiler: op distinct`
- `compiler: op union`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte2  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = StringSearchParam  ResourceTypeId = <n> SearchParamId = <n> col:Text like @p col:Text like @p  [or]
cte1 = TokenSearchParam  <-cte0  ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte2 = <-cte1  [distinct,order-by,top]

compiler:
select0 = <-cte4  [order-by]
cte0 = StringSearchParam  ResourceTypeId = <n> SearchParamId = <n> col:Text like @p  [distinct]
cte1 = StringSearchParam  ResourceTypeId = <n> SearchParamId = <n> col:Text like @p  [distinct]
cte2 = <-cte0,cte1  [union]
cte3 = TokenSearchParam  ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte4 = <-cte2,cte3  [correlate,correlate,inner-join]
```

</details>

### CompilerDoesMore: `/Patient?organization.name=ACME%20Corp`

Only the compiler does:
- `filter ReferenceResourceTypeId = <v>`
- `filter ResourceTypeId = <v>`
- `filter col:BaseUri is-null`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op in (x2)`
- `legacy: op inner-join`
- `legacy: op order-by`
- `legacy: op top`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte2  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = ReferenceSearchParam+Resource  IsDeleted = <n> IsHistory = <n> ResourceTypeId = <n> SearchParamId = <n>  [correlate,correlate,in,in,inner-join]
cte1 = StringSearchParam  <-cte0  SearchParamId = <n> col:Text like @p  [correlate,correlate,inner-join]
cte2 = <-cte1  [distinct,order-by,top]

compiler:
select0 = <-cte1  [order-by]
cte0 = StringSearchParam  ResourceTypeId = <n> SearchParamId = <n> col:Text like @p  [distinct]
cte1 = ReferenceSearchParam+Resource  <-cte0  IsDeleted = <n> IsHistory = <n> ReferenceResourceTypeId = <n> ResourceTypeId = <n> SearchParamId = <n> col:BaseUri is-null  [correlate,correlate,correlate,correlate,distinct,inner-join,inner-join]
```

</details>

### Divergent: `/DiagnosticReport?specimen:missing=true&_id=ignixa-inc-dr1&_include=DiagnosticReport:result`

Only the shipping engine does:
- `filter IsDeleted = <v>`
- `filter IsHistory = <v>`
- `filter Row < <v>`

Only the compiler does:
- `filter ResourceTypeId = <v>`
- `filter col:BaseUri is-null`

Operator differences (encoding, not semantics):
- `legacy: op distinct (x2)`
- `legacy: op in`
- `legacy: op not-in`
- `legacy: op row-number`
- `legacy: op top`
- `compiler: op correlate (x2)`
- `compiler: op exists`
- `compiler: op not`

<details><summary>shapes</summary>

```
legacy:
cte0 = Resource  IsDeleted = <n> IsHistory = <n> ResourceId = @p ResourceTypeId = <n>
cte1 = Resource  IsDeleted = <n> IsHistory = <n> ResourceTypeId = <n>
cte2 = sub:ReferenceSearchParam  <-cte1  sub:ResourceTypeId = <n> sub:SearchParamId = <n>  [not-in]
cte3 = <-cte2  [distinct,order-by,row-number,top]
select0 = Resource  <-cte6  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte3 = 
cte4 = ReferenceSearchParam+Resource  <-cte3  IsDeleted = <n> IsHistory = <n> SearchParamId = <n> sub:Row < @p  [correlate,correlate,correlate,correlate,distinct,exists,in,inner-join,top]
cte5 = <-cte4  [count-big,distinct,top]
cte6 = <-cte3,cte5  [correlate,correlate,exists,not,union-all]

compiler:
select0 = <-cteMatchPage,inc0lim  [correlate,correlate,exists,not,union-all]
cte0 = ReferenceSearchParam  ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte1 = Resource  IsDeleted = <n> IsHistory = <n> ResourceTypeId = @p
cte2 = <-cte0,cte1  [correlate,correlate,exists,not]
cteMatchPage = Resource  <-cte2  ResourceId = @p  [correlate,correlate,inner-join]
inc0 = ReferenceSearchParam+Resource  <-cteMatchPage  IsDeleted = <n> IsHistory = <n> ResourceTypeId = <n> ResourceTypeId = <n> SearchParamId = <n> col:BaseUri is-null  [correlate,correlate,correlate,correlate,distinct,exists,inner-join,order-by,top]
inc0lim = <-inc0  [count-big,order-by,top]
```

</details>

### Divergent: `/DocumentReference/$docref?patient=ignixa-docref-pat0,ignixa-docref-pat1`

Only the shipping engine does:
- `filter ReferenceResourceTypeId = <v> (x8)`
- `filter col:ReferenceResourceTypeId is-null (x2)`

Only the compiler does:
- `table ReferenceSearchParam`
- `filter ResourceTypeId = <v>`
- `filter SearchParamId = <v>`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op inner-join`
- `legacy: op or (x9)`
- `legacy: op order-by`
- `legacy: op top`
- `compiler: op union`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte1  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = ReferenceSearchParam  ReferenceResourceId = @p ReferenceResourceId = @p ReferenceResourceTypeId = <n> ReferenceResourceTypeId = <n> ReferenceResourceTypeId = <n> ReferenceResourceTypeId = <n> ReferenceResourceTypeId = <n> ReferenceResourceTypeId = <n> ReferenceResourceTypeId = <n> ReferenceResourceTypeId = <n> ResourceTypeId = <n> SearchParamId = <n> col:ReferenceResourceTypeId is-null col:ReferenceResourceTypeId is-null  [or,or,or,or,or,or,or,or,or]
cte1 = <-cte0  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = ReferenceSearchParam  ReferenceResourceId = @p ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte1 = ReferenceSearchParam  ReferenceResourceId = @p ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte2 = <-cte0,cte1  [union]
```

</details>

### Divergent: `/HealthcareService?_id=ignixa-id-a,ignixa-id-b,ignixa-id-c&_count=100`

Only the shipping engine does:
- `table TokenSearchParam`
- `filter Code = <v>`
- `filter ResourceTypeId = <v>`
- `filter SearchParamId = <v>`

Only the compiler does:
- `filter ResourceId = <v> (x2)`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op distinct (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`
- `compiler: op or (x2)`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte2  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = Resource  IsDeleted = <n> IsHistory = <n> ResourceId = @p ResourceTypeId = <n>
cte1 = TokenSearchParam  <-cte0  Code = @p ResourceTypeId = <n> SearchParamId = <n>  [correlate,correlate,exists]
cte2 = <-cte1  [distinct,order-by,top]

compiler:
select0 = Resource  <-cte0  ResourceId = @p ResourceId = @p ResourceId = @p  [correlate,correlate,inner-join,or,or,order-by]
cte0 = Resource  IsDeleted = <n> IsHistory = <n> ResourceTypeId = @p
```

</details>

### Divergent: `/Location?_id=ignixa-inc-loc-self&_include=Location:partof`

Only the shipping engine does:
- `filter Row < <v>`

Only the compiler does:
- `table Resource`
- `filter ResourceTypeId = <v> (x2)`
- `filter col:BaseUri is-null`

Operator differences (encoding, not semantics):
- `legacy: op distinct (x3)`
- `legacy: op in`
- `legacy: op row-number`
- `legacy: op top`

<details><summary>shapes</summary>

```
legacy:
cte0 = Resource  IsDeleted = <n> IsHistory = <n> ResourceId = @p ResourceTypeId = <n>
cte1 = <-cte0  [distinct,order-by,row-number,top]
select0 = Resource  <-cte4  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte1 = 
cte2 = ReferenceSearchParam+Resource  <-cte1  IsDeleted = <n> IsHistory = <n> SearchParamId = <n> sub:Row < @p  [correlate,correlate,correlate,correlate,distinct,exists,in,inner-join,top]
cte3 = <-cte2  [count-big,distinct,top]
cte4 = <-cte1,cte3  [correlate,correlate,exists,not,union-all]

compiler:
select0 = <-cteMatchPage,inc0lim  [correlate,correlate,exists,not,union-all]
cte0 = Resource  IsDeleted = <n> IsHistory = <n> ResourceTypeId = @p
cteMatchPage = Resource  <-cte0  ResourceId = @p  [correlate,correlate,inner-join]
inc0 = ReferenceSearchParam+Resource  <-cteMatchPage  IsDeleted = <n> IsHistory = <n> ResourceTypeId = <n> ResourceTypeId = <n> SearchParamId = <n> col:BaseUri is-null  [correlate,correlate,correlate,correlate,distinct,exists,inner-join,order-by,top]
inc0lim = <-inc0  [count-big,order-by,top]
```

</details>

### Divergent: `/MedicationDispense?_id=ignixa-inco-md1&_include=MedicationDispense:prescription&_include:iterate=MedicationRequest:subject&_includesCount=1`

Only the shipping engine does:
- `filter Row < <v>`

Only the compiler does:
- `table Resource`
- `filter ResourceTypeId = <v> (x5)`
- `filter col:BaseUri is-null (x2)`

Operator differences (encoding, not semantics):
- `legacy: op distinct (x4)`
- `legacy: op in (x2)`
- `legacy: op row-number`
- `legacy: op top`
- `compiler: op or`
- `compiler: op order-by (x2)`

<details><summary>shapes</summary>

```
legacy:
cte0 = Resource  IsDeleted = <n> IsHistory = <n> ResourceId = @p ResourceTypeId = <n>
cte1 = <-cte0  [distinct,order-by,row-number,top]
select0 = Resource  <-cte6  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte1 = 
cte2 = ReferenceSearchParam+Resource  <-cte1  IsDeleted = <n> IsHistory = <n> SearchParamId = <n> sub:Row < @p  [correlate,correlate,correlate,correlate,distinct,exists,in,inner-join,top]
cte3 = <-cte2  [count-big,distinct,top]
cte4 = ReferenceSearchParam+Resource  <-cte3  IsDeleted = <n> IsHistory = <n> SearchParamId = <n>  [correlate,correlate,correlate,correlate,distinct,exists,in,inner-join,top]
cte5 = <-cte4  [count-big,distinct,top]
cte6 = <-cte1,cte3,cte5  [correlate,correlate,correlate,correlate,exists,exists,not,not,union-all,union-all]

compiler:
select0 = <-cteMatchPage,inc0lim,inc1lim  [correlate,correlate,correlate,correlate,exists,exists,not,not,union-all,union-all]
cte0 = Resource  IsDeleted = <n> IsHistory = <n> ResourceTypeId = @p
cteMatchPage = Resource  <-cte0  ResourceId = @p  [correlate,correlate,inner-join]
inc0 = ReferenceSearchParam+Resource  <-cteMatchPage  IsDeleted = <n> IsHistory = <n> ResourceTypeId = <n> ResourceTypeId = <n> SearchParamId = <n> col:BaseUri is-null  [correlate,correlate,correlate,correlate,distinct,exists,inner-join,order-by,top]
inc0lim = <-inc0  [count-big,order-by,top]
inc1 = ReferenceSearchParam+Resource  <-inc0lim  IsDeleted = <n> IsHistory = <n> ResourceTypeId = <n> ResourceTypeId = <n> ResourceTypeId = <n> SearchParamId = <n> col:BaseUri is-null  [correlate,correlate,correlate,correlate,distinct,exists,inner-join,or,order-by,top]
inc1lim = <-inc1  [count-big,order-by,top]
```

</details>

### Divergent: `/Observation?_id=ignixa-inc-obs1&_include=Observation:performer`

Only the shipping engine does:
- `filter Row < <v>`

Only the compiler does:
- `table Resource`
- `filter ResourceTypeId = <v> (x7)`
- `filter col:BaseUri is-null`

Operator differences (encoding, not semantics):
- `legacy: op distinct (x3)`
- `legacy: op in`
- `legacy: op row-number`
- `legacy: op top`
- `compiler: op or (x5)`

<details><summary>shapes</summary>

```
legacy:
cte0 = Resource  IsDeleted = <n> IsHistory = <n> ResourceId = @p ResourceTypeId = <n>
cte1 = <-cte0  [distinct,order-by,row-number,top]
select0 = Resource  <-cte4  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte1 = 
cte2 = ReferenceSearchParam+Resource  <-cte1  IsDeleted = <n> IsHistory = <n> SearchParamId = <n> sub:Row < @p  [correlate,correlate,correlate,correlate,distinct,exists,in,inner-join,top]
cte3 = <-cte2  [count-big,distinct,top]
cte4 = <-cte1,cte3  [correlate,correlate,exists,not,union-all]

compiler:
select0 = <-cteMatchPage,inc0lim  [correlate,correlate,exists,not,union-all]
cte0 = Resource  IsDeleted = <n> IsHistory = <n> ResourceTypeId = @p
cteMatchPage = Resource  <-cte0  ResourceId = @p  [correlate,correlate,inner-join]
inc0 = ReferenceSearchParam+Resource  <-cteMatchPage  IsDeleted = <n> IsHistory = <n> ResourceTypeId = <n> ResourceTypeId = <n> ResourceTypeId = <n> ResourceTypeId = <n> ResourceTypeId = <n> ResourceTypeId = <n> ResourceTypeId = <n> SearchParamId = <n> col:BaseUri is-null  [correlate,correlate,correlate,correlate,distinct,exists,inner-join,or,or,or,or,or,order-by,top]
inc0lim = <-inc0  [count-big,order-by,top]
```

</details>

### Divergent: `/Observation?_id=ignixa-inc-obs1&_include=Observation:subject&_include:iterate=Patient:general-practitioner`

Only the shipping engine does:
- `filter Row < <v>`

Only the compiler does:
- `table Resource`
- `filter ResourceTypeId = <v> (x9)`
- `filter col:BaseUri is-null (x2)`

Operator differences (encoding, not semantics):
- `legacy: op distinct (x4)`
- `legacy: op in (x2)`
- `legacy: op row-number`
- `legacy: op top`
- `compiler: op or (x5)`
- `compiler: op order-by (x2)`

<details><summary>shapes</summary>

```
legacy:
cte0 = Resource  IsDeleted = <n> IsHistory = <n> ResourceId = @p ResourceTypeId = <n>
cte1 = <-cte0  [distinct,order-by,row-number,top]
select0 = Resource  <-cte6  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte1 = 
cte2 = ReferenceSearchParam+Resource  <-cte1  IsDeleted = <n> IsHistory = <n> SearchParamId = <n> sub:Row < @p  [correlate,correlate,correlate,correlate,distinct,exists,in,inner-join,top]
cte3 = <-cte2  [count-big,distinct,top]
cte4 = ReferenceSearchParam+Resource  <-cte3  IsDeleted = <n> IsHistory = <n> SearchParamId = <n>  [correlate,correlate,correlate,correlate,distinct,exists,in,inner-join,top]
cte5 = <-cte4  [count-big,distinct,top]
cte6 = <-cte1,cte3,cte5  [correlate,correlate,correlate,correlate,exists,exists,not,not,union-all,union-all]

compiler:
select0 = <-cteMatchPage,inc0lim,inc1lim  [correlate,correlate,correlate,correlate,exists,exists,not,not,union-all,union-all]
cte0 = Resource  IsDeleted = <n> IsHistory = <n> ResourceTypeId = @p
cteMatchPage = Resource  <-cte0  ResourceId = @p  [correlate,correlate,inner-join]
inc0 = ReferenceSearchParam+Resource  <-cteMatchPage  IsDeleted = <n> IsHistory = <n> ResourceTypeId = <n> ResourceTypeId = <n> ResourceTypeId = <n> ResourceTypeId = <n> ResourceTypeId = <n> SearchParamId = <n> col:BaseUri is-null  [correlate,correlate,correlate,correlate,distinct,exists,inner-join,or,or,or,order-by,top]
inc0lim = <-inc0  [count-big,order-by,top]
inc1 = ReferenceSearchParam+Resource  <-inc0lim  IsDeleted = <n> IsHistory = <n> ResourceTypeId = <n> ResourceTypeId = <n> ResourceTypeId = <n> ResourceTypeId = <n> SearchParamId = <n> col:BaseUri is-null  [correlate,correlate,correlate,correlate,distinct,exists,inner-join,or,or,order-by,top]
inc1lim = <-inc1  [count-big,order-by,top]
```

</details>

### Divergent: `/Observation?_id=ignixa-inc-obs1&_include=Observation:subject&_include:iterate=Patient:organization`

Only the shipping engine does:
- `filter Row < <v>`

Only the compiler does:
- `table Resource`
- `filter ResourceTypeId = <v> (x7)`
- `filter col:BaseUri is-null (x2)`

Operator differences (encoding, not semantics):
- `legacy: op distinct (x4)`
- `legacy: op in (x2)`
- `legacy: op row-number`
- `legacy: op top`
- `compiler: op or (x3)`
- `compiler: op order-by (x2)`

<details><summary>shapes</summary>

```
legacy:
cte0 = Resource  IsDeleted = <n> IsHistory = <n> ResourceId = @p ResourceTypeId = <n>
cte1 = <-cte0  [distinct,order-by,row-number,top]
select0 = Resource  <-cte6  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte1 = 
cte2 = ReferenceSearchParam+Resource  <-cte1  IsDeleted = <n> IsHistory = <n> SearchParamId = <n> sub:Row < @p  [correlate,correlate,correlate,correlate,distinct,exists,in,inner-join,top]
cte3 = <-cte2  [count-big,distinct,top]
cte4 = ReferenceSearchParam+Resource  <-cte3  IsDeleted = <n> IsHistory = <n> SearchParamId = <n>  [correlate,correlate,correlate,correlate,distinct,exists,in,inner-join,top]
cte5 = <-cte4  [count-big,distinct,top]
cte6 = <-cte1,cte3,cte5  [correlate,correlate,correlate,correlate,exists,exists,not,not,union-all,union-all]

compiler:
select0 = <-cteMatchPage,inc0lim,inc1lim  [correlate,correlate,correlate,correlate,exists,exists,not,not,union-all,union-all]
cte0 = Resource  IsDeleted = <n> IsHistory = <n> ResourceTypeId = @p
cteMatchPage = Resource  <-cte0  ResourceId = @p  [correlate,correlate,inner-join]
inc0 = ReferenceSearchParam+Resource  <-cteMatchPage  IsDeleted = <n> IsHistory = <n> ResourceTypeId = <n> ResourceTypeId = <n> ResourceTypeId = <n> ResourceTypeId = <n> ResourceTypeId = <n> SearchParamId = <n> col:BaseUri is-null  [correlate,correlate,correlate,correlate,distinct,exists,inner-join,or,or,or,order-by,top]
inc0lim = <-inc0  [count-big,order-by,top]
inc1 = ReferenceSearchParam+Resource  <-inc0lim  IsDeleted = <n> IsHistory = <n> ResourceTypeId = <n> ResourceTypeId = <n> SearchParamId = <n> col:BaseUri is-null  [correlate,correlate,correlate,correlate,distinct,exists,inner-join,order-by,top]
inc1lim = <-inc1  [count-big,order-by,top]
```

</details>

### Divergent: `/Observation?code=ignixa-date-test&date=1980-05-16T16:32:15.500Z`

Only the shipping engine does:
- `filter EndDateTime >= <v>`
- `filter StartDateTime <= <v>`

Only the compiler does:
- `filter EndDateTime <= <v>`
- `filter StartDateTime >= <v>`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte2  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n>
cte1 = DateTimeSearchParam  <-cte0  EndDateTime >= @p ResourceTypeId = <n> SearchParamId = <n> StartDateTime <= @p  [correlate,correlate,exists]
cte2 = <-cte1  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte1 = DateTimeSearchParam  EndDateTime <= @p ResourceTypeId = <n> SearchParamId = <n> StartDateTime >= @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### Divergent: `/Observation?combo-code-value-quantity=http://loinc.org%7C8480-6$107%7Chttp://unitsofmeasure.org%7Cmm%5BHg%5D&identifier=http://ignixa.io/testscript/suite/composite%7C`

Only the shipping engine does:
- `table TokenQuantityCompositeSearchParam`
- `filter Code1 = <v>`
- `filter HighValue2 >= <v>`
- `filter LowValue2 <= <v>`
- `filter QuantityCodeId2 = <v>`
- `filter ResourceTypeId = <v>`
- `filter SearchParamId = <v>`
- `filter SingleValue2 <= <v>`
- `filter SingleValue2 >= <v>`
- `filter SystemId1 = <v>`
- `filter SystemId2 = <v>`
- `filter col:SingleValue2 is-not-null (x2)`

Only the compiler does:
- `filter HighValue2 <= <v>`
- `filter LowValue2 >= <v>`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`
- `legacy: op union-all`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte3  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = TokenQuantityCompositeSearchParam  Code1 = @p QuantityCodeId2 = @p ResourceTypeId = <n> SearchParamId = <n> SingleValue2 <= @p SingleValue2 >= @p SystemId1 = @p SystemId2 = @p col:SingleValue2 is-not-null col:SingleValue2 is-not-null
cte1 = TokenQuantityCompositeSearchParam  <-cte0  Code1 = @p HighValue2 >= @p LowValue2 <= @p QuantityCodeId2 = @p ResourceTypeId = <n> SearchParamId = <n> SystemId1 = @p SystemId2 = @p  [union-all]
cte2 = TokenSearchParam  <-cte1  ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte3 = <-cte2  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = TokenQuantityCompositeSearchParam  Code1 = @p HighValue2 <= @p LowValue2 >= @p QuantityCodeId2 = @p ResourceTypeId = <n> SearchParamId = <n> SystemId1 = @p SystemId2 = @p  [distinct]
cte1 = TokenSearchParam  ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### Divergent: `/Observation?combo-code-value-quantity=http://loinc.org%7C8480-6$120%7Chttp://unitsofmeasure.org%7CmmHg&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-impsrch-suite&_count=100`

Only the shipping engine does:
- `table TokenQuantityCompositeSearchParam`
- `filter Code1 = <v>`
- `filter HighValue2 >= <v>`
- `filter LowValue2 <= <v>`
- `filter QuantityCodeId2 = <v>`
- `filter ResourceTypeId = <v>`
- `filter SearchParamId = <v>`
- `filter SingleValue2 <= <v>`
- `filter SingleValue2 >= <v>`
- `filter SystemId1 = <v>`
- `filter SystemId2 = <v>`
- `filter col:SingleValue2 is-not-null (x2)`

Only the compiler does:
- `filter HighValue2 <= <v>`
- `filter LowValue2 >= <v>`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`
- `legacy: op union-all`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte3  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = TokenQuantityCompositeSearchParam  Code1 = @p QuantityCodeId2 = @p ResourceTypeId = <n> SearchParamId = <n> SingleValue2 <= @p SingleValue2 >= @p SystemId1 = @p SystemId2 = @p col:SingleValue2 is-not-null col:SingleValue2 is-not-null
cte1 = TokenQuantityCompositeSearchParam  <-cte0  Code1 = @p HighValue2 >= @p LowValue2 <= @p QuantityCodeId2 = @p ResourceTypeId = <n> SearchParamId = <n> SystemId1 = @p SystemId2 = @p  [union-all]
cte2 = TokenSearchParam  <-cte1  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte3 = <-cte2  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = TokenQuantityCompositeSearchParam  Code1 = @p HighValue2 <= @p LowValue2 >= @p QuantityCodeId2 = @p ResourceTypeId = <n> SearchParamId = <n> SystemId1 = @p SystemId2 = @p  [distinct]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### Divergent: `/Observation?combo-code-value-quantity=http://loinc.org%7C8480-6$60&identifier=http://ignixa.io/testscript/suite/composite%7C`

Only the shipping engine does:
- `table TokenQuantityCompositeSearchParam`
- `filter Code1 = <v>`
- `filter HighValue2 >= <v>`
- `filter LowValue2 <= <v>`
- `filter ResourceTypeId = <v>`
- `filter SearchParamId = <v>`
- `filter SingleValue2 <= <v>`
- `filter SingleValue2 >= <v>`
- `filter SystemId1 = <v>`
- `filter col:SingleValue2 is-not-null (x2)`

Only the compiler does:
- `filter HighValue2 <= <v>`
- `filter LowValue2 >= <v>`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`
- `legacy: op union-all`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte3  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = TokenQuantityCompositeSearchParam  Code1 = @p ResourceTypeId = <n> SearchParamId = <n> SingleValue2 <= @p SingleValue2 >= @p SystemId1 = @p col:SingleValue2 is-not-null col:SingleValue2 is-not-null
cte1 = TokenQuantityCompositeSearchParam  <-cte0  Code1 = @p HighValue2 >= @p LowValue2 <= @p ResourceTypeId = <n> SearchParamId = <n> SystemId1 = @p  [union-all]
cte2 = TokenSearchParam  <-cte1  ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte3 = <-cte2  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = TokenQuantityCompositeSearchParam  Code1 = @p HighValue2 <= @p LowValue2 >= @p ResourceTypeId = <n> SearchParamId = <n> SystemId1 = @p  [distinct]
cte1 = TokenSearchParam  ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### Divergent: `/Observation?combo-code-value-quantity=http://loinc.org%7C8480-6$80%7Chttp://unitsofmeasure.org%7CmmHg&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-impsrch-suite&_count=100`

Only the shipping engine does:
- `table TokenQuantityCompositeSearchParam`
- `filter Code1 = <v>`
- `filter HighValue2 >= <v>`
- `filter LowValue2 <= <v>`
- `filter QuantityCodeId2 = <v>`
- `filter ResourceTypeId = <v>`
- `filter SearchParamId = <v>`
- `filter SingleValue2 <= <v>`
- `filter SingleValue2 >= <v>`
- `filter SystemId1 = <v>`
- `filter SystemId2 = <v>`
- `filter col:SingleValue2 is-not-null (x2)`

Only the compiler does:
- `filter HighValue2 <= <v>`
- `filter LowValue2 >= <v>`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`
- `legacy: op union-all`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte3  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = TokenQuantityCompositeSearchParam  Code1 = @p QuantityCodeId2 = @p ResourceTypeId = <n> SearchParamId = <n> SingleValue2 <= @p SingleValue2 >= @p SystemId1 = @p SystemId2 = @p col:SingleValue2 is-not-null col:SingleValue2 is-not-null
cte1 = TokenQuantityCompositeSearchParam  <-cte0  Code1 = @p HighValue2 >= @p LowValue2 <= @p QuantityCodeId2 = @p ResourceTypeId = <n> SearchParamId = <n> SystemId1 = @p SystemId2 = @p  [union-all]
cte2 = TokenSearchParam  <-cte1  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte3 = <-cte2  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = TokenQuantityCompositeSearchParam  Code1 = @p HighValue2 <= @p LowValue2 >= @p QuantityCodeId2 = @p ResourceTypeId = <n> SearchParamId = <n> SystemId1 = @p SystemId2 = @p  [distinct]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### Divergent: `/Observation?date=1980&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-impsrch-suite&_count=100`

Only the shipping engine does:
- `filter EndDateTime >= <v>`
- `filter StartDateTime <= <v>`

Only the compiler does:
- `filter EndDateTime <= <v>`
- `filter StartDateTime >= <v>`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte2  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = DateTimeSearchParam  EndDateTime >= @p ResourceTypeId = <n> SearchParamId = <n> StartDateTime <= @p
cte1 = TokenSearchParam  <-cte0  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte2 = <-cte1  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = DateTimeSearchParam  EndDateTime <= @p ResourceTypeId = <n> SearchParamId = <n> StartDateTime >= @p  [distinct]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### Divergent: `/Observation?date=1980-05-11&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-impsrch-suite&_count=100`

Only the shipping engine does:
- `filter EndDateTime >= <v>`
- `filter StartDateTime <= <v>`

Only the compiler does:
- `filter EndDateTime <= <v>`
- `filter StartDateTime >= <v>`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte2  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = DateTimeSearchParam  EndDateTime >= @p ResourceTypeId = <n> SearchParamId = <n> StartDateTime <= @p
cte1 = TokenSearchParam  <-cte0  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte2 = <-cte1  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = DateTimeSearchParam  EndDateTime <= @p ResourceTypeId = <n> SearchParamId = <n> StartDateTime >= @p  [distinct]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### Divergent: `/Observation?value-quantity=2e-3&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-qty-suite&_count=100`

Only the shipping engine does:
- `table QuantitySearchParam`
- `filter HighValue >= <v>`
- `filter LowValue <= <v>`
- `filter ResourceTypeId = <v>`
- `filter SearchParamId = <v>`
- `filter SingleValue <= <v>`
- `filter SingleValue >= <v>`
- `filter col:SingleValue is-not-null (x2)`

Only the compiler does:
- `filter HighValue <= <v>`
- `filter LowValue >= <v>`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`
- `legacy: op union-all`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte3  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = QuantitySearchParam  ResourceTypeId = <n> SearchParamId = <n> SingleValue <= @p SingleValue >= @p col:SingleValue is-not-null col:SingleValue is-not-null
cte1 = QuantitySearchParam  <-cte0  HighValue >= @p LowValue <= @p ResourceTypeId = <n> SearchParamId = <n>  [union-all]
cte2 = TokenSearchParam  <-cte1  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte3 = <-cte2  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = QuantitySearchParam  HighValue <= @p LowValue >= @p ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### Divergent: `/Observation?value-quantity=5%7C%7Cunit2&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-impsrch-suite&_count=100`

Only the shipping engine does:
- `table QuantitySearchParam`
- `filter HighValue >= <v>`
- `filter LowValue <= <v>`
- `filter QuantityCodeId = <v>`
- `filter ResourceTypeId = <v>`
- `filter SearchParamId = <v>`
- `filter SingleValue <= <v>`
- `filter SingleValue >= <v>`
- `filter col:SingleValue is-not-null (x2)`

Only the compiler does:
- `filter HighValue <= <v>`
- `filter LowValue >= <v>`
- `filter col:SystemId is-null`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`
- `legacy: op union-all`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte3  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = QuantitySearchParam  QuantityCodeId = @p ResourceTypeId = <n> SearchParamId = <n> SingleValue <= @p SingleValue >= @p col:SingleValue is-not-null col:SingleValue is-not-null
cte1 = QuantitySearchParam  <-cte0  HighValue >= @p LowValue <= @p QuantityCodeId = @p ResourceTypeId = <n> SearchParamId = <n>  [union-all]
cte2 = TokenSearchParam  <-cte1  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte3 = <-cte2  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = QuantitySearchParam  HighValue <= @p LowValue >= @p QuantityCodeId = @p ResourceTypeId = <n> SearchParamId = <n> col:SystemId is-null  [distinct]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### Divergent: `/Observation?value-quantity=5%7C%7Cunit2&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-qty-suite&_count=100`

Only the shipping engine does:
- `table QuantitySearchParam`
- `filter HighValue >= <v>`
- `filter LowValue <= <v>`
- `filter QuantityCodeId = <v>`
- `filter ResourceTypeId = <v>`
- `filter SearchParamId = <v>`
- `filter SingleValue <= <v>`
- `filter SingleValue >= <v>`
- `filter col:SingleValue is-not-null (x2)`

Only the compiler does:
- `filter HighValue <= <v>`
- `filter LowValue >= <v>`
- `filter col:SystemId is-null`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`
- `legacy: op union-all`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte3  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = QuantitySearchParam  QuantityCodeId = @p ResourceTypeId = <n> SearchParamId = <n> SingleValue <= @p SingleValue >= @p col:SingleValue is-not-null col:SingleValue is-not-null
cte1 = QuantitySearchParam  <-cte0  HighValue >= @p LowValue <= @p QuantityCodeId = @p ResourceTypeId = <n> SearchParamId = <n>  [union-all]
cte2 = TokenSearchParam  <-cte1  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte3 = <-cte2  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = QuantitySearchParam  HighValue <= @p LowValue >= @p QuantityCodeId = @p ResourceTypeId = <n> SearchParamId = <n> col:SystemId is-null  [distinct]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### Divergent: `/Observation?value-quantity=5%7Csystem1%7Cunit2&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-qty-suite&_count=100`

Only the shipping engine does:
- `table QuantitySearchParam`
- `filter HighValue >= <v>`
- `filter LowValue <= <v>`
- `filter QuantityCodeId = <v>`
- `filter ResourceTypeId = <v>`
- `filter SearchParamId = <v>`
- `filter SingleValue <= <v>`
- `filter SingleValue >= <v>`
- `filter SystemId = <v>`
- `filter col:SingleValue is-not-null (x2)`

Only the compiler does:
- `filter HighValue <= <v>`
- `filter LowValue >= <v>`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`
- `legacy: op union-all`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte3  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = QuantitySearchParam  QuantityCodeId = @p ResourceTypeId = <n> SearchParamId = <n> SingleValue <= @p SingleValue >= @p SystemId = @p col:SingleValue is-not-null col:SingleValue is-not-null
cte1 = QuantitySearchParam  <-cte0  HighValue >= @p LowValue <= @p QuantityCodeId = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [union-all]
cte2 = TokenSearchParam  <-cte1  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte3 = <-cte2  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = QuantitySearchParam  HighValue <= @p LowValue >= @p QuantityCodeId = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### Divergent: `/Observation?value-quantity=5%7Csystem1&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-impsrch-suite&_count=100`

Only the shipping engine does:
- `table QuantitySearchParam`
- `filter HighValue >= <v>`
- `filter LowValue <= <v>`
- `filter ResourceTypeId = <v>`
- `filter SearchParamId = <v>`
- `filter SingleValue <= <v>`
- `filter SingleValue >= <v>`
- `filter SystemId = <v>`
- `filter col:SingleValue is-not-null (x2)`

Only the compiler does:
- `filter HighValue <= <v>`
- `filter LowValue >= <v>`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`
- `legacy: op union-all`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte3  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = QuantitySearchParam  ResourceTypeId = <n> SearchParamId = <n> SingleValue <= @p SingleValue >= @p SystemId = @p col:SingleValue is-not-null col:SingleValue is-not-null
cte1 = QuantitySearchParam  <-cte0  HighValue >= @p LowValue <= @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [union-all]
cte2 = TokenSearchParam  <-cte1  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte3 = <-cte2  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = QuantitySearchParam  HighValue <= @p LowValue >= @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### Divergent: `/Observation?value-quantity=5%7Csystem1&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-qty-suite&_count=100`

Only the shipping engine does:
- `table QuantitySearchParam`
- `filter HighValue >= <v>`
- `filter LowValue <= <v>`
- `filter ResourceTypeId = <v>`
- `filter SearchParamId = <v>`
- `filter SingleValue <= <v>`
- `filter SingleValue >= <v>`
- `filter SystemId = <v>`
- `filter col:SingleValue is-not-null (x2)`

Only the compiler does:
- `filter HighValue <= <v>`
- `filter LowValue >= <v>`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`
- `legacy: op union-all`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte3  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = QuantitySearchParam  ResourceTypeId = <n> SearchParamId = <n> SingleValue <= @p SingleValue >= @p SystemId = @p col:SingleValue is-not-null col:SingleValue is-not-null
cte1 = QuantitySearchParam  <-cte0  HighValue >= @p LowValue <= @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [union-all]
cte2 = TokenSearchParam  <-cte1  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte3 = <-cte2  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = QuantitySearchParam  HighValue <= @p LowValue >= @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### Divergent: `/Observation?value-quantity=5&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-impsrch-suite&_count=100`

Only the shipping engine does:
- `table QuantitySearchParam`
- `filter HighValue >= <v>`
- `filter LowValue <= <v>`
- `filter ResourceTypeId = <v>`
- `filter SearchParamId = <v>`
- `filter SingleValue <= <v>`
- `filter SingleValue >= <v>`
- `filter col:SingleValue is-not-null (x2)`

Only the compiler does:
- `filter HighValue <= <v>`
- `filter LowValue >= <v>`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`
- `legacy: op union-all`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte3  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = QuantitySearchParam  ResourceTypeId = <n> SearchParamId = <n> SingleValue <= @p SingleValue >= @p col:SingleValue is-not-null col:SingleValue is-not-null
cte1 = QuantitySearchParam  <-cte0  HighValue >= @p LowValue <= @p ResourceTypeId = <n> SearchParamId = <n>  [union-all]
cte2 = TokenSearchParam  <-cte1  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte3 = <-cte2  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = QuantitySearchParam  HighValue <= @p LowValue >= @p ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### Divergent: `/Observation?value-quantity=5&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-qty-suite&_count=100`

Only the shipping engine does:
- `table QuantitySearchParam`
- `filter HighValue >= <v>`
- `filter LowValue <= <v>`
- `filter ResourceTypeId = <v>`
- `filter SearchParamId = <v>`
- `filter SingleValue <= <v>`
- `filter SingleValue >= <v>`
- `filter col:SingleValue is-not-null (x2)`

Only the compiler does:
- `filter HighValue <= <v>`
- `filter LowValue >= <v>`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`
- `legacy: op union-all`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte3  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = QuantitySearchParam  ResourceTypeId = <n> SearchParamId = <n> SingleValue <= @p SingleValue >= @p col:SingleValue is-not-null col:SingleValue is-not-null
cte1 = QuantitySearchParam  <-cte0  HighValue >= @p LowValue <= @p ResourceTypeId = <n> SearchParamId = <n>  [union-all]
cte2 = TokenSearchParam  <-cte1  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte3 = <-cte2  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = QuantitySearchParam  HighValue <= @p LowValue >= @p ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### Divergent: `/Observation?value-quantity=eb5&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-qty-suite&_count=100`

Only the shipping engine does:
- `table QuantitySearchParam`
- `filter LowValue < <v>`
- `filter ResourceTypeId = <v>`
- `filter SearchParamId = <v>`
- `filter SingleValue < <v>`
- `filter col:SingleValue is-not-null`

Only the compiler does:
- `filter HighValue < <v>`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`
- `legacy: op union-all`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte3  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = QuantitySearchParam  ResourceTypeId = <n> SearchParamId = <n> SingleValue < @p col:SingleValue is-not-null
cte1 = QuantitySearchParam  <-cte0  LowValue < @p ResourceTypeId = <n> SearchParamId = <n>  [union-all]
cte2 = TokenSearchParam  <-cte1  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte3 = <-cte2  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = QuantitySearchParam  HighValue < @p ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### Divergent: `/Observation?value-quantity=eq5&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-qty-suite&_count=100`

Only the shipping engine does:
- `table QuantitySearchParam`
- `filter HighValue >= <v>`
- `filter LowValue <= <v>`
- `filter ResourceTypeId = <v>`
- `filter SearchParamId = <v>`
- `filter SingleValue <= <v>`
- `filter SingleValue >= <v>`
- `filter col:SingleValue is-not-null (x2)`

Only the compiler does:
- `filter HighValue <= <v>`
- `filter LowValue >= <v>`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`
- `legacy: op union-all`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte3  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = QuantitySearchParam  ResourceTypeId = <n> SearchParamId = <n> SingleValue <= @p SingleValue >= @p col:SingleValue is-not-null col:SingleValue is-not-null
cte1 = QuantitySearchParam  <-cte0  HighValue >= @p LowValue <= @p ResourceTypeId = <n> SearchParamId = <n>  [union-all]
cte2 = TokenSearchParam  <-cte1  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte3 = <-cte2  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = QuantitySearchParam  HighValue <= @p LowValue >= @p ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### Divergent: `/Observation?value-quantity=sa5&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-qty-suite&_count=100`

Only the shipping engine does:
- `table QuantitySearchParam`
- `filter HighValue > <v>`
- `filter ResourceTypeId = <v>`
- `filter SearchParamId = <v>`
- `filter SingleValue > <v>`
- `filter col:SingleValue is-not-null`

Only the compiler does:
- `filter LowValue > <v>`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`
- `legacy: op union-all`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte3  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = QuantitySearchParam  ResourceTypeId = <n> SearchParamId = <n> SingleValue > @p col:SingleValue is-not-null
cte1 = QuantitySearchParam  <-cte0  HighValue > @p ResourceTypeId = <n> SearchParamId = <n>  [union-all]
cte2 = TokenSearchParam  <-cte1  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte3 = <-cte2  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = QuantitySearchParam  LowValue > @p ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### Divergent: `/Patient/ignixa-cmp-pat1/Observation?performer=Practitioner/ignixa-cmp-prac-other`

Only the shipping engine does:
- `table ReferenceSearchParam (x2)`
- `filter ReferenceResourceId = <v> (x2)`
- `filter ReferenceResourceTypeId = <v> (x12)`
- `filter ResourceTypeId = <v> (x3)`
- `filter SearchParamId = <v> (x3)`
- `filter col:ReferenceResourceTypeId is-null (x2)`

Only the compiler does:
- `filter IsDeleted = <v>`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x6)`
- `legacy: op distinct (x2)`
- `legacy: op exists`
- `legacy: op inner-join (x2)`
- `legacy: op or (x11)`
- `legacy: op order-by`
- `legacy: op top`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte4  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = ReferenceSearchParam  ReferenceResourceId = @p ReferenceResourceTypeId = <n> ReferenceResourceTypeId = <n> ReferenceResourceTypeId = <n> ReferenceResourceTypeId = <n> ReferenceResourceTypeId = <n> ReferenceResourceTypeId = <n> ReferenceResourceTypeId = <n> ReferenceResourceTypeId = <n> ReferenceResourceTypeId = <n> ReferenceResourceTypeId = <n> ReferenceResourceTypeId = <n> ResourceTypeId = <n> ResourceTypeId = <n> SearchParamId = <n> SearchParamId = <n> col:ReferenceResourceTypeId is-null col:ReferenceResourceTypeId is-null  [or,or,or,or,or,or,or,or,or,or,or]
cte1 = <-cte0
cte2 = Resource  <-cte1  IsHistory = <n> ResourceTypeId = <n>  [correlate,correlate,inner-join]
cte3 = ReferenceSearchParam  <-cte2  ReferenceResourceId = @p ReferenceResourceTypeId = <n> ResourceTypeId = <n> SearchParamId = <n>  [correlate,correlate,exists]
cte4 = <-cte3  [distinct,order-by,top]

compiler:
select0 = <-cte0  [order-by]
cte0 = Resource  IsDeleted = <n> IsHistory = <n> ResourceTypeId = @p
```

</details>

### Divergent: `/Patient/ignixa-cmp-pat1/Observation?performer=Practitioner/ignixa-cmp-prac1`

Only the shipping engine does:
- `table ReferenceSearchParam (x2)`
- `filter ReferenceResourceId = <v> (x2)`
- `filter ReferenceResourceTypeId = <v> (x12)`
- `filter ResourceTypeId = <v> (x3)`
- `filter SearchParamId = <v> (x3)`
- `filter col:ReferenceResourceTypeId is-null (x2)`

Only the compiler does:
- `filter IsDeleted = <v>`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x6)`
- `legacy: op distinct (x2)`
- `legacy: op exists`
- `legacy: op inner-join (x2)`
- `legacy: op or (x11)`
- `legacy: op order-by`
- `legacy: op top`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte4  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = ReferenceSearchParam  ReferenceResourceId = @p ReferenceResourceTypeId = <n> ReferenceResourceTypeId = <n> ReferenceResourceTypeId = <n> ReferenceResourceTypeId = <n> ReferenceResourceTypeId = <n> ReferenceResourceTypeId = <n> ReferenceResourceTypeId = <n> ReferenceResourceTypeId = <n> ReferenceResourceTypeId = <n> ReferenceResourceTypeId = <n> ReferenceResourceTypeId = <n> ResourceTypeId = <n> ResourceTypeId = <n> SearchParamId = <n> SearchParamId = <n> col:ReferenceResourceTypeId is-null col:ReferenceResourceTypeId is-null  [or,or,or,or,or,or,or,or,or,or,or]
cte1 = <-cte0
cte2 = Resource  <-cte1  IsHistory = <n> ResourceTypeId = <n>  [correlate,correlate,inner-join]
cte3 = ReferenceSearchParam  <-cte2  ReferenceResourceId = @p ReferenceResourceTypeId = <n> ResourceTypeId = <n> SearchParamId = <n>  [correlate,correlate,exists]
cte4 = <-cte3  [distinct,order-by,top]

compiler:
select0 = <-cte0  [order-by]
cte0 = Resource  IsDeleted = <n> IsHistory = <n> ResourceTypeId = @p
```

</details>

### Divergent: `/Patient?_id=880fb2d8-f23b-47a5-a398-0939a79a9c5e,63220610-3dac-4eff-af3f-a179f187758b,b0ac0bcb-9599-48d8-b688-c7612377c558,38bfef7f-1475-48f7-812f-35c31261fc46&birthdate=ge1980-01-01&birthdate=lt2000-01-01`

Only the shipping engine does:
- `table Resource`
- `filter IsDeleted = <v>`
- `filter IsHistory = <v>`

Only the compiler does:
- `table DateTimeSearchParam`
- `filter SearchParamId = <v>`

Operator differences (encoding, not semantics):
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`
- `compiler: op inner-join`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte2  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = Resource  IsDeleted = <n> IsHistory = <n> ResourceId = @p ResourceId = @p ResourceId = @p ResourceId = @p ResourceTypeId = <n>  [or,or,or]
cte1 = DateTimeSearchParam  <-cte0  EndDateTime >= @p ResourceTypeId = <n> SearchParamId = <n> StartDateTime < @p  [correlate,correlate,exists]
cte2 = <-cte1  [distinct,order-by,top]

compiler:
select0 = Resource  <-cte2  ResourceId = @p ResourceId = @p ResourceId = @p ResourceId = @p  [correlate,correlate,inner-join,or,or,or,order-by]
cte0 = DateTimeSearchParam  EndDateTime >= @p ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte1 = DateTimeSearchParam  ResourceTypeId = <n> SearchParamId = <n> StartDateTime < @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### Divergent: `/Patient?_id=e5ec00b6-1167-49b3-a754-c2f8d57ab7cb`

Only the shipping engine does:
- `table DateTimeSearchParam`
- `table TokenSearchParam`
- `filter EndDateTime >= <v>`
- `filter ResourceTypeId = <v>`
- `filter SearchParamId = <v> (x2)`
- `filter SystemId = <v>`

Only the compiler does:
- `table Resource`
- `filter IsDeleted = <v>`
- `filter IsHistory = <v>`
- `filter ResourceId = <v>`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op distinct (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte2  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = DateTimeSearchParam  EndDateTime >= @p ResourceTypeId = <n> SearchParamId = <n>
cte1 = TokenSearchParam  <-cte0  ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte2 = <-cte1  [distinct,order-by,top]

compiler:
select0 = Resource  <-cte0  ResourceId = @p  [correlate,correlate,inner-join,order-by]
cte0 = Resource  IsDeleted = <n> IsHistory = <n> ResourceTypeId = @p
```

</details>

### Divergent: `/Patient?_not-referenced=Observation:*&identifier=http://ignixa.io/testscript/suite/ms-not-referenced%7C&_count=100`

Only the shipping engine does:
- `filter IsHistory = <v>`
- `filter SourceResourceTypeId = <v>`

Only the compiler does:
- `filter ResourceTypeId = <v>`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op distinct`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte2  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = TokenSearchParam  ResourceTypeId = <n> SearchParamId = <n> SystemId = @p
cte1 = Resource+sub:ReferenceSearchParam  <-cte0  IsDeleted = <n> IsHistory = <n> IsHistory = <n> ResourceTypeId = <n> sub:SourceResourceTypeId = <n>  [correlate,correlate,correlate,correlate,exists,exists,not]
cte2 = <-cte1  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = Resource+sub:ReferenceSearchParam  IsDeleted = <n> IsHistory = <n> ResourceTypeId = @p sub:ResourceTypeId = <n>  [correlate,correlate,exists,not]
cte1 = TokenSearchParam  ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### Divergent: `/Patient?_not-referenced=Observation:subject&identifier=http://ignixa.io/testscript/suite/ms-not-referenced%7C&_count=100`

Only the shipping engine does:
- `filter IsHistory = <v>`
- `filter SourceResourceTypeId = <v>`

Only the compiler does:
- `filter ResourceTypeId = <v>`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op distinct`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte2  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = TokenSearchParam  ResourceTypeId = <n> SearchParamId = <n> SystemId = @p
cte1 = Resource+sub:ReferenceSearchParam  <-cte0  IsDeleted = <n> IsHistory = <n> IsHistory = <n> ResourceTypeId = <n> sub:SearchParamId = <n> sub:SourceResourceTypeId = <n>  [correlate,correlate,correlate,correlate,exists,exists,not]
cte2 = <-cte1  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = Resource+sub:ReferenceSearchParam  IsDeleted = <n> IsHistory = <n> ResourceTypeId = @p sub:ResourceTypeId = <n> sub:SearchParamId = <n>  [correlate,correlate,exists,not]
cte1 = TokenSearchParam  ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### Divergent: `/Patient?address-city:contains=Vestibulum&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-impsrch-suite&_count=100`

Only the shipping engine does:
- `filter col:TextOverflow is-not-null`

Only the compiler does:
- `filter col:TextOverflow is-null`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte2  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = StringSearchParam  ResourceTypeId = <n> SearchParamId = <n> col:Text like @p col:TextOverflow is-not-null col:TextOverflow like @p  [or]
cte1 = TokenSearchParam  <-cte0  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte2 = <-cte1  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = StringSearchParam  ResourceTypeId = <n> SearchParamId = <n> col:Text like @p col:TextOverflow is-null col:TextOverflow like @p  [distinct,or]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### Divergent: `/Patient?address-city:contains=att&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-impsrch-suite&_count=100`

Only the shipping engine does:
- `filter col:TextOverflow is-not-null`

Only the compiler does:
- `filter col:TextOverflow is-null`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte2  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = StringSearchParam  ResourceTypeId = <n> SearchParamId = <n> col:Text like @p col:TextOverflow is-not-null col:TextOverflow like @p  [or]
cte1 = TokenSearchParam  <-cte0  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte2 = <-cte1  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = StringSearchParam  ResourceTypeId = <n> SearchParamId = <n> col:Text like @p col:TextOverflow is-null col:TextOverflow like @p  [distinct,or]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### Divergent: `/Patient?address-city:exact=Seattle&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-impsrch-suite&_count=100`

Only the shipping engine does:
- `filter Text = <v>`

Only the compiler does:
- `filter col:TextOverflow is-null`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte2  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = StringSearchParam  ResourceTypeId = <n> SearchParamId = <n> Text = @p Text = @p
cte1 = TokenSearchParam  <-cte0  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte2 = <-cte1  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = StringSearchParam  ResourceTypeId = <n> SearchParamId = <n> Text = @p col:TextOverflow is-null  [distinct]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### Divergent: `/Patient?address-city:exact=seattle&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-impsrch-suite&_count=100`

Only the shipping engine does:
- `filter Text = <v>`

Only the compiler does:
- `filter col:TextOverflow is-null`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte2  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = StringSearchParam  ResourceTypeId = <n> SearchParamId = <n> Text = @p Text = @p
cte1 = TokenSearchParam  <-cte0  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte2 = <-cte1  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = StringSearchParam  ResourceTypeId = <n> SearchParamId = <n> Text = @p col:TextOverflow is-null  [distinct]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### Divergent: `/Patient?family:contains=SON&identifier=http://fhir262/test%7C&_count=100`

Only the shipping engine does:
- `filter col:TextOverflow is-not-null`

Only the compiler does:
- `filter col:TextOverflow is-null`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte2  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = StringSearchParam  ResourceTypeId = <n> SearchParamId = <n> col:Text like @p col:TextOverflow is-not-null col:TextOverflow like @p  [or]
cte1 = TokenSearchParam  <-cte0  ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte2 = <-cte1  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = StringSearchParam  ResourceTypeId = <n> SearchParamId = <n> col:Text like @p col:TextOverflow is-null col:TextOverflow like @p  [distinct,or]
cte1 = TokenSearchParam  ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### Divergent: `/Patient?family:contains=son&identifier=http://fhir262/test%7C&_count=100`

Only the shipping engine does:
- `filter col:TextOverflow is-not-null`

Only the compiler does:
- `filter col:TextOverflow is-null`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte2  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = StringSearchParam  ResourceTypeId = <n> SearchParamId = <n> col:Text like @p col:TextOverflow is-not-null col:TextOverflow like @p  [or]
cte1 = TokenSearchParam  <-cte0  ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte2 = <-cte1  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = StringSearchParam  ResourceTypeId = <n> SearchParamId = <n> col:Text like @p col:TextOverflow is-null col:TextOverflow like @p  [distinct,or]
cte1 = TokenSearchParam  ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### Divergent: `/Patient?family:exact=Smith&identifier=http://fhir262/test%7C&_count=100`

Only the shipping engine does:
- `filter Text = <v>`

Only the compiler does:
- `filter col:TextOverflow is-null`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte2  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = StringSearchParam  ResourceTypeId = <n> SearchParamId = <n> Text = @p Text = @p
cte1 = TokenSearchParam  <-cte0  ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte2 = <-cte1  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = StringSearchParam  ResourceTypeId = <n> SearchParamId = <n> Text = @p col:TextOverflow is-null  [distinct]
cte1 = TokenSearchParam  ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### Divergent: `/Patient?family:exact=smith&identifier=http://fhir262/test%7C&_count=100`

Only the shipping engine does:
- `filter Text = <v>`

Only the compiler does:
- `filter col:TextOverflow is-null`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte2  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = StringSearchParam  ResourceTypeId = <n> SearchParamId = <n> Text = @p Text = @p
cte1 = TokenSearchParam  <-cte0  ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte2 = <-cte1  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = StringSearchParam  ResourceTypeId = <n> SearchParamId = <n> Text = @p col:TextOverflow is-null  [distinct]
cte1 = TokenSearchParam  ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### Divergent: `/Patient?given:exact=Alex,Carol&identifier=http://fhir262/test%7C&_count=100`

Only the shipping engine does:
- `filter Text = <v> (x2)`

Only the compiler does:
- `table StringSearchParam`
- `filter ResourceTypeId = <v>`
- `filter SearchParamId = <v>`
- `filter col:TextOverflow is-null (x2)`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op or`
- `legacy: op order-by`
- `legacy: op top`
- `compiler: op distinct`
- `compiler: op union`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte2  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = StringSearchParam  ResourceTypeId = <n> SearchParamId = <n> Text = @p Text = @p Text = @p Text = @p  [or]
cte1 = TokenSearchParam  <-cte0  ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte2 = <-cte1  [distinct,order-by,top]

compiler:
select0 = <-cte4  [order-by]
cte0 = StringSearchParam  ResourceTypeId = <n> SearchParamId = <n> Text = @p col:TextOverflow is-null  [distinct]
cte1 = StringSearchParam  ResourceTypeId = <n> SearchParamId = <n> Text = @p col:TextOverflow is-null  [distinct]
cte2 = <-cte0,cte1  [union]
cte3 = TokenSearchParam  ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte4 = <-cte2,cte3  [correlate,correlate,inner-join]
```

</details>

### Divergent: `/RiskAssessment?probability=5&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-impsrch-suite&_count=100`

Only the shipping engine does:
- `table NumberSearchParam`
- `filter HighValue >= <v>`
- `filter LowValue <= <v>`
- `filter ResourceTypeId = <v>`
- `filter SearchParamId = <v>`
- `filter SingleValue <= <v>`
- `filter SingleValue >= <v>`
- `filter col:SingleValue is-not-null (x2)`

Only the compiler does:
- `filter HighValue <= <v>`
- `filter LowValue >= <v>`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`
- `legacy: op union-all`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte3  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = NumberSearchParam  ResourceTypeId = <n> SearchParamId = <n> SingleValue <= @p SingleValue >= @p col:SingleValue is-not-null col:SingleValue is-not-null
cte1 = NumberSearchParam  <-cte0  HighValue >= @p LowValue <= @p ResourceTypeId = <n> SearchParamId = <n>  [union-all]
cte2 = TokenSearchParam  <-cte1  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte3 = <-cte2  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = NumberSearchParam  HighValue <= @p LowValue >= @p ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### Divergent: `/RiskAssessment?probability=5&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-num-suite&_count=100`

Only the shipping engine does:
- `table NumberSearchParam`
- `filter HighValue >= <v>`
- `filter LowValue <= <v>`
- `filter ResourceTypeId = <v>`
- `filter SearchParamId = <v>`
- `filter SingleValue <= <v>`
- `filter SingleValue >= <v>`
- `filter col:SingleValue is-not-null (x2)`

Only the compiler does:
- `filter HighValue <= <v>`
- `filter LowValue >= <v>`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`
- `legacy: op union-all`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte3  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = NumberSearchParam  ResourceTypeId = <n> SearchParamId = <n> SingleValue <= @p SingleValue >= @p col:SingleValue is-not-null col:SingleValue is-not-null
cte1 = NumberSearchParam  <-cte0  HighValue >= @p LowValue <= @p ResourceTypeId = <n> SearchParamId = <n>  [union-all]
cte2 = TokenSearchParam  <-cte1  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte3 = <-cte2  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = NumberSearchParam  HighValue <= @p LowValue >= @p ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### Divergent: `/RiskAssessment?probability=eb5&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-num-suite&_count=100`

Only the shipping engine does:
- `table NumberSearchParam`
- `filter LowValue < <v>`
- `filter ResourceTypeId = <v>`
- `filter SearchParamId = <v>`
- `filter SingleValue < <v>`
- `filter col:SingleValue is-not-null`

Only the compiler does:
- `filter HighValue < <v>`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`
- `legacy: op union-all`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte3  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = NumberSearchParam  ResourceTypeId = <n> SearchParamId = <n> SingleValue < @p col:SingleValue is-not-null
cte1 = NumberSearchParam  <-cte0  LowValue < @p ResourceTypeId = <n> SearchParamId = <n>  [union-all]
cte2 = TokenSearchParam  <-cte1  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte3 = <-cte2  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = NumberSearchParam  HighValue < @p ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### Divergent: `/RiskAssessment?probability=eq5&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-num-suite&_count=100`

Only the shipping engine does:
- `table NumberSearchParam`
- `filter HighValue >= <v>`
- `filter LowValue <= <v>`
- `filter ResourceTypeId = <v>`
- `filter SearchParamId = <v>`
- `filter SingleValue <= <v>`
- `filter SingleValue >= <v>`
- `filter col:SingleValue is-not-null (x2)`

Only the compiler does:
- `filter HighValue <= <v>`
- `filter LowValue >= <v>`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`
- `legacy: op union-all`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte3  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = NumberSearchParam  ResourceTypeId = <n> SearchParamId = <n> SingleValue <= @p SingleValue >= @p col:SingleValue is-not-null col:SingleValue is-not-null
cte1 = NumberSearchParam  <-cte0  HighValue >= @p LowValue <= @p ResourceTypeId = <n> SearchParamId = <n>  [union-all]
cte2 = TokenSearchParam  <-cte1  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte3 = <-cte2  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = NumberSearchParam  HighValue <= @p LowValue >= @p ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### Divergent: `/RiskAssessment?probability=sa5&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-num-suite&_count=100`

Only the shipping engine does:
- `table NumberSearchParam`
- `filter HighValue > <v>`
- `filter ResourceTypeId = <v>`
- `filter SearchParamId = <v>`
- `filter SingleValue > <v>`
- `filter col:SingleValue is-not-null`

Only the compiler does:
- `filter LowValue > <v>`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`
- `legacy: op union-all`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte3  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = NumberSearchParam  ResourceTypeId = <n> SearchParamId = <n> SingleValue > @p col:SingleValue is-not-null
cte1 = NumberSearchParam  <-cte0  HighValue > @p ResourceTypeId = <n> SearchParamId = <n>  [union-all]
cte2 = TokenSearchParam  <-cte1  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte3 = <-cte2  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = NumberSearchParam  LowValue > @p ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### Divergent: `/ValueSet?url:above=http://ignixa-unrelated.example.com/x&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-uri-suite`

Only the shipping engine does:
- `filter <v> like (col:Uri op <s>)`
- `filter col:Uri not-like <v>`

Only the compiler does:
- `filter <expr> = col:Uri`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte2  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = UriSearchParam  @p like (col:Uri op <s>) ResourceTypeId = <n> SearchParamId = <n> col:Uri not-like @p
cte1 = TokenSearchParam  <-cte0  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte2 = <-cte1  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = UriSearchParam  <expr> = col:Uri ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### Divergent: `/ValueSet?url:above=http://ignixa.example.com/rdf%2354135-9-9-10&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-uri-suite`

Only the shipping engine does:
- `filter <v> like (col:Uri op <s>)`
- `filter col:Uri not-like <v>`

Only the compiler does:
- `filter <expr> = col:Uri`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte2  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = UriSearchParam  @p like (col:Uri op <s>) ResourceTypeId = <n> SearchParamId = <n> col:Uri not-like @p
cte1 = TokenSearchParam  <-cte0  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte2 = <-cte1  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = UriSearchParam  <expr> = col:Uri ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### Divergent: `/ValueSet?url:above=http://ignixa.example.com/test/system/123&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-uri-suite`

Only the shipping engine does:
- `filter <v> like (col:Uri op <s>)`
- `filter col:Uri not-like <v>`

Only the compiler does:
- `filter <expr> = col:Uri`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte2  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = UriSearchParam  @p like (col:Uri op <s>) ResourceTypeId = <n> SearchParamId = <n> col:Uri not-like @p
cte1 = TokenSearchParam  <-cte0  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte2 = <-cte1  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = UriSearchParam  <expr> = col:Uri ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### Divergent: `/ValueSet?url:above=http://somewhere.com/test/system/123&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-impsrch-suite&_count=100`

Only the shipping engine does:
- `filter <v> like (col:Uri op <s>)`
- `filter col:Uri not-like <v>`

Only the compiler does:
- `filter <expr> = col:Uri`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte2  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = UriSearchParam  @p like (col:Uri op <s>) ResourceTypeId = <n> SearchParamId = <n> col:Uri not-like @p
cte1 = TokenSearchParam  <-cte0  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte2 = <-cte1  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = UriSearchParam  <expr> = col:Uri ResourceTypeId = <n> SearchParamId = <n>  [distinct]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### Divergent: `/ValueSet?url:below=http&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-impsrch-suite&_count=100`

Only the shipping engine does:
- `filter col:Uri not-like <v>`

Only the compiler does:
- `filter Uri = <v>`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`
- `compiler: op or`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte2  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = UriSearchParam  ResourceTypeId = <n> SearchParamId = <n> col:Uri like @p col:Uri not-like @p
cte1 = TokenSearchParam  <-cte0  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte2 = <-cte1  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = UriSearchParam  ResourceTypeId = <n> SearchParamId = <n> Uri = @p col:Uri like @p  [distinct,or]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### Divergent: `/ValueSet?url:below=http://ignixa-unrelated.example.com&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-uri-suite`

Only the shipping engine does:
- `filter col:Uri not-like <v>`

Only the compiler does:
- `filter Uri = <v>`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`
- `compiler: op or`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte2  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = UriSearchParam  ResourceTypeId = <n> SearchParamId = <n> col:Uri like @p col:Uri not-like @p
cte1 = TokenSearchParam  <-cte0  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte2 = <-cte1  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = UriSearchParam  ResourceTypeId = <n> SearchParamId = <n> Uri = @p col:Uri like @p  [distinct,or]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>

### Divergent: `/ValueSet?url:below=http://ignixa.example.com&_tag=http://ignixa.io/testscript/suite/test%7Cignixa-uri-suite`

Only the shipping engine does:
- `filter col:Uri not-like <v>`

Only the compiler does:
- `filter Uri = <v>`

Operator differences (encoding, not semantics):
- `legacy: op correlate (x2)`
- `legacy: op exists`
- `legacy: op order-by`
- `legacy: op top`
- `compiler: op or`

<details><summary>shapes</summary>

```
legacy:
select0 = Resource  <-cte2  IsDeleted = <n> IsHistory = <n>  [correlate,correlate,distinct,inner-join,order-by]
cte0 = UriSearchParam  ResourceTypeId = <n> SearchParamId = <n> col:Uri like @p col:Uri not-like @p
cte1 = TokenSearchParam  <-cte0  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [correlate,correlate,exists]
cte2 = <-cte1  [distinct,order-by,top]

compiler:
select0 = <-cte2  [order-by]
cte0 = UriSearchParam  ResourceTypeId = <n> SearchParamId = <n> Uri = @p col:Uri like @p  [distinct,or]
cte1 = TokenSearchParam  Code = @p ResourceTypeId = <n> SearchParamId = <n> SystemId = @p  [distinct]
cte2 = <-cte0,cte1  [correlate,correlate,inner-join]
```

</details>


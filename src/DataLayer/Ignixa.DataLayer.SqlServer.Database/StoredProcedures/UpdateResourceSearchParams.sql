CREATE PROCEDURE dbo.UpdateResourceSearchParams
@FailedResources INT=0 OUTPUT, @Resources dbo.ResourceList READONLY, @ResourceWriteClaims dbo.ResourceWriteClaimList READONLY, @ReferenceSearchParams dbo.ReferenceSearchParamList READONLY, @TokenSearchParams dbo.TokenSearchParamList READONLY, @TokenTexts dbo.TokenTextList READONLY, @StringSearchParams dbo.StringSearchParamList READONLY, @UriSearchParams dbo.UriSearchParamList READONLY, @NumberSearchParams dbo.NumberSearchParamList READONLY, @QuantitySearchParams dbo.QuantitySearchParamList READONLY, @DateTimeSearchParams dbo.DateTimeSearchParamList READONLY, @ReferenceTokenCompositeSearchParams dbo.ReferenceTokenCompositeSearchParamList READONLY, @TokenTokenCompositeSearchParams dbo.TokenTokenCompositeSearchParamList READONLY, @TokenDateTimeCompositeSearchParams dbo.TokenDateTimeCompositeSearchParamList READONLY, @TokenQuantityCompositeSearchParams dbo.TokenQuantityCompositeSearchParamList READONLY, @TokenStringCompositeSearchParams dbo.TokenStringCompositeSearchParamList READONLY, @TokenNumberNumberCompositeSearchParams dbo.TokenNumberNumberCompositeSearchParamList READONLY
AS
SET NOCOUNT ON;
DECLARE @st AS DATETIME = getUTCdate(), @SP AS VARCHAR (100) = object_name(@@procid), @Mode AS VARCHAR (200) = isnull((SELECT 'RT=[' + CONVERT (VARCHAR, min(ResourceTypeId)) + ',' + CONVERT (VARCHAR, max(ResourceTypeId)) + '] Sur=[' + CONVERT (VARCHAR, min(ResourceSurrogateId)) + ',' + CONVERT (VARCHAR, max(ResourceSurrogateId)) + '] V=' + CONVERT (VARCHAR, max(Version)) + ' Rows=' + CONVERT (VARCHAR, count(*))
                                                                                                                       FROM   @Resources), 'Input=Empty'), @Rows AS INT, @ReferenceSearchParamsCurrent AS dbo.ReferenceSearchParamList, @ReferenceSearchParamsDelete AS dbo.ReferenceSearchParamList, @ReferenceSearchParamsInsert AS dbo.ReferenceSearchParamList, @TokenSearchParamsCurrent AS dbo.TokenSearchParamList, @TokenSearchParamsDelete AS dbo.TokenSearchParamList, @TokenSearchParamsInsert AS dbo.TokenSearchParamList, @TokenTextsCurrent AS dbo.TokenTextList, @TokenTextsDelete AS dbo.TokenTextList, @TokenTextsInsert AS dbo.TokenTextList, @StringSearchParamsCurrent AS dbo.StringSearchParamList, @StringSearchParamsDelete AS dbo.StringSearchParamList, @StringSearchParamsInsert AS dbo.StringSearchParamList, @UriSearchParamsCurrent AS dbo.UriSearchParamList, @UriSearchParamsDelete AS dbo.UriSearchParamList, @UriSearchParamsInsert AS dbo.UriSearchParamList, @NumberSearchParamsCurrent AS dbo.NumberSearchParamList, @NumberSearchParamsDelete AS dbo.NumberSearchParamList, @NumberSearchParamsInsert AS dbo.NumberSearchParamList, @QuantitySearchParamsCurrent AS dbo.QuantitySearchParamList, @QuantitySearchParamsDelete AS dbo.QuantitySearchParamList, @QuantitySearchParamsInsert AS dbo.QuantitySearchParamList, @DateTimeSearchParamsCurrent AS dbo.DateTimeSearchParamList, @DateTimeSearchParamsDelete AS dbo.DateTimeSearchParamList, @DateTimeSearchParamsInsert AS dbo.DateTimeSearchParamList, @ReferenceTokenCompositeSearchParamsCurrent AS dbo.ReferenceTokenCompositeSearchParamList, @ReferenceTokenCompositeSearchParamsDelete AS dbo.ReferenceTokenCompositeSearchParamList, @ReferenceTokenCompositeSearchParamsInsert AS dbo.ReferenceTokenCompositeSearchParamList, @TokenTokenCompositeSearchParamsCurrent AS dbo.TokenTokenCompositeSearchParamList, @TokenTokenCompositeSearchParamsDelete AS dbo.TokenTokenCompositeSearchParamList, @TokenTokenCompositeSearchParamsInsert AS dbo.TokenTokenCompositeSearchParamList, @TokenDateTimeCompositeSearchParamsCurrent AS dbo.TokenDateTimeCompositeSearchParamList, @TokenDateTimeCompositeSearchParamsDelete AS dbo.TokenDateTimeCompositeSearchParamList, @TokenDateTimeCompositeSearchParamsInsert AS dbo.TokenDateTimeCompositeSearchParamList, @TokenQuantityCompositeSearchParamsCurrent AS dbo.TokenQuantityCompositeSearchParamList, @TokenQuantityCompositeSearchParamsDelete AS dbo.TokenQuantityCompositeSearchParamList, @TokenQuantityCompositeSearchParamsInsert AS dbo.TokenQuantityCompositeSearchParamList, @TokenStringCompositeSearchParamsCurrent AS dbo.TokenStringCompositeSearchParamList, @TokenStringCompositeSearchParamsDelete AS dbo.TokenStringCompositeSearchParamList, @TokenStringCompositeSearchParamsInsert AS dbo.TokenStringCompositeSearchParamList, @TokenNumberNumberCompositeSearchParamsCurrent AS dbo.TokenNumberNumberCompositeSearchParamList, @TokenNumberNumberCompositeSearchParamsDelete AS dbo.TokenNumberNumberCompositeSearchParamList, @TokenNumberNumberCompositeSearchParamsInsert AS dbo.TokenNumberNumberCompositeSearchParamList, @ResourceWriteClaimsCurrent AS dbo.ResourceWriteClaimList, @ResourceWriteClaimsDelete AS dbo.ResourceWriteClaimList, @ResourceWriteClaimsInsert AS dbo.ResourceWriteClaimList;
BEGIN TRY
    DECLARE @Ids TABLE (
        ResourceTypeId      SMALLINT NOT NULL,
        ResourceSurrogateId BIGINT   NOT NULL);
    BEGIN TRANSACTION;
    UPDATE B
    SET    SearchParamHash = A.SearchParamHash
    OUTPUT deleted.ResourceTypeId, deleted.ResourceSurrogateId INTO @Ids
    FROM   @Resources AS A
           INNER JOIN
           dbo.Resource AS B
           ON B.ResourceTypeId = A.ResourceTypeId
              AND B.ResourceSurrogateId = A.ResourceSurrogateId
    WHERE  B.IsHistory = 0;
    SET @Rows = @@rowcount;
    INSERT INTO @ResourceWriteClaimsCurrent (ResourceSurrogateId, ClaimTypeId, ClaimValue)
    SELECT A.ResourceSurrogateId,
           ClaimTypeId,
           ClaimValue
    FROM   dbo.ResourceWriteClaim AS A
           INNER JOIN
           @Ids AS B
           ON B.ResourceSurrogateId = A.ResourceSurrogateId;
    INSERT INTO @ResourceWriteClaimsDelete (ResourceSurrogateId, ClaimTypeId, ClaimValue)
    SELECT ResourceSurrogateId,
           ClaimTypeId,
           ClaimValue
    FROM   @ResourceWriteClaimsCurrent AS A
    WHERE  NOT EXISTS (SELECT *
                       FROM   @ResourceWriteClaims AS B
                       WHERE  B.ResourceSurrogateId = A.ResourceSurrogateId
                              AND B.ClaimTypeId = A.ClaimTypeId
                              AND B.ClaimValue = A.ClaimValue);
    DELETE A
    FROM   dbo.ResourceWriteClaim AS A
    WHERE  EXISTS (SELECT *
                   FROM   @ResourceWriteClaimsDelete AS B
                   WHERE  B.ResourceSurrogateId = A.ResourceSurrogateId
                          AND B.ClaimTypeId = A.ClaimTypeId
                          AND B.ClaimValue = A.ClaimValue);
    INSERT INTO @ResourceWriteClaimsInsert (ResourceSurrogateId, ClaimTypeId, ClaimValue)
    SELECT ResourceSurrogateId,
           ClaimTypeId,
           ClaimValue
    FROM   @ResourceWriteClaims AS A
    WHERE  NOT EXISTS (SELECT *
                       FROM   @ResourceWriteClaimsCurrent AS B
                       WHERE  B.ResourceSurrogateId = A.ResourceSurrogateId
                              AND B.ClaimTypeId = A.ClaimTypeId
                              AND B.ClaimValue = A.ClaimValue);
    INSERT INTO @ReferenceSearchParamsCurrent (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion)
    SELECT A.ResourceTypeId,
           A.ResourceSurrogateId,
           SearchParamId,
           BaseUri,
           ReferenceResourceTypeId,
           ReferenceResourceId,
           ReferenceResourceVersion
    FROM   dbo.ReferenceSearchParam AS A
           INNER JOIN
           @Ids AS B
           ON B.ResourceTypeId = A.ResourceTypeId
              AND B.ResourceSurrogateId = A.ResourceSurrogateId;
    INSERT INTO @ReferenceSearchParamsDelete (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion)
    SELECT ResourceTypeId,
           ResourceSurrogateId,
           SearchParamId,
           BaseUri,
           ReferenceResourceTypeId,
           ReferenceResourceId,
           ReferenceResourceVersion
    FROM   @ReferenceSearchParamsCurrent AS A
    WHERE  NOT EXISTS (SELECT *
                       FROM   @ReferenceSearchParams AS B
                       WHERE  B.ResourceTypeId = A.ResourceTypeId
                              AND B.ResourceSurrogateId = A.ResourceSurrogateId
                              AND B.SearchParamId = A.SearchParamId
                              AND (B.BaseUri = A.BaseUri
                                   OR B.BaseUri IS NULL
                                      AND A.BaseUri IS NULL)
                              AND (B.ReferenceResourceTypeId = A.ReferenceResourceTypeId
                                   OR B.ReferenceResourceTypeId IS NULL
                                      AND A.ReferenceResourceTypeId IS NULL)
                              AND B.ReferenceResourceId = A.ReferenceResourceId);
    DELETE A
    FROM   dbo.ReferenceSearchParam AS A WITH (INDEX (1))
    WHERE  EXISTS (SELECT *
                   FROM   @ReferenceSearchParamsDelete AS B
                   WHERE  B.ResourceTypeId = A.ResourceTypeId
                          AND B.ResourceSurrogateId = A.ResourceSurrogateId
                          AND B.SearchParamId = A.SearchParamId
                          AND (B.BaseUri = A.BaseUri
                               OR B.BaseUri IS NULL
                                  AND A.BaseUri IS NULL)
                          AND (B.ReferenceResourceTypeId = A.ReferenceResourceTypeId
                               OR B.ReferenceResourceTypeId IS NULL
                                  AND A.ReferenceResourceTypeId IS NULL)
                          AND B.ReferenceResourceId = A.ReferenceResourceId);
    INSERT INTO @ReferenceSearchParamsInsert (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion)
    SELECT ResourceTypeId,
           ResourceSurrogateId,
           SearchParamId,
           BaseUri,
           ReferenceResourceTypeId,
           ReferenceResourceId,
           ReferenceResourceVersion
    FROM   @ReferenceSearchParams AS A
    WHERE  NOT EXISTS (SELECT *
                       FROM   @ReferenceSearchParamsCurrent AS B
                       WHERE  B.ResourceTypeId = A.ResourceTypeId
                              AND B.ResourceSurrogateId = A.ResourceSurrogateId
                              AND B.SearchParamId = A.SearchParamId
                              AND (B.BaseUri = A.BaseUri
                                   OR B.BaseUri IS NULL
                                      AND A.BaseUri IS NULL)
                              AND (B.ReferenceResourceTypeId = A.ReferenceResourceTypeId
                                   OR B.ReferenceResourceTypeId IS NULL
                                      AND A.ReferenceResourceTypeId IS NULL)
                              AND B.ReferenceResourceId = A.ReferenceResourceId);
    INSERT INTO @TokenSearchParamsCurrent (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow)
    SELECT A.ResourceTypeId,
           A.ResourceSurrogateId,
           SearchParamId,
           SystemId,
           Code,
           CodeOverflow
    FROM   dbo.TokenSearchParam AS A
           INNER JOIN
           @Ids AS B
           ON B.ResourceTypeId = A.ResourceTypeId
              AND B.ResourceSurrogateId = A.ResourceSurrogateId;
    INSERT INTO @TokenSearchParamsDelete (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow)
    SELECT ResourceTypeId,
           ResourceSurrogateId,
           SearchParamId,
           SystemId,
           Code,
           CodeOverflow
    FROM   @TokenSearchParamsCurrent AS A
    WHERE  NOT EXISTS (SELECT *
                       FROM   @TokenSearchParams AS B
                       WHERE  B.ResourceTypeId = A.ResourceTypeId
                              AND B.ResourceSurrogateId = A.ResourceSurrogateId
                              AND B.SearchParamId = A.SearchParamId
                              AND (B.SystemId = A.SystemId
                                   OR B.SystemId IS NULL
                                      AND A.SystemId IS NULL)
                              AND B.Code = A.Code
                              AND (B.CodeOverflow = A.CodeOverflow
                                   OR B.CodeOverflow IS NULL
                                      AND A.CodeOverflow IS NULL));
    DELETE A
    FROM   dbo.TokenSearchParam AS A WITH (INDEX (1))
    WHERE  EXISTS (SELECT *
                   FROM   @TokenSearchParamsDelete AS B
                   WHERE  B.ResourceTypeId = A.ResourceTypeId
                          AND B.ResourceSurrogateId = A.ResourceSurrogateId
                          AND B.SearchParamId = A.SearchParamId
                          AND (B.SystemId = A.SystemId
                               OR B.SystemId IS NULL
                                  AND A.SystemId IS NULL)
                          AND B.Code = A.Code
                          AND (B.CodeOverflow = A.CodeOverflow
                               OR B.CodeOverflow IS NULL
                                  AND A.CodeOverflow IS NULL));
    INSERT INTO @TokenSearchParamsInsert (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow)
    SELECT ResourceTypeId,
           ResourceSurrogateId,
           SearchParamId,
           SystemId,
           Code,
           CodeOverflow
    FROM   @TokenSearchParams AS A
    WHERE  NOT EXISTS (SELECT *
                       FROM   @TokenSearchParamsCurrent AS B
                       WHERE  B.ResourceTypeId = A.ResourceTypeId
                              AND B.ResourceSurrogateId = A.ResourceSurrogateId
                              AND B.SearchParamId = A.SearchParamId
                              AND (B.SystemId = A.SystemId
                                   OR B.SystemId IS NULL
                                      AND A.SystemId IS NULL)
                              AND B.Code = A.Code
                              AND (B.CodeOverflow = A.CodeOverflow
                                   OR B.CodeOverflow IS NULL
                                      AND A.CodeOverflow IS NULL));
    INSERT INTO @TokenStringCompositeSearchParamsCurrent (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, Text2, TextOverflow2)
    SELECT A.ResourceTypeId,
           A.ResourceSurrogateId,
           SearchParamId,
           SystemId1,
           Code1,
           CodeOverflow1,
           Text2,
           TextOverflow2
    FROM   dbo.TokenStringCompositeSearchParam AS A
           INNER JOIN
           @Ids AS B
           ON B.ResourceTypeId = A.ResourceTypeId
              AND B.ResourceSurrogateId = A.ResourceSurrogateId;
    INSERT INTO @TokenStringCompositeSearchParamsDelete (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, Text2, TextOverflow2)
    SELECT ResourceTypeId,
           ResourceSurrogateId,
           SearchParamId,
           SystemId1,
           Code1,
           CodeOverflow1,
           Text2,
           TextOverflow2
    FROM   @TokenStringCompositeSearchParamsCurrent AS A
    WHERE  NOT EXISTS (SELECT *
                       FROM   @TokenStringCompositeSearchParams AS B
                       WHERE  B.ResourceTypeId = A.ResourceTypeId
                              AND B.ResourceSurrogateId = A.ResourceSurrogateId
                              AND B.SearchParamId = A.SearchParamId
                              AND (B.SystemId1 = A.SystemId1
                                   OR B.SystemId1 IS NULL
                                      AND A.SystemId1 IS NULL)
                              AND B.Code1 = A.Code1
                              AND (B.CodeOverflow1 = A.CodeOverflow1
                                   OR B.CodeOverflow1 IS NULL
                                      AND A.CodeOverflow1 IS NULL)
                              AND B.Text2 = A.Text2
                              AND (B.TextOverflow2 = A.TextOverflow2
                                   OR B.TextOverflow2 IS NULL
                                      AND A.TextOverflow2 IS NULL));
    DELETE A
    FROM   dbo.TokenStringCompositeSearchParam AS A WITH (INDEX (1))
    WHERE  EXISTS (SELECT *
                   FROM   @TokenStringCompositeSearchParamsDelete AS B
                   WHERE  B.ResourceTypeId = A.ResourceTypeId
                          AND B.ResourceSurrogateId = A.ResourceSurrogateId
                          AND B.SearchParamId = A.SearchParamId
                          AND (B.SystemId1 = A.SystemId1
                               OR B.SystemId1 IS NULL
                                  AND A.SystemId1 IS NULL)
                          AND B.Code1 = A.Code1
                          AND (B.CodeOverflow1 = A.CodeOverflow1
                               OR B.CodeOverflow1 IS NULL
                                  AND A.CodeOverflow1 IS NULL)
                          AND B.Text2 COLLATE Latin1_General_100_CI_AI_SC = A.Text2
                          AND (B.TextOverflow2 COLLATE Latin1_General_100_CI_AI_SC = A.TextOverflow2
                               OR B.TextOverflow2 IS NULL
                                  AND A.TextOverflow2 IS NULL));
    INSERT INTO @TokenStringCompositeSearchParamsInsert (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, Text2, TextOverflow2)
    SELECT ResourceTypeId,
           ResourceSurrogateId,
           SearchParamId,
           SystemId1,
           Code1,
           CodeOverflow1,
           Text2,
           TextOverflow2
    FROM   @TokenStringCompositeSearchParams AS A
    WHERE  NOT EXISTS (SELECT *
                       FROM   @TokenStringCompositeSearchParamsCurrent AS B
                       WHERE  B.ResourceTypeId = A.ResourceTypeId
                              AND B.ResourceSurrogateId = A.ResourceSurrogateId
                              AND B.SearchParamId = A.SearchParamId
                              AND (B.SystemId1 = A.SystemId1
                                   OR B.SystemId1 IS NULL
                                      AND A.SystemId1 IS NULL)
                              AND B.Code1 = A.Code1
                              AND (B.CodeOverflow1 = A.CodeOverflow1
                                   OR B.CodeOverflow1 IS NULL
                                      AND A.CodeOverflow1 IS NULL)
                              AND B.Text2 = A.Text2
                              AND (B.TextOverflow2 = A.TextOverflow2
                                   OR B.TextOverflow2 IS NULL
                                      AND A.TextOverflow2 IS NULL));
    INSERT INTO @TokenTextsCurrent (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text)
    SELECT A.ResourceTypeId,
           A.ResourceSurrogateId,
           SearchParamId,
           Text
    FROM   dbo.TokenText AS A
           INNER JOIN
           @Ids AS B
           ON B.ResourceTypeId = A.ResourceTypeId
              AND B.ResourceSurrogateId = A.ResourceSurrogateId;
    INSERT INTO @TokenTextsDelete (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text)
    SELECT ResourceTypeId,
           ResourceSurrogateId,
           SearchParamId,
           Text
    FROM   @TokenTextsCurrent AS A
    WHERE  NOT EXISTS (SELECT *
                       FROM   @TokenTexts AS B
                       WHERE  B.ResourceTypeId = A.ResourceTypeId
                              AND B.ResourceSurrogateId = A.ResourceSurrogateId
                              AND B.SearchParamId = A.SearchParamId
                              AND B.Text = A.Text);
    DELETE A
    FROM   dbo.TokenText AS A WITH (INDEX (1))
    WHERE  EXISTS (SELECT *
                   FROM   @TokenTextsDelete AS B
                   WHERE  B.ResourceTypeId = A.ResourceTypeId
                          AND B.ResourceSurrogateId = A.ResourceSurrogateId
                          AND B.SearchParamId = A.SearchParamId
                          AND B.Text = A.Text);
    INSERT INTO @TokenTextsInsert (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text)
    SELECT ResourceTypeId,
           ResourceSurrogateId,
           SearchParamId,
           Text
    FROM   @TokenTexts AS A
    WHERE  NOT EXISTS (SELECT *
                       FROM   @TokenTextsCurrent AS B
                       WHERE  B.ResourceTypeId = A.ResourceTypeId
                              AND B.ResourceSurrogateId = A.ResourceSurrogateId
                              AND B.SearchParamId = A.SearchParamId
                              AND B.Text = A.Text);
    INSERT INTO @StringSearchParamsCurrent (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsMin, IsMax)
    SELECT A.ResourceTypeId,
           A.ResourceSurrogateId,
           SearchParamId,
           Text,
           TextOverflow,
           IsMin,
           IsMax
    FROM   dbo.StringSearchParam AS A
           INNER JOIN
           @Ids AS B
           ON B.ResourceTypeId = A.ResourceTypeId
              AND B.ResourceSurrogateId = A.ResourceSurrogateId;
    INSERT INTO @StringSearchParamsDelete (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsMin, IsMax)
    SELECT ResourceTypeId,
           ResourceSurrogateId,
           SearchParamId,
           Text,
           TextOverflow,
           IsMin,
           IsMax
    FROM   @StringSearchParamsCurrent AS A
    WHERE  NOT EXISTS (SELECT *
                       FROM   @StringSearchParams AS B
                       WHERE  B.ResourceTypeId = A.ResourceTypeId
                              AND B.ResourceSurrogateId = A.ResourceSurrogateId
                              AND B.SearchParamId = A.SearchParamId
                              AND B.Text = A.Text
                              AND (B.TextOverflow = A.TextOverflow
                                   OR B.TextOverflow IS NULL
                                      AND A.TextOverflow IS NULL)
                              AND B.IsMin = A.IsMin
                              AND B.IsMax = A.IsMax);
    DELETE A
    FROM   dbo.StringSearchParam AS A WITH (INDEX (1))
    WHERE  EXISTS (SELECT *
                   FROM   @StringSearchParamsDelete AS B
                   WHERE  B.ResourceTypeId = A.ResourceTypeId
                          AND B.ResourceSurrogateId = A.ResourceSurrogateId
                          AND B.SearchParamId = A.SearchParamId
                          AND B.Text = A.Text
                          AND (B.TextOverflow = A.TextOverflow
                               OR B.TextOverflow IS NULL
                                  AND A.TextOverflow IS NULL)
                          AND B.IsMin = A.IsMin
                          AND B.IsMax = A.IsMax);
    INSERT INTO @StringSearchParamsInsert (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsMin, IsMax)
    SELECT ResourceTypeId,
           ResourceSurrogateId,
           SearchParamId,
           Text,
           TextOverflow,
           IsMin,
           IsMax
    FROM   @StringSearchParams AS A
    WHERE  NOT EXISTS (SELECT *
                       FROM   @StringSearchParamsCurrent AS B
                       WHERE  B.ResourceTypeId = A.ResourceTypeId
                              AND B.ResourceSurrogateId = A.ResourceSurrogateId
                              AND B.SearchParamId = A.SearchParamId
                              AND B.Text = A.Text
                              AND (B.TextOverflow = A.TextOverflow
                                   OR B.TextOverflow IS NULL
                                      AND A.TextOverflow IS NULL)
                              AND B.IsMin = A.IsMin
                              AND B.IsMax = A.IsMax);
    INSERT INTO @UriSearchParamsCurrent (ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri)
    SELECT A.ResourceTypeId,
           A.ResourceSurrogateId,
           SearchParamId,
           Uri
    FROM   dbo.UriSearchParam AS A
           INNER JOIN
           @Ids AS B
           ON B.ResourceTypeId = A.ResourceTypeId
              AND B.ResourceSurrogateId = A.ResourceSurrogateId;
    INSERT INTO @UriSearchParamsDelete (ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri)
    SELECT ResourceTypeId,
           ResourceSurrogateId,
           SearchParamId,
           Uri
    FROM   @UriSearchParamsCurrent AS A
    WHERE  NOT EXISTS (SELECT *
                       FROM   @UriSearchParams AS B
                       WHERE  B.ResourceTypeId = A.ResourceTypeId
                              AND B.ResourceSurrogateId = A.ResourceSurrogateId
                              AND B.SearchParamId = A.SearchParamId
                              AND B.Uri = A.Uri);
    DELETE A
    FROM   dbo.UriSearchParam AS A WITH (INDEX (1))
    WHERE  EXISTS (SELECT *
                   FROM   @UriSearchParamsDelete AS B
                   WHERE  B.ResourceTypeId = A.ResourceTypeId
                          AND B.ResourceSurrogateId = A.ResourceSurrogateId
                          AND B.SearchParamId = A.SearchParamId
                          AND B.Uri = A.Uri);
    INSERT INTO @UriSearchParamsInsert (ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri)
    SELECT ResourceTypeId,
           ResourceSurrogateId,
           SearchParamId,
           Uri
    FROM   @UriSearchParams AS A
    WHERE  NOT EXISTS (SELECT *
                       FROM   @UriSearchParamsCurrent AS B
                       WHERE  B.ResourceTypeId = A.ResourceTypeId
                              AND B.ResourceSurrogateId = A.ResourceSurrogateId
                              AND B.SearchParamId = A.SearchParamId
                              AND B.Uri = A.Uri);
    INSERT INTO @NumberSearchParamsCurrent (ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue)
    SELECT A.ResourceTypeId,
           A.ResourceSurrogateId,
           SearchParamId,
           SingleValue,
           LowValue,
           HighValue
    FROM   dbo.NumberSearchParam AS A
           INNER JOIN
           @Ids AS B
           ON B.ResourceTypeId = A.ResourceTypeId
              AND B.ResourceSurrogateId = A.ResourceSurrogateId;
    INSERT INTO @NumberSearchParamsDelete (ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue)
    SELECT ResourceTypeId,
           ResourceSurrogateId,
           SearchParamId,
           SingleValue,
           LowValue,
           HighValue
    FROM   @NumberSearchParamsCurrent AS A
    WHERE  NOT EXISTS (SELECT *
                       FROM   @NumberSearchParams AS B
                       WHERE  B.ResourceTypeId = A.ResourceTypeId
                              AND B.ResourceSurrogateId = A.ResourceSurrogateId
                              AND B.SearchParamId = A.SearchParamId
                              AND (B.SingleValue = A.SingleValue
                                   OR B.SingleValue IS NULL
                                      AND A.SingleValue IS NULL)
                              AND (B.LowValue = A.LowValue
                                   OR B.LowValue IS NULL
                                      AND A.LowValue IS NULL)
                              AND (B.HighValue = A.HighValue
                                   OR B.HighValue IS NULL
                                      AND A.HighValue IS NULL));
    DELETE A
    FROM   dbo.NumberSearchParam AS A WITH (INDEX (1))
    WHERE  EXISTS (SELECT *
                   FROM   @NumberSearchParamsDelete AS B
                   WHERE  B.ResourceTypeId = A.ResourceTypeId
                          AND B.ResourceSurrogateId = A.ResourceSurrogateId
                          AND B.SearchParamId = A.SearchParamId
                          AND (B.SingleValue = A.SingleValue
                               OR B.SingleValue IS NULL
                                  AND A.SingleValue IS NULL)
                          AND (B.LowValue = A.LowValue
                               OR B.LowValue IS NULL
                                  AND A.LowValue IS NULL)
                          AND (B.HighValue = A.HighValue
                               OR B.HighValue IS NULL
                                  AND A.HighValue IS NULL));
    INSERT INTO @NumberSearchParamsInsert (ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue)
    SELECT ResourceTypeId,
           ResourceSurrogateId,
           SearchParamId,
           SingleValue,
           LowValue,
           HighValue
    FROM   @NumberSearchParams AS A
    WHERE  NOT EXISTS (SELECT *
                       FROM   @NumberSearchParamsCurrent AS B
                       WHERE  B.ResourceTypeId = A.ResourceTypeId
                              AND B.ResourceSurrogateId = A.ResourceSurrogateId
                              AND B.SearchParamId = A.SearchParamId
                              AND (B.SingleValue = A.SingleValue
                                   OR B.SingleValue IS NULL
                                      AND A.SingleValue IS NULL)
                              AND (B.LowValue = A.LowValue
                                   OR B.LowValue IS NULL
                                      AND A.LowValue IS NULL)
                              AND (B.HighValue = A.HighValue
                                   OR B.HighValue IS NULL
                                      AND A.HighValue IS NULL));
    INSERT INTO @QuantitySearchParamsCurrent (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue)
    SELECT A.ResourceTypeId,
           A.ResourceSurrogateId,
           SearchParamId,
           SystemId,
           QuantityCodeId,
           SingleValue,
           LowValue,
           HighValue
    FROM   dbo.QuantitySearchParam AS A
           INNER JOIN
           @Ids AS B
           ON B.ResourceTypeId = A.ResourceTypeId
              AND B.ResourceSurrogateId = A.ResourceSurrogateId;
    INSERT INTO @QuantitySearchParamsDelete (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue)
    SELECT ResourceTypeId,
           ResourceSurrogateId,
           SearchParamId,
           SystemId,
           QuantityCodeId,
           SingleValue,
           LowValue,
           HighValue
    FROM   @QuantitySearchParamsCurrent AS A
    WHERE  NOT EXISTS (SELECT *
                       FROM   @QuantitySearchParams AS B
                       WHERE  B.ResourceTypeId = A.ResourceTypeId
                              AND B.ResourceSurrogateId = A.ResourceSurrogateId
                              AND B.SearchParamId = A.SearchParamId
                              AND (B.SystemId = A.SystemId
                                   OR B.SystemId IS NULL
                                      AND A.SystemId IS NULL)
                              AND (B.QuantityCodeId = A.QuantityCodeId
                                   OR B.QuantityCodeId IS NULL
                                      AND A.QuantityCodeId IS NULL)
                              AND (B.SingleValue = A.SingleValue
                                   OR B.SingleValue IS NULL
                                      AND A.SingleValue IS NULL)
                              AND (B.LowValue = A.LowValue
                                   OR B.LowValue IS NULL
                                      AND A.LowValue IS NULL)
                              AND (B.HighValue = A.HighValue
                                   OR B.HighValue IS NULL
                                      AND A.HighValue IS NULL));
    DELETE A
    FROM   dbo.QuantitySearchParam AS A WITH (INDEX (1))
    WHERE  EXISTS (SELECT *
                   FROM   @QuantitySearchParamsDelete AS B
                   WHERE  B.ResourceTypeId = A.ResourceTypeId
                          AND B.ResourceSurrogateId = A.ResourceSurrogateId
                          AND B.SearchParamId = A.SearchParamId
                          AND (B.SystemId = A.SystemId
                               OR B.SystemId IS NULL
                                  AND A.SystemId IS NULL)
                          AND (B.QuantityCodeId = A.QuantityCodeId
                               OR B.QuantityCodeId IS NULL
                                  AND A.QuantityCodeId IS NULL)
                          AND (B.SingleValue = A.SingleValue
                               OR B.SingleValue IS NULL
                                  AND A.SingleValue IS NULL)
                          AND (B.LowValue = A.LowValue
                               OR B.LowValue IS NULL
                                  AND A.LowValue IS NULL)
                          AND (B.HighValue = A.HighValue
                               OR B.HighValue IS NULL
                                  AND A.HighValue IS NULL));
    INSERT INTO @QuantitySearchParamsInsert (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue)
    SELECT ResourceTypeId,
           ResourceSurrogateId,
           SearchParamId,
           SystemId,
           QuantityCodeId,
           SingleValue,
           LowValue,
           HighValue
    FROM   @QuantitySearchParams AS A
    WHERE  NOT EXISTS (SELECT *
                       FROM   @QuantitySearchParamsCurrent AS B
                       WHERE  B.ResourceTypeId = A.ResourceTypeId
                              AND B.ResourceSurrogateId = A.ResourceSurrogateId
                              AND B.SearchParamId = A.SearchParamId
                              AND (B.SystemId = A.SystemId
                                   OR B.SystemId IS NULL
                                      AND A.SystemId IS NULL)
                              AND (B.QuantityCodeId = A.QuantityCodeId
                                   OR B.QuantityCodeId IS NULL
                                      AND A.QuantityCodeId IS NULL)
                              AND (B.SingleValue = A.SingleValue
                                   OR B.SingleValue IS NULL
                                      AND A.SingleValue IS NULL)
                              AND (B.LowValue = A.LowValue
                                   OR B.LowValue IS NULL
                                      AND A.LowValue IS NULL)
                              AND (B.HighValue = A.HighValue
                                   OR B.HighValue IS NULL
                                      AND A.HighValue IS NULL));
    INSERT INTO @DateTimeSearchParamsCurrent (ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsMin, IsMax)
    SELECT A.ResourceTypeId,
           A.ResourceSurrogateId,
           SearchParamId,
           StartDateTime,
           EndDateTime,
           IsLongerThanADay,
           IsMin,
           IsMax
    FROM   dbo.DateTimeSearchParam AS A
           INNER JOIN
           @Ids AS B
           ON B.ResourceTypeId = A.ResourceTypeId
              AND B.ResourceSurrogateId = A.ResourceSurrogateId;
    INSERT INTO @DateTimeSearchParamsDelete (ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsMin, IsMax)
    SELECT ResourceTypeId,
           ResourceSurrogateId,
           SearchParamId,
           StartDateTime,
           EndDateTime,
           IsLongerThanADay,
           IsMin,
           IsMax
    FROM   @DateTimeSearchParamsCurrent AS A
    WHERE  NOT EXISTS (SELECT *
                       FROM   @DateTimeSearchParams AS B
                       WHERE  B.ResourceTypeId = A.ResourceTypeId
                              AND B.ResourceSurrogateId = A.ResourceSurrogateId
                              AND B.SearchParamId = A.SearchParamId
                              AND B.StartDateTime = A.StartDateTime
                              AND B.EndDateTime = A.EndDateTime
                              AND B.IsLongerThanADay = A.IsLongerThanADay
                              AND B.IsMin = A.IsMin
                              AND B.IsMax = A.IsMax);
    DELETE A
    FROM   dbo.DateTimeSearchParam AS A WITH (INDEX (1))
    WHERE  EXISTS (SELECT *
                   FROM   @DateTimeSearchParamsDelete AS B
                   WHERE  B.ResourceTypeId = A.ResourceTypeId
                          AND B.ResourceSurrogateId = A.ResourceSurrogateId
                          AND B.SearchParamId = A.SearchParamId
                          AND B.StartDateTime = A.StartDateTime
                          AND B.EndDateTime = A.EndDateTime
                          AND B.IsLongerThanADay = A.IsLongerThanADay
                          AND B.IsMin = A.IsMin
                          AND B.IsMax = A.IsMax);
    INSERT INTO @DateTimeSearchParamsInsert (ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsMin, IsMax)
    SELECT ResourceTypeId,
           ResourceSurrogateId,
           SearchParamId,
           StartDateTime,
           EndDateTime,
           IsLongerThanADay,
           IsMin,
           IsMax
    FROM   @DateTimeSearchParams AS A
    WHERE  NOT EXISTS (SELECT *
                       FROM   @DateTimeSearchParamsCurrent AS B
                       WHERE  B.ResourceTypeId = A.ResourceTypeId
                              AND B.ResourceSurrogateId = A.ResourceSurrogateId
                              AND B.SearchParamId = A.SearchParamId
                              AND B.StartDateTime = A.StartDateTime
                              AND B.EndDateTime = A.EndDateTime
                              AND B.IsLongerThanADay = A.IsLongerThanADay
                              AND B.IsMin = A.IsMin
                              AND B.IsMax = A.IsMax);
    INSERT INTO @ReferenceTokenCompositeSearchParamsCurrent (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, CodeOverflow2)
    SELECT A.ResourceTypeId,
           A.ResourceSurrogateId,
           SearchParamId,
           BaseUri1,
           ReferenceResourceTypeId1,
           ReferenceResourceId1,
           ReferenceResourceVersion1,
           SystemId2,
           Code2,
           CodeOverflow2
    FROM   dbo.ReferenceTokenCompositeSearchParam AS A
           INNER JOIN
           @Ids AS B
           ON B.ResourceTypeId = A.ResourceTypeId
              AND B.ResourceSurrogateId = A.ResourceSurrogateId;
    INSERT INTO @ReferenceTokenCompositeSearchParamsDelete (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, CodeOverflow2)
    SELECT ResourceTypeId,
           ResourceSurrogateId,
           SearchParamId,
           BaseUri1,
           ReferenceResourceTypeId1,
           ReferenceResourceId1,
           ReferenceResourceVersion1,
           SystemId2,
           Code2,
           CodeOverflow2
    FROM   @ReferenceTokenCompositeSearchParamsCurrent AS A
    WHERE  NOT EXISTS (SELECT *
                       FROM   @ReferenceTokenCompositeSearchParams AS B
                       WHERE  B.ResourceTypeId = A.ResourceTypeId
                              AND B.ResourceSurrogateId = A.ResourceSurrogateId
                              AND B.SearchParamId = A.SearchParamId
                              AND (B.BaseUri1 = A.BaseUri1
                                   OR B.BaseUri1 IS NULL
                                      AND A.BaseUri1 IS NULL)
                              AND (B.ReferenceResourceTypeId1 = A.ReferenceResourceTypeId1
                                   OR B.ReferenceResourceTypeId1 IS NULL
                                      AND A.ReferenceResourceTypeId1 IS NULL)
                              AND B.ReferenceResourceId1 = A.ReferenceResourceId1
                              AND (B.SystemId2 = A.SystemId2
                                   OR B.SystemId2 IS NULL
                                      AND A.SystemId2 IS NULL)
                              AND B.Code2 = A.Code2
                              AND (B.CodeOverflow2 = A.CodeOverflow2
                                   OR B.CodeOverflow2 IS NULL
                                      AND A.CodeOverflow2 IS NULL));
    DELETE A
    FROM   dbo.ReferenceTokenCompositeSearchParam AS A WITH (INDEX (1))
    WHERE  EXISTS (SELECT *
                   FROM   @ReferenceTokenCompositeSearchParamsDelete AS B
                   WHERE  B.ResourceTypeId = A.ResourceTypeId
                          AND B.ResourceSurrogateId = A.ResourceSurrogateId
                          AND B.SearchParamId = A.SearchParamId
                          AND (B.BaseUri1 = A.BaseUri1
                               OR B.BaseUri1 IS NULL
                                  AND A.BaseUri1 IS NULL)
                          AND (B.ReferenceResourceTypeId1 = A.ReferenceResourceTypeId1
                               OR B.ReferenceResourceTypeId1 IS NULL
                                  AND A.ReferenceResourceTypeId1 IS NULL)
                          AND B.ReferenceResourceId1 = A.ReferenceResourceId1
                          AND (B.SystemId2 = A.SystemId2
                               OR B.SystemId2 IS NULL
                                  AND A.SystemId2 IS NULL)
                          AND B.Code2 = A.Code2
                          AND (B.CodeOverflow2 = A.CodeOverflow2
                               OR B.CodeOverflow2 IS NULL
                                  AND A.CodeOverflow2 IS NULL));
    INSERT INTO @ReferenceTokenCompositeSearchParamsInsert (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, CodeOverflow2)
    SELECT ResourceTypeId,
           ResourceSurrogateId,
           SearchParamId,
           BaseUri1,
           ReferenceResourceTypeId1,
           ReferenceResourceId1,
           ReferenceResourceVersion1,
           SystemId2,
           Code2,
           CodeOverflow2
    FROM   @ReferenceTokenCompositeSearchParams AS A
    WHERE  NOT EXISTS (SELECT *
                       FROM   @ReferenceTokenCompositeSearchParamsCurrent AS B
                       WHERE  B.ResourceTypeId = A.ResourceTypeId
                              AND B.ResourceSurrogateId = A.ResourceSurrogateId
                              AND B.SearchParamId = A.SearchParamId
                              AND (B.BaseUri1 = A.BaseUri1
                                   OR B.BaseUri1 IS NULL
                                      AND A.BaseUri1 IS NULL)
                              AND (B.ReferenceResourceTypeId1 = A.ReferenceResourceTypeId1
                                   OR B.ReferenceResourceTypeId1 IS NULL
                                      AND A.ReferenceResourceTypeId1 IS NULL)
                              AND B.ReferenceResourceId1 = A.ReferenceResourceId1
                              AND (B.SystemId2 = A.SystemId2
                                   OR B.SystemId2 IS NULL
                                      AND A.SystemId2 IS NULL)
                              AND B.Code2 = A.Code2
                              AND (B.CodeOverflow2 = A.CodeOverflow2
                                   OR B.CodeOverflow2 IS NULL
                                      AND A.CodeOverflow2 IS NULL));
    INSERT INTO @TokenTokenCompositeSearchParamsCurrent (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SystemId2, Code2, CodeOverflow2)
    SELECT A.ResourceTypeId,
           A.ResourceSurrogateId,
           SearchParamId,
           SystemId1,
           Code1,
           CodeOverflow1,
           SystemId2,
           Code2,
           CodeOverflow2
    FROM   dbo.TokenTokenCompositeSearchParam AS A
           INNER JOIN
           @Ids AS B
           ON B.ResourceTypeId = A.ResourceTypeId
              AND B.ResourceSurrogateId = A.ResourceSurrogateId;
    INSERT INTO @TokenTokenCompositeSearchParamsDelete (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SystemId2, Code2, CodeOverflow2)
    SELECT ResourceTypeId,
           ResourceSurrogateId,
           SearchParamId,
           SystemId1,
           Code1,
           CodeOverflow1,
           SystemId2,
           Code2,
           CodeOverflow2
    FROM   @TokenTokenCompositeSearchParamsCurrent AS A
    WHERE  NOT EXISTS (SELECT *
                       FROM   @TokenTokenCompositeSearchParams AS B
                       WHERE  B.ResourceTypeId = A.ResourceTypeId
                              AND B.ResourceSurrogateId = A.ResourceSurrogateId
                              AND B.SearchParamId = A.SearchParamId
                              AND (B.SystemId1 = A.SystemId1
                                   OR B.SystemId1 IS NULL
                                      AND A.SystemId1 IS NULL)
                              AND B.Code1 = A.Code1
                              AND (B.CodeOverflow1 = A.CodeOverflow1
                                   OR B.CodeOverflow1 IS NULL
                                      AND A.CodeOverflow1 IS NULL)
                              AND (B.SystemId2 = A.SystemId2
                                   OR B.SystemId2 IS NULL
                                      AND A.SystemId2 IS NULL)
                              AND B.Code2 = A.Code2
                              AND (B.CodeOverflow2 = A.CodeOverflow2
                                   OR B.CodeOverflow2 IS NULL
                                      AND A.CodeOverflow2 IS NULL));
    DELETE A
    FROM   dbo.TokenTokenCompositeSearchParam AS A WITH (INDEX (1))
    WHERE  EXISTS (SELECT *
                   FROM   @TokenTokenCompositeSearchParamsDelete AS B
                   WHERE  B.ResourceTypeId = A.ResourceTypeId
                          AND B.ResourceSurrogateId = A.ResourceSurrogateId
                          AND B.SearchParamId = A.SearchParamId
                          AND (B.SystemId1 = A.SystemId1
                               OR B.SystemId1 IS NULL
                                  AND A.SystemId1 IS NULL)
                          AND B.Code1 = A.Code1
                          AND (B.CodeOverflow1 = A.CodeOverflow1
                               OR B.CodeOverflow1 IS NULL
                                  AND A.CodeOverflow1 IS NULL)
                          AND (B.SystemId2 = A.SystemId2
                               OR B.SystemId2 IS NULL
                                  AND A.SystemId2 IS NULL)
                          AND B.Code2 = A.Code2
                          AND (B.CodeOverflow2 = A.CodeOverflow2
                               OR B.CodeOverflow2 IS NULL
                                  AND A.CodeOverflow2 IS NULL));
    INSERT INTO @TokenTokenCompositeSearchParamsInsert (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SystemId2, Code2, CodeOverflow2)
    SELECT ResourceTypeId,
           ResourceSurrogateId,
           SearchParamId,
           SystemId1,
           Code1,
           CodeOverflow1,
           SystemId2,
           Code2,
           CodeOverflow2
    FROM   @TokenTokenCompositeSearchParams AS A
    WHERE  NOT EXISTS (SELECT *
                       FROM   @TokenTokenCompositeSearchParamsCurrent AS B
                       WHERE  B.ResourceTypeId = A.ResourceTypeId
                              AND B.ResourceSurrogateId = A.ResourceSurrogateId
                              AND B.SearchParamId = A.SearchParamId
                              AND (B.SystemId1 = A.SystemId1
                                   OR B.SystemId1 IS NULL
                                      AND A.SystemId1 IS NULL)
                              AND B.Code1 = A.Code1
                              AND (B.CodeOverflow1 = A.CodeOverflow1
                                   OR B.CodeOverflow1 IS NULL
                                      AND A.CodeOverflow1 IS NULL)
                              AND (B.SystemId2 = A.SystemId2
                                   OR B.SystemId2 IS NULL
                                      AND A.SystemId2 IS NULL)
                              AND B.Code2 = A.Code2
                              AND (B.CodeOverflow2 = A.CodeOverflow2
                                   OR B.CodeOverflow2 IS NULL
                                      AND A.CodeOverflow2 IS NULL));
    INSERT INTO @TokenDateTimeCompositeSearchParamsCurrent (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, StartDateTime2, EndDateTime2, IsLongerThanADay2)
    SELECT A.ResourceTypeId,
           A.ResourceSurrogateId,
           SearchParamId,
           SystemId1,
           Code1,
           CodeOverflow1,
           StartDateTime2,
           EndDateTime2,
           IsLongerThanADay2
    FROM   dbo.TokenDateTimeCompositeSearchParam AS A
           INNER JOIN
           @Ids AS B
           ON B.ResourceTypeId = A.ResourceTypeId
              AND B.ResourceSurrogateId = A.ResourceSurrogateId;
    INSERT INTO @TokenDateTimeCompositeSearchParamsDelete (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, StartDateTime2, EndDateTime2, IsLongerThanADay2)
    SELECT ResourceTypeId,
           ResourceSurrogateId,
           SearchParamId,
           SystemId1,
           Code1,
           CodeOverflow1,
           StartDateTime2,
           EndDateTime2,
           IsLongerThanADay2
    FROM   @TokenDateTimeCompositeSearchParamsCurrent AS A
    WHERE  NOT EXISTS (SELECT *
                       FROM   @TokenDateTimeCompositeSearchParams AS B
                       WHERE  B.ResourceTypeId = A.ResourceTypeId
                              AND B.ResourceSurrogateId = A.ResourceSurrogateId
                              AND B.SearchParamId = A.SearchParamId
                              AND (B.SystemId1 = A.SystemId1
                                   OR B.SystemId1 IS NULL
                                      AND A.SystemId1 IS NULL)
                              AND B.Code1 = A.Code1
                              AND (B.CodeOverflow1 = A.CodeOverflow1
                                   OR B.CodeOverflow1 IS NULL
                                      AND A.CodeOverflow1 IS NULL)
                              AND B.StartDateTime2 = A.StartDateTime2
                              AND B.EndDateTime2 = A.EndDateTime2
                              AND B.IsLongerThanADay2 = A.IsLongerThanADay2);
    DELETE A
    FROM   dbo.TokenDateTimeCompositeSearchParam AS A WITH (INDEX (1))
    WHERE  EXISTS (SELECT *
                   FROM   @TokenDateTimeCompositeSearchParamsDelete AS B
                   WHERE  B.ResourceTypeId = A.ResourceTypeId
                          AND B.ResourceSurrogateId = A.ResourceSurrogateId
                          AND B.SearchParamId = A.SearchParamId
                          AND (B.SystemId1 = A.SystemId1
                               OR B.SystemId1 IS NULL
                                  AND A.SystemId1 IS NULL)
                          AND B.Code1 = A.Code1
                          AND (B.CodeOverflow1 = A.CodeOverflow1
                               OR B.CodeOverflow1 IS NULL
                                  AND A.CodeOverflow1 IS NULL)
                          AND B.StartDateTime2 = A.StartDateTime2
                          AND B.EndDateTime2 = A.EndDateTime2
                          AND B.IsLongerThanADay2 = A.IsLongerThanADay2);
    INSERT INTO @TokenDateTimeCompositeSearchParamsInsert (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, StartDateTime2, EndDateTime2, IsLongerThanADay2)
    SELECT ResourceTypeId,
           ResourceSurrogateId,
           SearchParamId,
           SystemId1,
           Code1,
           CodeOverflow1,
           StartDateTime2,
           EndDateTime2,
           IsLongerThanADay2
    FROM   @TokenDateTimeCompositeSearchParams AS A
    WHERE  NOT EXISTS (SELECT *
                       FROM   @TokenDateTimeCompositeSearchParamsCurrent AS B
                       WHERE  B.ResourceTypeId = A.ResourceTypeId
                              AND B.ResourceSurrogateId = A.ResourceSurrogateId
                              AND B.SearchParamId = A.SearchParamId
                              AND (B.SystemId1 = A.SystemId1
                                   OR B.SystemId1 IS NULL
                                      AND A.SystemId1 IS NULL)
                              AND B.Code1 = A.Code1
                              AND (B.CodeOverflow1 = A.CodeOverflow1
                                   OR B.CodeOverflow1 IS NULL
                                      AND A.CodeOverflow1 IS NULL)
                              AND B.StartDateTime2 = A.StartDateTime2
                              AND B.EndDateTime2 = A.EndDateTime2
                              AND B.IsLongerThanADay2 = A.IsLongerThanADay2);
    INSERT INTO @TokenQuantityCompositeSearchParamsCurrent (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2)
    SELECT A.ResourceTypeId,
           A.ResourceSurrogateId,
           SearchParamId,
           SystemId1,
           Code1,
           CodeOverflow1,
           SingleValue2,
           SystemId2,
           QuantityCodeId2,
           LowValue2,
           HighValue2
    FROM   dbo.TokenQuantityCompositeSearchParam AS A
           INNER JOIN
           @Ids AS B
           ON B.ResourceTypeId = A.ResourceTypeId
              AND B.ResourceSurrogateId = A.ResourceSurrogateId;
    INSERT INTO @TokenQuantityCompositeSearchParamsDelete (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2)
    SELECT ResourceTypeId,
           ResourceSurrogateId,
           SearchParamId,
           SystemId1,
           Code1,
           CodeOverflow1,
           SingleValue2,
           SystemId2,
           QuantityCodeId2,
           LowValue2,
           HighValue2
    FROM   @TokenQuantityCompositeSearchParamsCurrent AS A
    WHERE  NOT EXISTS (SELECT *
                       FROM   @TokenQuantityCompositeSearchParams AS B
                       WHERE  B.ResourceTypeId = A.ResourceTypeId
                              AND B.ResourceSurrogateId = A.ResourceSurrogateId
                              AND B.SearchParamId = A.SearchParamId
                              AND (B.SystemId1 = A.SystemId1
                                   OR B.SystemId1 IS NULL
                                      AND A.SystemId1 IS NULL)
                              AND B.Code1 = A.Code1
                              AND (B.CodeOverflow1 = A.CodeOverflow1
                                   OR B.CodeOverflow1 IS NULL
                                      AND A.CodeOverflow1 IS NULL)
                              AND (B.SingleValue2 = A.SingleValue2
                                   OR B.SingleValue2 IS NULL
                                      AND A.SingleValue2 IS NULL)
                              AND (B.SystemId2 = A.SystemId2
                                   OR B.SystemId2 IS NULL
                                      AND A.SystemId2 IS NULL)
                              AND (B.QuantityCodeId2 = A.QuantityCodeId2
                                   OR B.QuantityCodeId2 IS NULL
                                      AND A.QuantityCodeId2 IS NULL)
                              AND (B.LowValue2 = A.LowValue2
                                   OR B.LowValue2 IS NULL
                                      AND A.LowValue2 IS NULL)
                              AND (B.HighValue2 = A.HighValue2
                                   OR B.HighValue2 IS NULL
                                      AND A.HighValue2 IS NULL));
    DELETE A
    FROM   dbo.TokenQuantityCompositeSearchParam AS A WITH (INDEX (1))
    WHERE  EXISTS (SELECT *
                   FROM   @TokenQuantityCompositeSearchParamsDelete AS B
                   WHERE  B.ResourceTypeId = A.ResourceTypeId
                          AND B.ResourceSurrogateId = A.ResourceSurrogateId
                          AND B.SearchParamId = A.SearchParamId
                          AND (B.SystemId1 = A.SystemId1
                               OR B.SystemId1 IS NULL
                                  AND A.SystemId1 IS NULL)
                          AND B.Code1 = A.Code1
                          AND (B.CodeOverflow1 = A.CodeOverflow1
                               OR B.CodeOverflow1 IS NULL
                                  AND A.CodeOverflow1 IS NULL)
                          AND (B.SingleValue2 = A.SingleValue2
                               OR B.SingleValue2 IS NULL
                                  AND A.SingleValue2 IS NULL)
                          AND (B.SystemId2 = A.SystemId2
                               OR B.SystemId2 IS NULL
                                  AND A.SystemId2 IS NULL)
                          AND (B.QuantityCodeId2 = A.QuantityCodeId2
                               OR B.QuantityCodeId2 IS NULL
                                  AND A.QuantityCodeId2 IS NULL)
                          AND (B.LowValue2 = A.LowValue2
                               OR B.LowValue2 IS NULL
                                  AND A.LowValue2 IS NULL)
                          AND (B.HighValue2 = A.HighValue2
                               OR B.HighValue2 IS NULL
                                  AND A.HighValue2 IS NULL));
    INSERT INTO @TokenQuantityCompositeSearchParamsInsert (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2)
    SELECT ResourceTypeId,
           ResourceSurrogateId,
           SearchParamId,
           SystemId1,
           Code1,
           CodeOverflow1,
           SingleValue2,
           SystemId2,
           QuantityCodeId2,
           LowValue2,
           HighValue2
    FROM   @TokenQuantityCompositeSearchParams AS A
    WHERE  NOT EXISTS (SELECT *
                       FROM   @TokenQuantityCompositeSearchParamsCurrent AS B
                       WHERE  B.ResourceTypeId = A.ResourceTypeId
                              AND B.ResourceSurrogateId = A.ResourceSurrogateId
                              AND B.SearchParamId = A.SearchParamId
                              AND (B.SystemId1 = A.SystemId1
                                   OR B.SystemId1 IS NULL
                                      AND A.SystemId1 IS NULL)
                              AND B.Code1 = A.Code1
                              AND (B.CodeOverflow1 = A.CodeOverflow1
                                   OR B.CodeOverflow1 IS NULL
                                      AND A.CodeOverflow1 IS NULL)
                              AND (B.SingleValue2 = A.SingleValue2
                                   OR B.SingleValue2 IS NULL
                                      AND A.SingleValue2 IS NULL)
                              AND (B.SystemId2 = A.SystemId2
                                   OR B.SystemId2 IS NULL
                                      AND A.SystemId2 IS NULL)
                              AND (B.QuantityCodeId2 = A.QuantityCodeId2
                                   OR B.QuantityCodeId2 IS NULL
                                      AND A.QuantityCodeId2 IS NULL)
                              AND (B.LowValue2 = A.LowValue2
                                   OR B.LowValue2 IS NULL
                                      AND A.LowValue2 IS NULL)
                              AND (B.HighValue2 = A.HighValue2
                                   OR B.HighValue2 IS NULL
                                      AND A.HighValue2 IS NULL));
    INSERT INTO @TokenNumberNumberCompositeSearchParamsCurrent (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange)
    SELECT A.ResourceTypeId,
           A.ResourceSurrogateId,
           SearchParamId,
           SystemId1,
           Code1,
           CodeOverflow1,
           SingleValue2,
           LowValue2,
           HighValue2,
           SingleValue3,
           LowValue3,
           HighValue3,
           HasRange
    FROM   dbo.TokenNumberNumberCompositeSearchParam AS A
           INNER JOIN
           @Ids AS B
           ON B.ResourceTypeId = A.ResourceTypeId
              AND B.ResourceSurrogateId = A.ResourceSurrogateId;
    INSERT INTO @TokenNumberNumberCompositeSearchParamsDelete (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange)
    SELECT ResourceTypeId,
           ResourceSurrogateId,
           SearchParamId,
           SystemId1,
           Code1,
           CodeOverflow1,
           SingleValue2,
           LowValue2,
           HighValue2,
           SingleValue3,
           LowValue3,
           HighValue3,
           HasRange
    FROM   @TokenNumberNumberCompositeSearchParamsCurrent AS A
    WHERE  NOT EXISTS (SELECT *
                       FROM   @TokenNumberNumberCompositeSearchParams AS B
                       WHERE  B.ResourceTypeId = A.ResourceTypeId
                              AND B.ResourceSurrogateId = A.ResourceSurrogateId
                              AND B.SearchParamId = A.SearchParamId
                              AND (B.SystemId1 = A.SystemId1
                                   OR B.SystemId1 IS NULL
                                      AND A.SystemId1 IS NULL)
                              AND B.Code1 = A.Code1
                              AND (B.CodeOverflow1 = A.CodeOverflow1
                                   OR B.CodeOverflow1 IS NULL
                                      AND A.CodeOverflow1 IS NULL)
                              AND (B.SingleValue2 = A.SingleValue2
                                   OR B.SingleValue2 IS NULL
                                      AND A.SingleValue2 IS NULL)
                              AND (B.LowValue2 = A.LowValue2
                                   OR B.LowValue2 IS NULL
                                      AND A.LowValue2 IS NULL)
                              AND (B.HighValue2 = A.HighValue2
                                   OR B.HighValue2 IS NULL
                                      AND A.HighValue2 IS NULL)
                              AND (B.SingleValue3 = A.SingleValue3
                                   OR B.SingleValue3 IS NULL
                                      AND A.SingleValue3 IS NULL)
                              AND (B.LowValue3 = A.LowValue3
                                   OR B.LowValue3 IS NULL
                                      AND A.LowValue3 IS NULL)
                              AND (B.HighValue3 = A.HighValue3
                                   OR B.HighValue3 IS NULL
                                      AND A.HighValue3 IS NULL)
                              AND B.HasRange = A.HasRange);
    DELETE A
    FROM   dbo.TokenNumberNumberCompositeSearchParam AS A WITH (INDEX (1))
    WHERE  EXISTS (SELECT *
                   FROM   @TokenNumberNumberCompositeSearchParamsDelete AS B
                   WHERE  B.ResourceTypeId = A.ResourceTypeId
                          AND B.ResourceSurrogateId = A.ResourceSurrogateId
                          AND B.SearchParamId = A.SearchParamId
                          AND (B.SystemId1 = A.SystemId1
                               OR B.SystemId1 IS NULL
                                  AND A.SystemId1 IS NULL)
                          AND B.Code1 = A.Code1
                          AND (B.CodeOverflow1 = A.CodeOverflow1
                               OR B.CodeOverflow1 IS NULL
                                  AND A.CodeOverflow1 IS NULL)
                          AND (B.SingleValue2 = A.SingleValue2
                               OR B.SingleValue2 IS NULL
                                  AND A.SingleValue2 IS NULL)
                          AND (B.LowValue2 = A.LowValue2
                               OR B.LowValue2 IS NULL
                                  AND A.LowValue2 IS NULL)
                          AND (B.HighValue2 = A.HighValue2
                               OR B.HighValue2 IS NULL
                                  AND A.HighValue2 IS NULL)
                          AND (B.SingleValue3 = A.SingleValue3
                               OR B.SingleValue3 IS NULL
                                  AND A.SingleValue3 IS NULL)
                          AND (B.LowValue3 = A.LowValue3
                               OR B.LowValue3 IS NULL
                                  AND A.LowValue3 IS NULL)
                          AND (B.HighValue3 = A.HighValue3
                               OR B.HighValue3 IS NULL
                                  AND A.HighValue3 IS NULL)
                          AND B.HasRange = A.HasRange);
    INSERT INTO @TokenNumberNumberCompositeSearchParamsInsert (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange)
    SELECT ResourceTypeId,
           ResourceSurrogateId,
           SearchParamId,
           SystemId1,
           Code1,
           CodeOverflow1,
           SingleValue2,
           LowValue2,
           HighValue2,
           SingleValue3,
           LowValue3,
           HighValue3,
           HasRange
    FROM   @TokenNumberNumberCompositeSearchParams AS A
    WHERE  NOT EXISTS (SELECT *
                       FROM   @TokenNumberNumberCompositeSearchParamsCurrent AS B
                       WHERE  B.ResourceTypeId = A.ResourceTypeId
                              AND B.ResourceSurrogateId = A.ResourceSurrogateId
                              AND B.SearchParamId = A.SearchParamId
                              AND (B.SystemId1 = A.SystemId1
                                   OR B.SystemId1 IS NULL
                                      AND A.SystemId1 IS NULL)
                              AND B.Code1 = A.Code1
                              AND (B.CodeOverflow1 = A.CodeOverflow1
                                   OR B.CodeOverflow1 IS NULL
                                      AND A.CodeOverflow1 IS NULL)
                              AND (B.SingleValue2 = A.SingleValue2
                                   OR B.SingleValue2 IS NULL
                                      AND A.SingleValue2 IS NULL)
                              AND (B.LowValue2 = A.LowValue2
                                   OR B.LowValue2 IS NULL
                                      AND A.LowValue2 IS NULL)
                              AND (B.HighValue2 = A.HighValue2
                                   OR B.HighValue2 IS NULL
                                      AND A.HighValue2 IS NULL)
                              AND (B.SingleValue3 = A.SingleValue3
                                   OR B.SingleValue3 IS NULL
                                      AND A.SingleValue3 IS NULL)
                              AND (B.LowValue3 = A.LowValue3
                                   OR B.LowValue3 IS NULL
                                      AND A.LowValue3 IS NULL)
                              AND (B.HighValue3 = A.HighValue3
                                   OR B.HighValue3 IS NULL
                                      AND A.HighValue3 IS NULL)
                              AND B.HasRange = A.HasRange);
    INSERT INTO dbo.ResourceWriteClaim (ResourceSurrogateId, ClaimTypeId, ClaimValue)
    SELECT ResourceSurrogateId,
           ClaimTypeId,
           ClaimValue
    FROM   @ResourceWriteClaimsInsert;
    INSERT INTO dbo.ReferenceSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion)
    SELECT ResourceTypeId,
           ResourceSurrogateId,
           SearchParamId,
           BaseUri,
           ReferenceResourceTypeId,
           ReferenceResourceId,
           ReferenceResourceVersion
    FROM   @ReferenceSearchParamsInsert;
    INSERT INTO dbo.TokenSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow)
    SELECT ResourceTypeId,
           ResourceSurrogateId,
           SearchParamId,
           SystemId,
           Code,
           CodeOverflow
    FROM   @TokenSearchParamsInsert;
    INSERT INTO dbo.TokenStringCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, Text2, TextOverflow2)
    SELECT ResourceTypeId,
           ResourceSurrogateId,
           SearchParamId,
           SystemId1,
           Code1,
           CodeOverflow1,
           Text2,
           TextOverflow2
    FROM   @TokenStringCompositeSearchParamsInsert;
    INSERT INTO dbo.TokenText (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text)
    SELECT ResourceTypeId,
           ResourceSurrogateId,
           SearchParamId,
           Text
    FROM   @TokenTextsInsert;
    INSERT INTO dbo.StringSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsMin, IsMax)
    SELECT ResourceTypeId,
           ResourceSurrogateId,
           SearchParamId,
           Text,
           TextOverflow,
           IsMin,
           IsMax
    FROM   @StringSearchParamsInsert;
    INSERT INTO dbo.UriSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri)
    SELECT ResourceTypeId,
           ResourceSurrogateId,
           SearchParamId,
           Uri
    FROM   @UriSearchParamsInsert;
    INSERT INTO dbo.NumberSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue)
    SELECT ResourceTypeId,
           ResourceSurrogateId,
           SearchParamId,
           SingleValue,
           LowValue,
           HighValue
    FROM   @NumberSearchParamsInsert;
    INSERT INTO dbo.QuantitySearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue)
    SELECT ResourceTypeId,
           ResourceSurrogateId,
           SearchParamId,
           SystemId,
           QuantityCodeId,
           SingleValue,
           LowValue,
           HighValue
    FROM   @QuantitySearchParamsInsert;
    INSERT INTO dbo.DateTimeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsMin, IsMax)
    SELECT ResourceTypeId,
           ResourceSurrogateId,
           SearchParamId,
           StartDateTime,
           EndDateTime,
           IsLongerThanADay,
           IsMin,
           IsMax
    FROM   @DateTimeSearchParamsInsert;
    INSERT INTO dbo.ReferenceTokenCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, CodeOverflow2)
    SELECT ResourceTypeId,
           ResourceSurrogateId,
           SearchParamId,
           BaseUri1,
           ReferenceResourceTypeId1,
           ReferenceResourceId1,
           ReferenceResourceVersion1,
           SystemId2,
           Code2,
           CodeOverflow2
    FROM   @ReferenceTokenCompositeSearchParamsInsert;
    INSERT INTO dbo.TokenTokenCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SystemId2, Code2, CodeOverflow2)
    SELECT ResourceTypeId,
           ResourceSurrogateId,
           SearchParamId,
           SystemId1,
           Code1,
           CodeOverflow1,
           SystemId2,
           Code2,
           CodeOverflow2
    FROM   @TokenTokenCompositeSearchParamsInsert;
    INSERT INTO dbo.TokenDateTimeCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, StartDateTime2, EndDateTime2, IsLongerThanADay2)
    SELECT ResourceTypeId,
           ResourceSurrogateId,
           SearchParamId,
           SystemId1,
           Code1,
           CodeOverflow1,
           StartDateTime2,
           EndDateTime2,
           IsLongerThanADay2
    FROM   @TokenDateTimeCompositeSearchParamsInsert;
    INSERT INTO dbo.TokenQuantityCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2)
    SELECT ResourceTypeId,
           ResourceSurrogateId,
           SearchParamId,
           SystemId1,
           Code1,
           CodeOverflow1,
           SingleValue2,
           SystemId2,
           QuantityCodeId2,
           LowValue2,
           HighValue2
    FROM   @TokenQuantityCompositeSearchParamsInsert;
    INSERT INTO dbo.TokenNumberNumberCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange)
    SELECT ResourceTypeId,
           ResourceSurrogateId,
           SearchParamId,
           SystemId1,
           Code1,
           CodeOverflow1,
           SingleValue2,
           LowValue2,
           HighValue2,
           SingleValue3,
           LowValue3,
           HighValue3,
           HasRange
    FROM   @TokenNumberNumberCompositeSearchParamsInsert;
    COMMIT TRANSACTION;
    SET @FailedResources = (SELECT count(*)
                            FROM   @Resources) - @Rows;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @Rows;
END TRY
BEGIN CATCH
    IF @@trancount > 0
        ROLLBACK;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error', @Start = @st;
    THROW;
END CATCH

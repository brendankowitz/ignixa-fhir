CREATE PROCEDURE dbo.CaptureResourceIdsForChanges
@Resources dbo.ResourceList READONLY
AS
SET NOCOUNT ON;
INSERT INTO dbo.ResourceChangeData (ResourceId, ResourceTypeId, ResourceVersion, ResourceChangeTypeId)
SELECT ResourceId,
       ResourceTypeId,
       Version,
       CASE WHEN IsDeleted = 1 THEN 2 WHEN Version > 1 THEN 1 ELSE 0 END
FROM   @Resources
WHERE  IsHistory = 0;

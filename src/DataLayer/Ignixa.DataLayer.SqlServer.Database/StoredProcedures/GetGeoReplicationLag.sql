CREATE PROCEDURE dbo.GetGeoReplicationLag
AS
BEGIN
    SET NOCOUNT ON;
    SELECT replication_state_desc,
           replication_lag_sec,
           last_replication
    FROM   sys.dm_geo_replication_link_status
    WHERE  role_desc = 'PRIMARY';
END

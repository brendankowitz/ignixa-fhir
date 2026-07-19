CREATE PROCEDURE dbo.AcquireWatchdogLease
@Watchdog VARCHAR (100), @Worker VARCHAR (100), @AllowRebalance BIT=1, @ForceAcquire BIT=0, @LeasePeriodSec FLOAT, @WorkerIsRunning BIT=0, @LeaseEndTime DATETIME OUTPUT, @IsAcquired BIT OUTPUT, @CurrentLeaseHolder VARCHAR (100)=NULL OUTPUT
AS
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @SP AS VARCHAR (100) = 'AcquireWatchdogLease', @Mode AS VARCHAR (100), @msg AS VARCHAR (1000), @MyLeasesNumber AS INT, @OtherValidRequestsOrLeasesNumber AS INT, @MyValidRequestsOrLeasesNumber AS INT, @DesiredLeasesNumber AS INT, @NotLeasedWatchdogNumber AS INT, @WatchdogNumber AS INT, @Now AS DATETIME, @MyLastChangeTime AS DATETIME, @PreviousLeaseHolder AS VARCHAR (100), @Rows AS INT = 0, @NumberOfWorkers AS INT, @st AS DATETIME = getUTCdate(), @RowsInt AS INT, @Pattern AS VARCHAR (100);
BEGIN TRY
    SET @Mode = 'R=' + isnull(@Watchdog, 'NULL') + ' W=' + isnull(@Worker, 'NULL') + ' F=' + isnull(CONVERT (VARCHAR, @ForceAcquire), 'NULL') + ' LP=' + isnull(CONVERT (VARCHAR, @LeasePeriodSec), 'NULL');
    SET @CurrentLeaseHolder = '';
    SET @IsAcquired = 0;
    SET @Now = getUTCdate();
    SET @LeaseEndTime = @Now;
    SET @Pattern = NULLIF ((SELECT Char
                            FROM   dbo.Parameters
                            WHERE  Id = 'WatchdogLeaseHolderIncludePatternFor' + @Watchdog), '');
    IF @Pattern IS NULL
        SET @Pattern = NULLIF ((SELECT Char
                                FROM   dbo.Parameters
                                WHERE  Id = 'WatchdogLeaseHolderIncludePattern'), '');
    IF @Pattern IS NOT NULL
       AND @Worker NOT LIKE @Pattern
        BEGIN
            SET @msg = 'Worker does not match include pattern=' + @Pattern;
            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @Rows, @Text = @msg;
            SET @CurrentLeaseHolder = isnull((SELECT LeaseHolder
                                              FROM   dbo.WatchdogLeases
                                              WHERE  Watchdog = @Watchdog), '');
            RETURN;
        END
    SET @Pattern = NULLIF ((SELECT Char
                            FROM   dbo.Parameters
                            WHERE  Id = 'WatchdogLeaseHolderExcludePatternFor' + @Watchdog), '');
    IF @Pattern IS NULL
        SET @Pattern = NULLIF ((SELECT Char
                                FROM   dbo.Parameters
                                WHERE  Id = 'WatchdogLeaseHolderExcludePattern'), '');
    IF @Pattern IS NOT NULL
       AND @Worker LIKE @Pattern
        BEGIN
            SET @msg = 'Worker matches exclude pattern=' + @Pattern;
            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @Rows, @Text = @msg;
            SET @CurrentLeaseHolder = isnull((SELECT LeaseHolder
                                              FROM   dbo.WatchdogLeases
                                              WHERE  Watchdog = @Watchdog), '');
            RETURN;
        END
    DECLARE @Watchdogs TABLE (
        Watchdog VARCHAR (100) PRIMARY KEY);
    INSERT INTO @Watchdogs
    SELECT Watchdog
    FROM   dbo.WatchdogLeases WITH (NOLOCK)
    WHERE  RemainingLeaseTimeSec * (-1) > 10 * @LeasePeriodSec
           OR @ForceAcquire = 1
              AND Watchdog = @Watchdog
              AND LeaseHolder <> @Worker;
    IF @@rowcount > 0
        BEGIN
            DELETE dbo.WatchdogLeases
            WHERE  Watchdog IN (SELECT Watchdog
                                FROM   @Watchdogs);
            SET @Rows += @@rowcount;
            IF @Rows > 0
                BEGIN
                    SET @msg = '';
                    SELECT @msg = CONVERT (VARCHAR (1000), @msg + CASE WHEN @msg = '' THEN '' ELSE ',' END + Watchdog)
                    FROM   @Watchdogs;
                    SET @msg = CONVERT (VARCHAR (1000), 'Remove old/forced leases:' + @msg);
                    EXECUTE dbo.LogEvent @Process = 'AcquireWatchdogLease', @Status = 'Info', @Mode = @Mode, @Target = 'WatchdogLeases', @Action = 'Delete', @Rows = @Rows, @Text = @msg;
                END
        END
    SET @NumberOfWorkers = 1 + (SELECT count(*)
                                FROM   (SELECT LeaseHolder
                                        FROM   dbo.WatchdogLeases WITH (NOLOCK)
                                        WHERE  LeaseHolder <> @Worker
                                        UNION
                                        SELECT LeaseRequestor
                                        FROM   dbo.WatchdogLeases WITH (NOLOCK)
                                        WHERE  LeaseRequestor <> @Worker
                                               AND LeaseRequestor <> '') AS A);
    SET @Mode = CONVERT (VARCHAR (100), @Mode + ' N=' + CONVERT (VARCHAR (10), @NumberOfWorkers));
    IF NOT EXISTS (SELECT *
                   FROM   dbo.WatchdogLeases WITH (NOLOCK)
                   WHERE  Watchdog = @Watchdog)
        INSERT INTO dbo.WatchdogLeases (Watchdog, LeaseEndTime, LeaseRequestTime)
        SELECT @Watchdog,
               dateadd(day, -10, @Now),
               dateadd(day, -10, @Now)
        WHERE  NOT EXISTS (SELECT *
                           FROM   dbo.WatchdogLeases WITH (TABLOCKX)
                           WHERE  Watchdog = @Watchdog);
    SET @LeaseEndTime = dateadd(second, @LeasePeriodSec, @Now);
    SET @WatchdogNumber = (SELECT count(*)
                           FROM   dbo.WatchdogLeases WITH (NOLOCK));
    SET @NotLeasedWatchdogNumber = (SELECT count(*)
                                    FROM   dbo.WatchdogLeases WITH (NOLOCK)
                                    WHERE  LeaseHolder = ''
                                           OR LeaseEndTime < @Now);
    SET @MyLeasesNumber = (SELECT count(*)
                           FROM   dbo.WatchdogLeases WITH (NOLOCK)
                           WHERE  LeaseHolder = @Worker
                                  AND LeaseEndTime > @Now);
    SET @OtherValidRequestsOrLeasesNumber = (SELECT count(*)
                                             FROM   dbo.WatchdogLeases WITH (NOLOCK)
                                             WHERE  LeaseHolder <> @Worker
                                                    AND LeaseEndTime > @Now
                                                    OR LeaseRequestor <> @Worker
                                                       AND datediff(second, LeaseRequestTime, @Now) < @LeasePeriodSec);
    SET @MyValidRequestsOrLeasesNumber = (SELECT count(*)
                                          FROM   dbo.WatchdogLeases WITH (NOLOCK)
                                          WHERE  LeaseHolder = @Worker
                                                 AND LeaseEndTime > @Now
                                                 OR LeaseRequestor = @Worker
                                                    AND datediff(second, LeaseRequestTime, @Now) < @LeasePeriodSec);
    SET @DesiredLeasesNumber = ceiling(1.0 * @WatchdogNumber / @NumberOfWorkers);
    IF @DesiredLeasesNumber = 0
        SET @DesiredLeasesNumber = 1;
    IF @DesiredLeasesNumber = 1
       AND @OtherValidRequestsOrLeasesNumber = 1
       AND @WatchdogNumber = 1
        SET @DesiredLeasesNumber = 0;
    IF @MyValidRequestsOrLeasesNumber = floor(1.0 * @WatchdogNumber / @NumberOfWorkers)
       AND @OtherValidRequestsOrLeasesNumber + @MyValidRequestsOrLeasesNumber = @WatchdogNumber
        SET @DesiredLeasesNumber = @DesiredLeasesNumber - 1;
    UPDATE dbo.WatchdogLeases
    SET    LeaseHolder          = @Worker,
           LeaseEndTime         = @LeaseEndTime,
           LeaseRequestor       = '',
           @PreviousLeaseHolder = LeaseHolder
    WHERE  Watchdog = @Watchdog
           AND NOT (LeaseRequestor <> @Worker
                    AND datediff(second, LeaseRequestTime, @Now) < @LeasePeriodSec)
           AND (LeaseHolder = @Worker
                AND (LeaseEndTime > @Now
                     OR @WorkerIsRunning = 1)
                OR LeaseEndTime < @Now
                   AND (@DesiredLeasesNumber > @MyLeasesNumber
                        OR @OtherValidRequestsOrLeasesNumber < @WatchdogNumber));
    IF @@rowcount > 0
        BEGIN
            SET @IsAcquired = 1;
            SET @msg = 'Lease holder changed from [' + isnull(@PreviousLeaseHolder, '') + '] to [' + @Worker + ']';
            IF @PreviousLeaseHolder <> @Worker
                EXECUTE dbo.LogEvent @Process = 'AcquireWatchdogLease', @Status = 'Info', @Mode = @Mode, @Text = @msg;
        END
    ELSE
        IF @AllowRebalance = 1
            BEGIN
                SET @CurrentLeaseHolder = (SELECT LeaseHolder
                                           FROM   dbo.WatchdogLeases
                                           WHERE  Watchdog = @Watchdog);
                UPDATE dbo.WatchdogLeases
                SET    LeaseRequestTime = @Now
                WHERE  Watchdog = @Watchdog
                       AND LeaseRequestor = @Worker
                       AND datediff(second, LeaseRequestTime, @Now) < @LeasePeriodSec;
                IF @DesiredLeasesNumber > @MyValidRequestsOrLeasesNumber
                    BEGIN
                        UPDATE A
                        SET    LeaseRequestor   = @Worker,
                               LeaseRequestTime = @Now
                        FROM   dbo.WatchdogLeases AS A
                        WHERE  Watchdog = @Watchdog
                               AND NOT (LeaseRequestor <> @Worker
                                        AND datediff(second, LeaseRequestTime, @Now) < @LeasePeriodSec)
                               AND @NotLeasedWatchdogNumber = 0
                               AND (SELECT count(*)
                                    FROM   dbo.WatchdogLeases AS B
                                    WHERE  B.LeaseHolder = A.LeaseHolder
                                           AND datediff(second, B.LeaseEndTime, @Now) < @LeasePeriodSec) > @DesiredLeasesNumber;
                        SET @RowsInt = @@rowcount;
                        SET @msg = '@DesiredLeasesNumber=[' + CONVERT (VARCHAR (10), @DesiredLeasesNumber) + '] > @MyValidRequestsOrLeasesNumber=[' + CONVERT (VARCHAR (10), @MyValidRequestsOrLeasesNumber) + ']';
                        EXECUTE dbo.LogEvent @Process = 'AcquireWatchdogLease', @Status = 'Info', @Mode = @Mode, @Rows = @RowsInt, @Text = @msg;
                    END
            END
    SET @Mode = CONVERT (VARCHAR (100), @Mode + ' A=' + CONVERT (VARCHAR (1), @IsAcquired));
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @Rows;
END TRY
BEGIN CATCH
    IF @@trancount > 0
        ROLLBACK;
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = 'AcquireWatchdogLease', @Status = 'Error', @Mode = @Mode;
    THROW;
END CATCH

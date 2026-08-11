CREATE TABLE dbo.WatchdogLeases (
    Watchdog              VARCHAR (100) NOT NULL,
    LeaseHolder           VARCHAR (100) CONSTRAINT DF_WatchdogLeases_LeaseHolder DEFAULT '' NOT NULL,
    LeaseEndTime          DATETIME      CONSTRAINT DF_WatchdogLeases_LeaseEndTime DEFAULT 0 NOT NULL,
    RemainingLeaseTimeSec AS            datediff(second, getUTCdate(), LeaseEndTime),
    LeaseRequestor        VARCHAR (100) CONSTRAINT DF_WatchdogLeases_LeaseRequestor DEFAULT '' NOT NULL,
    LeaseRequestTime      DATETIME      CONSTRAINT DF_WatchdogLeases_LeaseRequestTime DEFAULT 0 NOT NULL CONSTRAINT PKC_WatchdogLeases_Watchdog PRIMARY KEY CLUSTERED (Watchdog)
);

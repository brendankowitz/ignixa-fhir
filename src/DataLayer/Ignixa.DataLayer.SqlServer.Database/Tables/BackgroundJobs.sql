CREATE TABLE dbo.BackgroundJobs (
    TenantId                INT             NOT NULL,
    JobId                   NVARCHAR (36)   NOT NULL,
    JobType                 INT             NOT NULL,
    OrchestrationInstanceId NVARCHAR (100)  NULL,
    Status                  NVARCHAR (20)   NOT NULL,
    Definition              NVARCHAR (MAX)  NOT NULL,
    Progress                NVARCHAR (MAX)  NULL,
    Result                  NVARCHAR (MAX)  NULL,
    CreateDate              DATETIMEOFFSET  NOT NULL,
    StartDate               DATETIMEOFFSET  NULL,
    EndDate                 DATETIMEOFFSET  NULL,
    HeartbeatDate           DATETIMEOFFSET  NOT NULL,
    Worker                  NVARCHAR (256)  NULL,
    ErrorMessage            NVARCHAR (1000) NULL,
    CancelRequested         BIT             NOT NULL,
    CONSTRAINT PK_BackgroundJobs PRIMARY KEY (TenantId, JobId)
);

GO

CREATE INDEX IX_BackgroundJobs_CreateDate
    ON dbo.BackgroundJobs(CreateDate);

GO

CREATE INDEX IX_BackgroundJobs_HeartbeatDate
    ON dbo.BackgroundJobs(HeartbeatDate);

GO

CREATE INDEX IX_BackgroundJobs_OrchestrationInstanceId
    ON dbo.BackgroundJobs(OrchestrationInstanceId)
    WHERE OrchestrationInstanceId IS NOT NULL;

GO

CREATE INDEX IX_BackgroundJobs_TenantId_JobType
    ON dbo.BackgroundJobs(TenantId, JobType);

GO

CREATE INDEX IX_BackgroundJobs_TenantId_Status
    ON dbo.BackgroundJobs(TenantId, Status);

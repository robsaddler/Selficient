-- The operator's audit trail and the jobs' run log (M19). The operator is one person with one
-- key, so a hand records what was done and to what, never who; a job run records when it started,
-- when it finished and how it went, so the reviewer can say so and a missed night is seen.
CREATE TABLE dbo.operator_actions (
    id BIGINT IDENTITY(1, 1) NOT NULL CONSTRAINT pk_operator_actions PRIMARY KEY,
    at DATETIME2(0) NOT NULL,
    hand NVARCHAR(40) NOT NULL,
    offer_id BIGINT NULL,
    outcome NVARCHAR(400) NOT NULL
);
GO
CREATE INDEX ix_operator_actions_at ON dbo.operator_actions (at DESC);
GO
CREATE TABLE dbo.job_runs (
    id BIGINT IDENTITY(1, 1) NOT NULL CONSTRAINT pk_job_runs PRIMARY KEY,
    job NVARCHAR(40) NOT NULL,
    started_at DATETIME2(0) NOT NULL,
    finished_at DATETIME2(0) NULL,
    outcome NVARCHAR(40) NOT NULL,
    detail NVARCHAR(400) NOT NULL CONSTRAINT df_job_runs_detail DEFAULT (N'')
);
GO
CREATE INDEX ix_job_runs_job ON dbo.job_runs (job, started_at DESC);
GO

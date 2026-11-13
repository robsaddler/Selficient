-- The assumptions desk: the published sets of corrections to the figures the plan works from, the
-- draft in hand, and the record of every figure that ever changed, with why and on what authority.
-- The shipped figures are in the code and are set nought; nothing here is needed for the site to run.
-- Sets are only ever added. Putting an old one back publishes a new set and says which it restores.
CREATE TABLE dbo.figure_sets (
    version INT NOT NULL CONSTRAINT pk_figure_sets PRIMARY KEY,
    published_at DATETIME2(0) NOT NULL,
    published_by NVARCHAR(64) NOT NULL,
    note NVARCHAR(400) NOT NULL,
    restores INT NULL,
    corrections NVARCHAR(MAX) NOT NULL,
    CONSTRAINT ck_figure_sets_version CHECK (version >= 1)
);
GO
-- One row per figure per publication it changed in.
CREATE TABLE dbo.figure_changes (
    id BIGINT IDENTITY(1, 1) NOT NULL CONSTRAINT pk_figure_changes PRIMARY KEY,
    version INT NOT NULL CONSTRAINT fk_figure_changes_set REFERENCES dbo.figure_sets (version),
    parameter_id NVARCHAR(160) COLLATE Latin1_General_BIN2 NOT NULL,
    value_before DECIMAL(18, 4) NOT NULL,
    value_after DECIMAL(18, 4) NOT NULL,
    reason NVARCHAR(400) NOT NULL,
    source NVARCHAR(400) NOT NULL,
    drafted_by NVARCHAR(64) NOT NULL,
    drafted_at DATETIME2(0) NOT NULL
);
GO
CREATE INDEX ix_figure_changes_parameter ON dbo.figure_changes (parameter_id, version DESC);
GO
-- The draft in hand: at most one proposed value for a figure, over whatever set is in force.
CREATE TABLE dbo.figure_drafts (
    parameter_id NVARCHAR(160) COLLATE Latin1_General_BIN2 NOT NULL CONSTRAINT pk_figure_drafts PRIMARY KEY,
    value DECIMAL(18, 4) NOT NULL,
    reason NVARCHAR(400) NOT NULL,
    source NVARCHAR(400) NOT NULL,
    drafted_by NVARCHAR(64) NOT NULL,
    drafted_at DATETIME2(0) NOT NULL
);
GO
-- Who may publish. While this is empty the first operator account made is the one who may, so a
-- new installation has a publisher without anybody being named.
CREATE TABLE dbo.figure_publishers (
    operator_id BIGINT NOT NULL CONSTRAINT pk_figure_publishers PRIMARY KEY CONSTRAINT fk_figure_publishers_operator REFERENCES dbo.operators (id)
);
GO

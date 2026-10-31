-- The catalogue (M18, moved to SQL Server in M19): offers, their checks, the clicks they earn,
-- and when each job last finished. No column is for a person.
CREATE TABLE dbo.offers (
    id BIGINT IDENTITY(1, 1) NOT NULL CONSTRAINT pk_offers PRIMARY KEY,
    action NVARCHAR(80) NOT NULL,
    name NVARCHAR(160) NOT NULL,
    retailer NVARCHAR(160) NOT NULL,
    price_pence BIGINT NOT NULL,
    seen DATE NOT NULL,
    link NVARCHAR(500) NOT NULL,
    note NVARCHAR(160) NOT NULL CONSTRAINT df_offers_note DEFAULT (N''),
    status INT NOT NULL CONSTRAINT df_offers_status DEFAULT (0),
    failures INT NOT NULL CONSTRAINT df_offers_failures DEFAULT (0),
    created_at DATETIME2(0) NOT NULL
);
GO
CREATE INDEX ix_offers_action ON dbo.offers (action, status, price_pence);
GO
CREATE INDEX ix_offers_link ON dbo.offers (link);
GO
CREATE TABLE dbo.checks (
    id BIGINT IDENTITY(1, 1) NOT NULL CONSTRAINT pk_checks PRIMARY KEY,
    offer_id BIGINT NOT NULL CONSTRAINT fk_checks_offer REFERENCES dbo.offers (id),
    at DATETIME2(3) NOT NULL,
    verdict INT NOT NULL,
    status INT NOT NULL,
    evidence NVARCHAR(400) NOT NULL,
    price_pence BIGINT NULL,
    judged_by NVARCHAR(20) NOT NULL
);
GO
CREATE INDEX ix_checks_offer ON dbo.checks (offer_id, at);
GO
CREATE TABLE dbo.clicks (
    offer_id BIGINT NOT NULL CONSTRAINT fk_clicks_offer REFERENCES dbo.offers (id),
    day DATE NOT NULL,
    count BIGINT NOT NULL CONSTRAINT df_clicks_count DEFAULT (0),
    CONSTRAINT pk_clicks PRIMARY KEY (offer_id, day)
);
GO
CREATE TABLE dbo.runs (
    name NVARCHAR(40) NOT NULL CONSTRAINT pk_runs PRIMARY KEY,
    finished_at DATETIME2(0) NOT NULL
);
GO

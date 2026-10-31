-- The platform's own tables (M19): the Data Protection key ring, so a signed cookie survives an
-- app-pool recycle and a second process; and the distributed cache behind HybridCache, in the
-- shape Microsoft.Extensions.Caching.SqlServer expects.
CREATE TABLE dbo.data_protection_keys (
    id INT IDENTITY(1, 1) NOT NULL CONSTRAINT pk_data_protection_keys PRIMARY KEY,
    friendly_name NVARCHAR(200) NOT NULL CONSTRAINT uq_data_protection_keys_name UNIQUE,
    xml NVARCHAR(MAX) NOT NULL,
    created_at DATETIME2(0) NOT NULL
);
GO
CREATE TABLE dbo.cache (
    Id NVARCHAR(449) COLLATE SQL_Latin1_General_CP1_CS_AS NOT NULL CONSTRAINT pk_cache PRIMARY KEY CLUSTERED,
    Value VARBINARY(MAX) NOT NULL,
    ExpiresAtTime DATETIMEOFFSET(7) NOT NULL,
    SlidingExpirationInSeconds BIGINT NULL,
    AbsoluteExpiration DATETIMEOFFSET(7) NULL
);
GO
CREATE NONCLUSTERED INDEX ix_cache_expires ON dbo.cache (ExpiresAtTime);
GO

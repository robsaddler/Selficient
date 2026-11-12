-- Household accounts (M23). An account is an email address and nothing else about a person; it
-- exists only because a household chose to make one, and deleting it removes every row here that
-- names it. The address is compared without case. The security stamp travels in the session
-- cookie, so a deleted or re-stamped account ends every session at the next request.
CREATE TABLE dbo.household_accounts (
    id BIGINT IDENTITY(1, 1) NOT NULL CONSTRAINT pk_household_accounts PRIMARY KEY,
    email NVARCHAR(254) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    security_stamp UNIQUEIDENTIFIER NOT NULL,
    created_at DATETIME2(0) NOT NULL,
    last_seen_at DATETIME2(0) NOT NULL
);
GO
CREATE UNIQUE INDEX ux_household_accounts_email ON dbo.household_accounts (email);
GO
-- Sign-in codes that have been sent. Nothing in a row can be read: the address and the browser's
-- binding value are SHA-256 hashes, and the code is an HMAC keyed by the binding value, which only
-- the browser that asked for the code holds. A row is kept for an hour after it is sent, used or
-- not, so that codes can be counted against an address, and is then swept.
CREATE TABLE dbo.household_signin_codes (
    id BIGINT IDENTITY(1, 1) NOT NULL CONSTRAINT pk_household_signin_codes PRIMARY KEY,
    email_hash BINARY(32) NOT NULL,
    binding_hash BINARY(32) NOT NULL,
    code_hash BINARY(32) NOT NULL,
    created_at DATETIME2(0) NOT NULL,
    expires_at DATETIME2(0) NOT NULL,
    attempts INT NOT NULL CONSTRAINT df_household_signin_codes_attempts DEFAULT (0),
    used_at DATETIME2(0) NULL
);
GO
CREATE UNIQUE INDEX ux_household_signin_codes_binding ON dbo.household_signin_codes (binding_hash);
GO
CREATE INDEX ix_household_signin_codes_email ON dbo.household_signin_codes (email_hash, created_at);
GO
CREATE INDEX ix_household_signin_codes_created ON dbo.household_signin_codes (created_at);
GO

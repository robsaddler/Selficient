-- Operator accounts (M20), replacing the single shared key M18 shipped. A real account each, with
-- a versioned password hash, a time-based second factor whose seed is encrypted by the Data
-- Protection key ring, single-use recovery codes held the same way, per-account lockout, and a
-- security stamp that ends every live session the moment the password or the second factor moves.
-- The username is compared without case but kept as typed; it is the one thing the audit trail
-- names, and no address or user agent is stored against an attempt.
CREATE TABLE dbo.operators (
    id BIGINT IDENTITY(1, 1) NOT NULL CONSTRAINT pk_operators PRIMARY KEY,
    username NVARCHAR(64) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    display_name NVARCHAR(128) NOT NULL,
    password_hash VARCHAR(512) NOT NULL,
    password_set_at DATETIME2(0) NOT NULL,
    totp_secret VARBINARY(1024) NULL,
    totp_confirmed_at DATETIME2(0) NULL,
    last_totp_step BIGINT NOT NULL CONSTRAINT df_operators_last_totp_step DEFAULT (0),
    recovery_codes VARBINARY(4000) NULL,
    security_stamp UNIQUEIDENTIFIER NOT NULL,
    is_enabled BIT NOT NULL CONSTRAINT df_operators_is_enabled DEFAULT (1),
    must_change_password BIT NOT NULL CONSTRAINT df_operators_must_change DEFAULT (0),
    failed_attempts INT NOT NULL CONSTRAINT df_operators_failed DEFAULT (0),
    locked_until DATETIME2(0) NULL,
    last_signin_at DATETIME2(0) NULL,
    created_at DATETIME2(0) NOT NULL
);
GO
CREATE UNIQUE INDEX ux_operators_username ON dbo.operators (username);
GO
-- Every attempt, right or wrong, so a run of refusals is visible on the reviewer. The password is
-- never a column here and never reaches a log.
CREATE TABLE dbo.operator_signin_attempts (
    id BIGINT IDENTITY(1, 1) NOT NULL CONSTRAINT pk_operator_signin_attempts PRIMARY KEY,
    at DATETIME2(0) NOT NULL,
    username NVARCHAR(64) NOT NULL,
    outcome NVARCHAR(32) NOT NULL,
    detail NVARCHAR(400) NOT NULL
);
GO
CREATE INDEX ix_operator_signin_attempts_at ON dbo.operator_signin_attempts (at DESC);
GO
-- A hand on the reviewer now names the operator who took it, which the one shared key could not.
ALTER TABLE dbo.operator_actions ADD operator_id BIGINT NULL, operator_name NVARCHAR(64) NULL;
GO

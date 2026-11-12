-- Passkeys (M25): a second way for a household to sign in, with the device's own fingerprint, face
-- or screen lock. What is kept is public: the credential's id, its public key and its signature
-- counter, a label chosen by the server from the kind of device, and two dates. The private key
-- never leaves the authenticator and nothing biometric reaches us. A credential id is unique
-- across every account. The rows go when the account does.
CREATE TABLE dbo.household_passkeys (
    id BIGINT IDENTITY(1, 1) NOT NULL CONSTRAINT pk_household_passkeys PRIMARY KEY,
    account_id BIGINT NOT NULL CONSTRAINT fk_household_passkeys_account REFERENCES dbo.household_accounts (id) ON DELETE CASCADE,
    credential_id VARBINARY(1023) NOT NULL,
    public_key VARBINARY(1024) NOT NULL,
    sign_count BIGINT NOT NULL,
    label NVARCHAR(40) NOT NULL,
    created_at DATETIME2(0) NOT NULL,
    last_used_at DATETIME2(0) NULL
);
GO
-- 900 bytes is the most an index key may be; a credential id is 1023 at most by the standard and
-- 16 to 64 in every authenticator made, so the unique index is on a hash of it.
ALTER TABLE dbo.household_passkeys ADD credential_hash AS CAST(HASHBYTES('SHA2_256', credential_id) AS BINARY(32)) PERSISTED NOT NULL;
GO
CREATE UNIQUE INDEX ux_household_passkeys_credential ON dbo.household_passkeys (credential_hash);
GO
CREATE INDEX ix_household_passkeys_account ON dbo.household_passkeys (account_id);
GO

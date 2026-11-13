-- Street groups: neighbours telling each other what each could offer when the power, the water or
-- the phones are off. The server keeps who is in what and, for each, a record it cannot read: the
-- key is made in a browser and travels in the part of a link no server is sent. What is here is a
-- group's id, the hash of the pass its invitation carries, its name as ciphertext, and for each
-- member the account, a random id the other members know it by, when it joined and last wrote, and
-- its record as ciphertext. Whoever joined first among those still in is the one who may remove a
-- home, so there is no role to be left behind. A member's row goes with its account; a group goes
-- with its last member, or is swept when an account's deletion empties it.
CREATE TABLE dbo.street_groups (
    id UNIQUEIDENTIFIER NOT NULL CONSTRAINT pk_street_groups PRIMARY KEY,
    created_at DATETIME2(0) NOT NULL,
    pass_hash BINARY(32) NOT NULL,
    about NVARCHAR(2000) NOT NULL
);
GO
CREATE TABLE dbo.street_members (
    seq BIGINT IDENTITY(1, 1) NOT NULL,
    group_id UNIQUEIDENTIFIER NOT NULL CONSTRAINT fk_street_members_group REFERENCES dbo.street_groups (id) ON DELETE CASCADE,
    account_id BIGINT NOT NULL CONSTRAINT fk_street_members_account REFERENCES dbo.household_accounts (id) ON DELETE CASCADE,
    member_id UNIQUEIDENTIFIER NOT NULL CONSTRAINT uq_street_members_member UNIQUE,
    joined_at DATETIME2(0) NOT NULL,
    updated_at DATETIME2(0) NOT NULL,
    record NVARCHAR(MAX) NOT NULL,
    CONSTRAINT pk_street_members PRIMARY KEY (group_id, account_id)
);
GO
CREATE INDEX ix_street_members_account ON dbo.street_members (account_id, seq);
GO

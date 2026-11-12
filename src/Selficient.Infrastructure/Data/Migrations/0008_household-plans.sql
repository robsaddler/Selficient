-- The copy of a plan an account keeps (M24), so that every device a household signs in on stays in
-- step and a lost device is not a lost plan. One row an account, replaced in place. The snapshot
-- is ciphertext under the Data Protection key ring (decision 61): this table cannot be read
-- without it. The version goes up by one with every write, and a write names the version it was
-- based on, so a device that has been away cannot overwrite what another wrote since.
-- Deleting the account deletes the row with it.
CREATE TABLE dbo.household_plans (
    account_id BIGINT NOT NULL CONSTRAINT pk_household_plans PRIMARY KEY
        CONSTRAINT fk_household_plans_account REFERENCES dbo.household_accounts (id) ON DELETE CASCADE,
    snapshot VARBINARY(MAX) NOT NULL,
    version BIGINT NOT NULL,
    updated_at DATETIME2(0) NOT NULL
);
GO

-- Sending a copy of a plan to another device (M22). The sender's browser encrypts the plan with a
-- key it never sends us and leaves the ciphertext here for ten minutes; the receiver takes it once,
-- and the row is deleted as it is read. There is nothing here about who left it: no address, no
-- agent, no account. The id is 128 random bits and is the only way to find a row.
CREATE TABLE dbo.handoffs (
    id BINARY(16) NOT NULL CONSTRAINT pk_handoffs PRIMARY KEY,
    ciphertext VARBINARY(MAX) NOT NULL,
    created_at DATETIME2(0) NOT NULL,
    expires_at DATETIME2(0) NOT NULL
);
GO
CREATE INDEX ix_handoffs_expires_at ON dbo.handoffs (expires_at);
GO

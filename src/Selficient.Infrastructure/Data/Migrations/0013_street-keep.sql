-- What a member's own devices need and nobody else is given: its key for the closer circle and the
-- sharing it chose, sealed in its browser with the group's key, which the server does not have. It
-- is answered only to the account whose row it is, so a second device, or a new one after every
-- device was lost, needs only the invitation again.
ALTER TABLE dbo.street_members ADD keep NVARCHAR(MAX) NULL;
GO

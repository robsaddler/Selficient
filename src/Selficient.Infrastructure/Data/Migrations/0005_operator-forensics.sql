-- The address and the agent behind every operator sign-in, and behind every change to the
-- catalogue (M20a). The "no personal data" rule is a promise to the households who read the site,
-- not a constraint on the back office: a security surface whose trail cannot say where a sign-in
-- came from cannot be investigated after an incident. Nothing here is a reader's.
ALTER TABLE dbo.operator_signin_attempts ADD address NVARCHAR(64) NULL, agent NVARCHAR(400) NULL;
GO
ALTER TABLE dbo.operator_actions ADD address NVARCHAR(64) NULL;
GO

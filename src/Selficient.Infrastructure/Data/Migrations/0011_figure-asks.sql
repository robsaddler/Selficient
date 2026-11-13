-- Which figures a household may give for itself becomes something an expert switches and a
-- publisher publishes, beside the figures' values. A set says where it differs from the product as
-- shipped: the question to ask, or null where a figure is switched off. A draft and a change are
-- each of a kind, a value (0) or a switch (1), so a figure may have one draft of each.
ALTER TABLE dbo.figure_sets ADD asks NVARCHAR(MAX) NOT NULL CONSTRAINT df_figure_sets_asks DEFAULT N'{}';
GO
ALTER TABLE dbo.figure_changes ADD kind TINYINT NOT NULL CONSTRAINT df_figure_changes_kind DEFAULT 0, question NVARCHAR(200) NULL;
GO
ALTER TABLE dbo.figure_drafts ADD kind TINYINT NOT NULL CONSTRAINT df_figure_drafts_kind DEFAULT 0, question NVARCHAR(200) NULL;
GO
ALTER TABLE dbo.figure_drafts DROP CONSTRAINT pk_figure_drafts;
GO
ALTER TABLE dbo.figure_drafts ADD CONSTRAINT pk_figure_drafts PRIMARY KEY (parameter_id, kind);
GO

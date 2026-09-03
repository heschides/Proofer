IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903152847_AddFormAttestations'
)
BEGIN
    CREATE TABLE [FormAttestations] (
        [Id] bigint NOT NULL IDENTITY,
        [FormId] int NOT NULL,
        [Kind] nvarchar(20) NOT NULL,
        [CompletedOn] date NULL,
        [ActorKind] nvarchar(20) NOT NULL,
        [ActorUserId] int NULL,
        [RecordedAtUtc] datetime2 NOT NULL,
        [EvidenceNoteId] int NULL,
        [PrerequisiteStateJson] nvarchar(4000) NULL,
        [Reason] nvarchar(500) NULL,
        CONSTRAINT [PK_FormAttestations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_FormAttestations_Forms_FormId] FOREIGN KEY ([FormId]) REFERENCES [Forms] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_FormAttestations_Users_ActorUserId] FOREIGN KEY ([ActorUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903152847_AddFormAttestations'
)
BEGIN
    CREATE INDEX [IX_FormAttestations_ActorUserId] ON [FormAttestations] ([ActorUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903152847_AddFormAttestations'
)
BEGIN
    CREATE INDEX [IX_FormAttestations_FormId_RecordedAtUtc] ON [FormAttestations] ([FormId], [RecordedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903152847_AddFormAttestations'
)
BEGIN
    INSERT INTO [FormAttestations]
        ([FormId], [Kind], [CompletedOn], [ActorKind], [ActorUserId],
         [RecordedAtUtc], [EvidenceNoteId], [PrerequisiteStateJson], [Reason])
    SELECT [Id], N'Attested', CAST([CompletedDate] AS date), N'System', NULL,
           SYSUTCDATETIME(), NULL, NULL, N'pre-attestation record'
    FROM [Forms]
    WHERE [CompletedDate] IS NOT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903152847_AddFormAttestations'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260903152847_AddFormAttestations', N'10.0.5');
END;


IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903173950_AddDocumentArtifacts'
)
BEGIN
    CREATE TABLE [DocumentArtifacts] (
        [Id] int NOT NULL IDENTITY,
        [PersonId] int NOT NULL,
        [AgencyId] int NOT NULL,
        [Kind] nvarchar(40) NOT NULL,
        [CycleStart] date NOT NULL,
        [Origin] nvarchar(30) NOT NULL,
        [GeneratedAtUtc] datetime2 NOT NULL,
        [GeneratedByUserId] int NOT NULL,
        [ContentSha256] char(64) NULL,
        [ByteCount] bigint NULL,
        [SuggestedFileName] nvarchar(260) NULL,
        [TemplateOwner] nvarchar(50) NULL,
        [TemplateKey] nvarchar(100) NULL,
        [TemplateVersion] int NULL,
        [BlankFieldsJson] nvarchar(4000) NOT NULL,
        [ExternalNote] nvarchar(1000) NULL,
        [SupersededByArtifactId] int NULL,
        CONSTRAINT [PK_DocumentArtifacts] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DocumentArtifacts_Agencies_AgencyId] FOREIGN KEY ([AgencyId]) REFERENCES [Agencies] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_DocumentArtifacts_People_PersonId] FOREIGN KEY ([PersonId]) REFERENCES [People] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_DocumentArtifacts_Users_GeneratedByUserId] FOREIGN KEY ([GeneratedByUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903173950_AddDocumentArtifacts'
)
BEGIN
    CREATE INDEX [IX_DocumentArtifacts_AgencyId] ON [DocumentArtifacts] ([AgencyId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903173950_AddDocumentArtifacts'
)
BEGIN
    CREATE INDEX [IX_DocumentArtifacts_GeneratedByUserId] ON [DocumentArtifacts] ([GeneratedByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903173950_AddDocumentArtifacts'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_DocumentArtifacts_OneLivePerCycle] ON [DocumentArtifacts] ([PersonId], [Kind], [CycleStart]) WHERE [SupersededByArtifactId] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903173950_AddDocumentArtifacts'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260903173950_AddDocumentArtifacts', N'10.0.5');
END;


IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903175219_AddPersonCreatedAtAndStatus'
)
BEGIN
    ALTER TABLE [People] ADD [CreatedAtUtc] datetime2 NOT NULL DEFAULT '0001-01-01T00:00:00.0000000';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903175219_AddPersonCreatedAtAndStatus'
)
BEGIN
    ALTER TABLE [People] ADD [Status] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903175219_AddPersonCreatedAtAndStatus'
)
BEGIN
    ALTER TABLE [People] ADD [StatusChangedAtUtc] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903175219_AddPersonCreatedAtAndStatus'
)
BEGIN
    ALTER TABLE [People] ADD [StatusChangedByUserId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903175219_AddPersonCreatedAtAndStatus'
)
BEGIN
    ALTER TABLE [People] ADD [StatusNote] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903175219_AddPersonCreatedAtAndStatus'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260903175219_AddPersonCreatedAtAndStatus', N'10.0.5');
END;


IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903183136_AddLegalHolds'
)
BEGIN
    CREATE TABLE [LegalHolds] (
        [Id] int NOT NULL IDENTITY,
        [AgencyId] int NOT NULL,
        [PersonId] int NOT NULL,
        [Reason] nvarchar(500) NOT NULL,
        [CaseReference] nvarchar(100) NULL,
        [IssuedBy] nvarchar(150) NULL,
        [EffectiveAtUtc] datetime2 NOT NULL,
        [PlacedByUserId] int NOT NULL,
        [PlacedAtUtc] datetime2 NOT NULL,
        [IsReleased] bit NOT NULL,
        [ReleasedByUserId] int NULL,
        [ReleasedAtUtc] datetime2 NULL,
        [ReleaseNote] nvarchar(500) NULL,
        CONSTRAINT [PK_LegalHolds] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_LegalHolds_Agencies_AgencyId] FOREIGN KEY ([AgencyId]) REFERENCES [Agencies] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_LegalHolds_People_PersonId] FOREIGN KEY ([PersonId]) REFERENCES [People] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_LegalHolds_Users_PlacedByUserId] FOREIGN KEY ([PlacedByUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_LegalHolds_Users_ReleasedByUserId] FOREIGN KEY ([ReleasedByUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903183136_AddLegalHolds'
)
BEGIN
    CREATE INDEX [IX_LegalHolds_AgencyId] ON [LegalHolds] ([AgencyId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903183136_AddLegalHolds'
)
BEGIN
    CREATE INDEX [IX_LegalHolds_PersonId_IsReleased] ON [LegalHolds] ([PersonId], [IsReleased]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903183136_AddLegalHolds'
)
BEGIN
    CREATE INDEX [IX_LegalHolds_PlacedByUserId] ON [LegalHolds] ([PlacedByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903183136_AddLegalHolds'
)
BEGIN
    CREATE INDEX [IX_LegalHolds_ReleasedByUserId] ON [LegalHolds] ([ReleasedByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903183136_AddLegalHolds'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260903183136_AddLegalHolds', N'10.0.5');
END;


IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903185920_AddDocumentTemplatesAndSafetyPlans'
)
BEGIN
    CREATE TABLE [DocumentTemplates] (
        [Id] int NOT NULL IDENTITY,
        [AgencyId] int NULL,
        [Kind] nvarchar(40) NOT NULL,
        [Version] int NOT NULL,
        [Body] nvarchar(max) NOT NULL,
        [PublishedAtUtc] datetime2 NOT NULL,
        [PublishedByUserId] int NULL,
        [RetiredAtUtc] datetime2 NULL,
        CONSTRAINT [PK_DocumentTemplates] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DocumentTemplates_Agencies_AgencyId] FOREIGN KEY ([AgencyId]) REFERENCES [Agencies] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_DocumentTemplates_Users_PublishedByUserId] FOREIGN KEY ([PublishedByUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903185920_AddDocumentTemplatesAndSafetyPlans'
)
BEGIN
    CREATE UNIQUE INDEX [IX_DocumentTemplates_AgencyKindVersion] ON [DocumentTemplates] ([AgencyId], [Kind], [Version]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903185920_AddDocumentTemplatesAndSafetyPlans'
)
BEGIN
    CREATE INDEX [IX_DocumentTemplates_PublishedByUserId] ON [DocumentTemplates] ([PublishedByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903185920_AddDocumentTemplatesAndSafetyPlans'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AgencyId', N'Kind', N'Version', N'Body', N'PublishedAtUtc', N'PublishedByUserId', N'RetiredAtUtc') AND [object_id] = OBJECT_ID(N'[DocumentTemplates]'))
        SET IDENTITY_INSERT [DocumentTemplates] ON;
    EXEC(N'INSERT INTO [DocumentTemplates] ([Id], [AgencyId], [Kind], [Version], [Body], [PublishedAtUtc], [PublishedByUserId], [RetiredAtUtc])
    VALUES (1, NULL, N''PrivacyPractices'', 1, CONCAT(CAST(N''# Notice of Privacy Practices'' AS nvarchar(max)), nchar(10), nchar(10), N''PROVISIONAL SATI DEFAULT - AGENCY PRIVACY AND LEGAL REVIEW REQUIRED'', nchar(10), nchar(10), N''Prepared for cycle beginning: {{cycle.start}}'', nchar(10), nchar(10), N''This notice describes general ways {{agency.name}} may use and share information about {{consumer.full_name}}, and how the individual or authorized representative may exercise privacy rights. It is a generic starting point and must be replaced or approved by the agency before production use.'', nchar(10), nchar(10), N''## Our responsibilities'', nchar(10), nchar(10), N''- Protect the privacy and security of health and service information.'', nchar(10), N''- Follow the privacy practices described in the agency''''s current approved notice.'', nchar(10), N''- Notify affected people when required after a breach of unsecured information.'', nchar(10), N''- Provide the current notice when privacy practices materially change.'', nchar(10), nchar(10), N''## How information may be used or shared'', nchar(10), nchar(10), N''Information may be used or shared as permitted or required by applicable law for treatment and service coordination, payment, health-care operations, public-health and safety duties, oversight, legal proceedings, and other specifically authorized purposes. Uses or disclosures requiring written authorization will not occur without that authorization, and an authorization may be revoked as allowed by law.'', nchar(10), nchar(10), N''## Individual privacy rights'', nchar(10), nchar(10), N''- Ask to inspect or obtain a copy of records, subject to lawful limits.'', nchar(10), N''- Ask for a correction or amendment.'', nchar(10), N''- Ask for confidential communications or certain restrictions.'', nchar(10), N''- Ask for an accounting of qualifying disclosures.'', nchar(10), N''- Receive a paper copy of the agency''''s approved notice.'', nchar(10), N''- Make a privacy complaint without retaliation.'', nchar(10), nchar(10), N''## Questions or complaints'', nchar(10), nchar(10), N''Contact {{agency.name}} at {{agency.address}} or {{agency.phone}} to ask questions, exercise a privacy right, or make a complaint. The agency''''s approved notice must identify any additional external complaint process that applies.'', nchar(10), nchar(10), N''## Receipt'', nchar(10), nchar(10), N''Receiving this notice does not authorize a release of information. Receipt or a documented good-faith effort to provide the notice is recorded separately by authorized staff.'', nchar(10), nchar(10), N''Prepared for: {{consumer.full_name}}'', nchar(10), N''Date of birth: {{consumer.birth_date}}'', nchar(10), N''Case manager: {{case_manager.name}}, {{case_manager.role}}'', nchar(10), N''Coverage cycle: {{cycle.start}} through {{cycle.end}}''), ''2026-09-03T00:00:00.0000000Z'', NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AgencyId', N'Kind', N'Version', N'Body', N'PublishedAtUtc', N'PublishedByUserId', N'RetiredAtUtc') AND [object_id] = OBJECT_ID(N'[DocumentTemplates]'))
        SET IDENTITY_INSERT [DocumentTemplates] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903185920_AddDocumentTemplatesAndSafetyPlans'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260903185920_AddDocumentTemplatesAndSafetyPlans', N'10.0.5');
END;


IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903190302_AddSafetyPlans'
)
BEGIN
    CREATE TABLE [SafetyPlans] (
        [Id] int NOT NULL IDENTITY,
        [PersonId] int NOT NULL,
        [AuthorUserId] int NOT NULL,
        [CycleStart] date NOT NULL,
        [Status] nvarchar(30) NOT NULL,
        [Version] int NOT NULL,
        [Revision] int NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [SubmittedAtUtc] datetime2 NULL,
        [ApprovedAtUtc] datetime2 NULL,
        [ApprovedByUserId] int NULL,
        [ReturnReason] nvarchar(500) NULL,
        [DocumentJson] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_SafetyPlans] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SafetyPlans_People_PersonId] FOREIGN KEY ([PersonId]) REFERENCES [People] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SafetyPlans_Users_ApprovedByUserId] FOREIGN KEY ([ApprovedByUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SafetyPlans_Users_AuthorUserId] FOREIGN KEY ([AuthorUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903190302_AddSafetyPlans'
)
BEGIN
    CREATE INDEX [IX_SafetyPlans_ApprovedByUserId] ON [SafetyPlans] ([ApprovedByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903190302_AddSafetyPlans'
)
BEGIN
    CREATE INDEX [IX_SafetyPlans_AuthorUserId] ON [SafetyPlans] ([AuthorUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903190302_AddSafetyPlans'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SafetyPlans_PersonId_CycleStart_Version] ON [SafetyPlans] ([PersonId], [CycleStart], [Version]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903190302_AddSafetyPlans'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260903190302_AddSafetyPlans', N'10.0.5');
END;


IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903200511_CompleteAnnualDocumentWorkflow'
)
BEGIN
    ALTER TABLE [Settings] ADD [AnnualPacketOpenDaysBefore] int NOT NULL DEFAULT 30;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903200511_CompleteAnnualDocumentWorkflow'
)
BEGIN
    ALTER TABLE [DocumentArtifacts] ADD [SourceContentId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903200511_CompleteAnnualDocumentWorkflow'
)
BEGIN
    ALTER TABLE [DocumentArtifacts] ADD [SourceContentVersion] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903200511_CompleteAnnualDocumentWorkflow'
)
BEGIN
    CREATE TABLE [DocumentAcknowledgments] (
        [Id] int NOT NULL IDENTITY,
        [DocumentArtifactId] int NOT NULL,
        [ReceivedOn] date NULL,
        [GoodFaithEffortReason] nvarchar(1000) NULL,
        [RecordedByUserId] int NOT NULL,
        [RecordedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_DocumentAcknowledgments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DocumentAcknowledgments_DocumentArtifacts_DocumentArtifactId] FOREIGN KEY ([DocumentArtifactId]) REFERENCES [DocumentArtifacts] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_DocumentAcknowledgments_Users_RecordedByUserId] FOREIGN KEY ([RecordedByUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903200511_CompleteAnnualDocumentWorkflow'
)
BEGIN
    CREATE INDEX [IX_DocumentAcknowledgments_DocumentArtifactId] ON [DocumentAcknowledgments] ([DocumentArtifactId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903200511_CompleteAnnualDocumentWorkflow'
)
BEGIN
    CREATE INDEX [IX_DocumentAcknowledgments_RecordedByUserId] ON [DocumentAcknowledgments] ([RecordedByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903200511_CompleteAnnualDocumentWorkflow'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260903200511_CompleteAnnualDocumentWorkflow', N'10.0.5');
END;



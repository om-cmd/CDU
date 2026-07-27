using Domain_Layer.Database;
using Microsoft.EntityFrameworkCore;

namespace Business_Layer.Services;

public static class CommunicationSchemaInitializer
{
    public static async Task EnsureCommunicationTablesAsync(AnalysisDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync("""
IF OBJECT_ID(N'[CrimeReports]', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'CrimeReports', N'Jurisdiction') IS NULL
        ALTER TABLE [CrimeReports] ADD [Jurisdiction] nvarchar(100) NULL;

    IF COL_LENGTH(N'CrimeReports', N'DataSource') IS NULL
        ALTER TABLE [CrimeReports] ADD [DataSource] nvarchar(120) NULL;
END

IF OBJECT_ID(N'[ApplicationUsers]', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'ApplicationUsers', N'ApprovalStatus') IS NULL
        ALTER TABLE [ApplicationUsers] ADD [ApprovalStatus] int NOT NULL
            CONSTRAINT [DF_ApplicationUsers_ApprovalStatus] DEFAULT (1) WITH VALUES;

    IF COL_LENGTH(N'ApplicationUsers', N'ApprovalRequestedAtUtc') IS NULL
        ALTER TABLE [ApplicationUsers] ADD [ApprovalRequestedAtUtc] datetime2 NULL;

    IF COL_LENGTH(N'ApplicationUsers', N'ReviewedAtUtc') IS NULL
        ALTER TABLE [ApplicationUsers] ADD [ReviewedAtUtc] datetime2 NULL;

    IF COL_LENGTH(N'ApplicationUsers', N'ReviewedByUserId') IS NULL
        ALTER TABLE [ApplicationUsers] ADD [ReviewedByUserId] int NULL;

    IF COL_LENGTH(N'ApplicationUsers', N'ReviewNotes') IS NULL
        ALTER TABLE [ApplicationUsers] ADD [ReviewNotes] nvarchar(1000) NULL;

    IF COL_LENGTH(N'ApplicationUsers', N'RegistrationPhotoPath') IS NULL
        ALTER TABLE [ApplicationUsers] ADD [RegistrationPhotoPath] nvarchar(500) NULL;

    IF COL_LENGTH(N'ApplicationUsers', N'IdentityDocumentPath') IS NULL
        ALTER TABLE [ApplicationUsers] ADD [IdentityDocumentPath] nvarchar(500) NULL;

    IF COL_LENGTH(N'ApplicationUsers', N'IdentityDocumentOriginalName') IS NULL
        ALTER TABLE [ApplicationUsers] ADD [IdentityDocumentOriginalName] nvarchar(255) NULL;

    IF COL_LENGTH(N'ApplicationUsers', N'IdentityDocumentContentType') IS NULL
        ALTER TABLE [ApplicationUsers] ADD [IdentityDocumentContentType] nvarchar(120) NULL;
END

IF OBJECT_ID(N'[UserNotifications]', N'U') IS NOT NULL
   AND COL_LENGTH(N'UserNotifications', N'ActionUrl') IS NULL
    ALTER TABLE [UserNotifications] ADD [ActionUrl] nvarchar(500) NULL;
""");

        await db.Database.ExecuteSqlRawAsync("""
IF OBJECT_ID(N'[PasswordResetOtps]', N'U') IS NULL
BEGIN
    CREATE TABLE [PasswordResetOtps](
        [PasswordResetOtpId] bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [UserAccountId] int NOT NULL,
        [Email] nvarchar(255) NOT NULL,
        [CodeHash] nvarchar(128) NOT NULL,
        [ExpiresAtUtc] datetime2 NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UsedAtUtc] datetime2 NULL,
        [Purpose] nvarchar(80) NULL
    );
    CREATE INDEX [IX_PasswordResetOtps_Email_ExpiresAtUtc] ON [PasswordResetOtps]([Email], [ExpiresAtUtc]);
END

IF OBJECT_ID(N'[EmailMessages]', N'U') IS NULL
BEGIN
    CREATE TABLE [EmailMessages](
        [EmailMessageId] bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [Title] nvarchar(180) NOT NULL,
        [Subject] nvarchar(255) NOT NULL,
        [Body] nvarchar(max) NOT NULL,
        [CompanyName] nvarchar(120) NOT NULL,
        [SendToAll] bit NOT NULL,
        [CreatedByUserId] int NOT NULL,
        [CreatedByEmail] nvarchar(255) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [IsDeleted] bit NOT NULL
    );
END

IF OBJECT_ID(N'[EmailMessageRecipients]', N'U') IS NULL
BEGIN
    CREATE TABLE [EmailMessageRecipients](
        [EmailMessageRecipientId] bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [EmailMessageId] bigint NOT NULL,
        [UserAccountId] int NOT NULL,
        [Email] nvarchar(255) NOT NULL,
        [Sent] bit NOT NULL,
        [ErrorMessage] nvarchar(500) NULL,
        [SentAtUtc] datetime2 NULL,
        CONSTRAINT [FK_EmailMessageRecipients_EmailMessages_EmailMessageId]
            FOREIGN KEY([EmailMessageId]) REFERENCES [EmailMessages]([EmailMessageId]) ON DELETE CASCADE
    );
END

IF OBJECT_ID(N'[UserNotifications]', N'U') IS NULL
BEGIN
    CREATE TABLE [UserNotifications](
        [UserNotificationId] bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [Title] nvarchar(180) NOT NULL,
        [Message] nvarchar(max) NOT NULL,
        [ActionUrl] nvarchar(500) NULL,
        [CompanyName] nvarchar(120) NOT NULL,
        [SendToAll] bit NOT NULL,
        [CreatedByUserId] int NOT NULL,
        [CreatedByEmail] nvarchar(255) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [IsDeleted] bit NOT NULL
    );
END

IF OBJECT_ID(N'[UserNotificationRecipients]', N'U') IS NULL
BEGIN
    CREATE TABLE [UserNotificationRecipients](
        [UserNotificationRecipientId] bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [UserNotificationId] bigint NOT NULL,
        [UserAccountId] int NOT NULL,
        [ReadAtUtc] datetime2 NULL,
        CONSTRAINT [FK_UserNotificationRecipients_UserNotifications_UserNotificationId]
            FOREIGN KEY([UserNotificationId]) REFERENCES [UserNotifications]([UserNotificationId]) ON DELETE CASCADE
    );
    CREATE INDEX [IX_UserNotificationRecipients_UserAccountId_ReadAtUtc]
        ON [UserNotificationRecipients]([UserAccountId], [ReadAtUtc]);
END

IF OBJECT_ID(N'[CrimeReports]', N'U') IS NOT NULL
BEGIN
    UPDATE [CrimeReports]
    SET
        [Jurisdiction] = COALESCE(
            NULLIF([Jurisdiction], N''),
            CASE
                WHEN [Neighborhood] LIKE N'Community Area %' THEN N'Chicago, IL, USA'
                ELSE N'Cambridge, MA, USA'
            END),
        [DataSource] = COALESCE(
            NULLIF([DataSource], N''),
            CASE
                WHEN [Neighborhood] LIKE N'Community Area %' THEN N'data.cityofchicago.org'
                ELSE N'Cambridge historical dataset'
            END);

    UPDATE [CrimeReports]
    SET
        [ReportYear] = COALESCE([ReportYear], YEAR([DateOfReport])),
        [ReportMonth] = COALESCE([ReportMonth], MONTH([DateOfReport])),
        [ReportDayOfWeek] = COALESCE([ReportDayOfWeek], DATEPART(WEEKDAY, [DateOfReport]) - 1),
        [CrimeHour] = COALESCE([CrimeHour], DATEPART(HOUR, [CrimeDateTime]))
    WHERE ([DateOfReport] IS NOT NULL AND ([ReportYear] IS NULL OR [ReportMonth] IS NULL OR [ReportDayOfWeek] IS NULL))
       OR ([CrimeDateTime] IS NOT NULL AND [CrimeHour] IS NULL);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CrimeReports_DateOfReport' AND object_id = OBJECT_ID(N'[CrimeReports]'))
        CREATE INDEX [IX_CrimeReports_DateOfReport]
            ON [CrimeReports]([DateOfReport] DESC)
            INCLUDE([CrimeType], [Neighborhood], [ReportingArea]);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CrimeReports_FileNumber' AND object_id = OBJECT_ID(N'[CrimeReports]'))
        CREATE INDEX [IX_CrimeReports_FileNumber]
            ON [CrimeReports]([FileNumber]);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CrimeReports_ReportYear_ReportMonth' AND object_id = OBJECT_ID(N'[CrimeReports]'))
        CREATE INDEX [IX_CrimeReports_ReportYear_ReportMonth]
            ON [CrimeReports]([ReportYear], [ReportMonth])
            INCLUDE([CrimeType]);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CrimeReports_CrimeHour' AND object_id = OBJECT_ID(N'[CrimeReports]'))
        CREATE INDEX [IX_CrimeReports_CrimeHour]
            ON [CrimeReports]([CrimeHour])
            INCLUDE([CrimeType]);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CrimeReports_Neighborhood_CrimeType' AND object_id = OBJECT_ID(N'[CrimeReports]'))
        CREATE INDEX [IX_CrimeReports_Neighborhood_CrimeType]
            ON [CrimeReports]([Neighborhood], [CrimeType]);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CrimeReports_Jurisdiction_DateOfReport' AND object_id = OBJECT_ID(N'[CrimeReports]'))
        CREATE INDEX [IX_CrimeReports_Jurisdiction_DateOfReport]
            ON [CrimeReports]([Jurisdiction], [DateOfReport])
            INCLUDE([CrimeType], [DataSource]);
END

IF OBJECT_ID(N'[UserNotifications]', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_UserNotifications_CreatedAtUtc_IsDeleted' AND object_id = OBJECT_ID(N'[UserNotifications]'))
        CREATE INDEX [IX_UserNotifications_CreatedAtUtc_IsDeleted]
            ON [UserNotifications]([CreatedAtUtc] DESC, [IsDeleted]);
END

IF OBJECT_ID(N'[ApplicationUsers]', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ApplicationUsers_ApprovalStatus_ApprovalRequestedAtUtc' AND object_id = OBJECT_ID(N'[ApplicationUsers]'))
        CREATE INDEX [IX_ApplicationUsers_ApprovalStatus_ApprovalRequestedAtUtc]
            ON [ApplicationUsers]([ApprovalStatus], [ApprovalRequestedAtUtc] DESC)
            INCLUDE([UserType], [IsActive], [Deleted]);
END

IF OBJECT_ID(N'[UserNotificationRecipients]', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_UserNotificationRecipients_UserAccountId_NotificationId_ReadAtUtc' AND object_id = OBJECT_ID(N'[UserNotificationRecipients]'))
        CREATE INDEX [IX_UserNotificationRecipients_UserAccountId_NotificationId_ReadAtUtc]
            ON [UserNotificationRecipients]([UserAccountId], [UserNotificationId], [ReadAtUtc]);
END
""");
    }
}

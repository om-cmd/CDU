using Domain_Layer.Database;
using Microsoft.EntityFrameworkCore;

namespace Business_Layer.Services;

public static class CommunicationSchemaInitializer
{
    public static async Task EnsureCommunicationTablesAsync(AnalysisDbContext db)
    {
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
""");
    }
}

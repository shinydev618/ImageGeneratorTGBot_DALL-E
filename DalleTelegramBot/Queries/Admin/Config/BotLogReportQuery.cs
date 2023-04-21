﻿using DalleTelegramBot.Common.Attributes;
using DalleTelegramBot.Common.Extensions;
using DalleTelegramBot.Common.IDependency;
using DalleTelegramBot.Queries.Base;
using DalleTelegramBot.Services.Telegram;
using Serilog;
using System.IO.Compression;
using System.Security.Cryptography;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace DalleTelegramBot.Queries.Admin.Config;

[Query("bot-config-get-log-report")]
internal class BotLogReportQuery : BaseQuery , ISingletonDependency
{
    public BotLogReportQuery(ITelegramService telegramService) : base(telegramService)
    {
    }

    public override async Task ExecuteAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        long userId = callbackQuery.UserId();

        string logsDirectoryPath = Path.Combine(Environment.CurrentDirectory, "logs");
        string logsDirectoryDestinationPath = Path.Combine(Path.GetTempPath(), "dalle-logs");
        string logsZipPath = $"{logsDirectoryDestinationPath}.zip";
        if (Directory.Exists(logsDirectoryPath))
        {
            try
            {
                DeleteIfExists(logsDirectoryDestinationPath);
                DeleteIfExists(logsZipPath);

                CopyDirectory(logsDirectoryPath, logsDirectoryDestinationPath);

                ZipFile.CreateFromDirectory(logsDirectoryDestinationPath, logsZipPath);
                await _telegramService.SendDocumentAsync(userId, logsZipPath, $"Logs report\n{DateTime.Now:G}\nSha256:`{await GetFileHash(logsZipPath)}`", ParseMode.Markdown, cancellationToken);

                DeleteIfExists(logsDirectoryDestinationPath);
                DeleteIfExists(logsZipPath);
            }
            catch (Exception ex)
            {
                await _telegramService.SendMessageAsync(userId, ex.Message, cancellationToken);
            }
        }
        else
        {
            await _telegramService.SendMessageAsync(userId, $"logs directory could not be found or it may not exist", cancellationToken);
        }
    }

    private void DeleteIfExists(string path)
    {
        if (System.IO.File.Exists(path))
            System.IO.File.Delete(path);

        if (Directory.Exists(path))
            Directory.Delete(path, true);
    }

    public void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        string[] subdirectories = Directory.GetDirectories(sourceDirectory);
        if (!Directory.Exists(destinationDirectory))
            Directory.CreateDirectory(destinationDirectory);

        foreach (string file in Directory.GetFiles(sourceDirectory))
            System.IO.File.Copy(file, Path.Combine(destinationDirectory, Path.GetFileName(file)), true);

        foreach (string subdirectory in subdirectories)
            CopyDirectory(subdirectory, Path.Combine(destinationDirectory, Path.GetFileName(subdirectory)));
    }


    public async Task<string> GetFileHash(string filePath)//todo: move to SecHelper.cs in utils
    {
        using (SHA256 sha256 = SHA256.Create())
        {
            using (FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                byte[] hashBytes = await sha256.ComputeHashAsync(fileStream);
                string hashString = BitConverter.ToString(hashBytes).Replace("-", "");
                return hashString;
            }
        }
    }
}

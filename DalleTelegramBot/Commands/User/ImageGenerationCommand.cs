﻿using DalleTelegramBot.Commands.Base;
using DalleTelegramBot.Common;
using DalleTelegramBot.Common.Attributes;
using DalleTelegramBot.Common.Caching;
using DalleTelegramBot.Common.Enums;
using DalleTelegramBot.Common.Extensions;
using DalleTelegramBot.Common.IDependency;
using DalleTelegramBot.Common.Utilities;
using DalleTelegramBot.Configurations;
using DalleTelegramBot.Data.Contracts;
using DalleTelegramBot.Services.OpenAI;
using DalleTelegramBot.Services.Telegram;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace DalleTelegramBot.Commands.User
{
    [Command("create-image", Role.User)]
    [CheckBanUser]
    internal class ImageGenerationCommand : BaseCommand, IScopedDependency
    {
        private readonly IUserRepository _userRepository;
        private readonly IOpenAIClient _openAIClient;
        private readonly RateLimitingMemoryCache _rateLimitingCache;
        private readonly StateManagementMemoryCache _stateCache;
        public ImageGenerationCommand(ITelegramService telegramService, IUserRepository userRepository, IOpenAIClient openAIClient,
            RateLimitingMemoryCache rateLimitingCache, StateManagementMemoryCache stateCache) : base(telegramService)
        {
            _userRepository = userRepository;
            _openAIClient = openAIClient;
            _rateLimitingCache = rateLimitingCache;
            _stateCache = stateCache;
        }

        public override async Task ExecuteAsync(Message message, CancellationToken cancellationToken)
        {
            long userId = message.UserId();

            var user = await _userRepository.GetByIdAsync(userId);

            var apiKey = user.ApiKey;
            if (message.Text.GetCommand("cancel"))
            {
                _stateCache.RemoveLastCommand(userId);
                await _telegramService.SendMessageAsync(userId, "Your operation cancelled", 
                    replyMarkup: InlineUtility.StartCommandReplyKeyboard, cancellationToken);

                return;
            }
            if (!message.Text.GetCommand("create-image"))
            {
                if (_stateCache.CanGetLastCommand(userId, "create-image", 2, false))
                {
                    var messageResponse = await _telegramService.ReplyMessageAsync(userId, message.MessageId, TextUtility.ImgGenerationProcessingMessage,
                        replyMarkup: new ReplyKeyboardRemove(), ParseMode.Markdown, cancellationToken);

                    var imageResponse = await _openAIClient.GenerateImageAsync(new(message.Text, user.ImageCount, user.ImageSize),
                        cancellationToken, apiKey: apiKey);
                    
                    if (imageResponse.IsSuccess)
                    {
                        await _telegramService.DeleteMessageAsync(userId, messageResponse.MessageId, cancellationToken);
                        messageResponse = await _telegramService.ReplyMessageAsync(userId, message.MessageId, string.Format(
                            TextUtility.ImgGenerationCompletedMessageFormat, imageResponse.Images!.ProcessingTime.ToString("ss\\.fff")),
                            InlineUtility.StartCommandReplyKeyboard, cancellationToken);

                        if (_stateCache.CanGetCommandData(userId, true)[0].Equals("with-limit"))
                        {
                            _rateLimitingCache.UpdateUserMessageCount(userId, imageResponse.Images.Data.Count);
                        }

                        await _telegramService.SendChatActionAsync(userId, ChatAction.UploadPhoto, cancellationToken);
                        var media = imageResponse.Images.Data.Select(x => new InputMediaPhoto(new InputFileUrl(x.Url))).OfType<IAlbumInputMedia>();
                        await _telegramService.SendMediaGroupAsync(userId, message.MessageId, media, cancellationToken);
                    }
                    else
                    {
                        await _telegramService.EditMessageAsync(userId, messageResponse.MessageId,
                            $"️ Uncompleted📛\n({imageResponse.Error!.ProcessingTime:ss\\.fff})\nError:{imageResponse.Error.Error.Message}");
                    }
                }
                return;
            }

            if (string.IsNullOrEmpty(apiKey))
            {
                if (_rateLimitingCache.IsMessageLimitExceeded(userId))
                {
                    await _telegramService.SendMessageAsync(userId, TextUtility.ImgGenerationExceededMessage, ParseMode.Markdown, cancellationToken);
                    return;
                }

                int currentCountMessage = _rateLimitingCache.GetMessageCount(userId);
                if ((user.ImageCount + currentCountMessage) > BotConfig.RateLimitCount)
                {
                    await _telegramService.SendMessageAsync(userId, string.Format(TextUtility.ImgGenerationLimitGenMessage, (BotConfig.RateLimitCount - currentCountMessage)), ParseMode.Markdown, cancellationToken);
                    return;
                }

                _stateCache.SetLastCommand(userId, "create-image", 2, data: "with-limit");
            }
            else
            {
                if (!await _openAIClient.ValidateApiKey(apiKey, cancellationToken))
                {
                    await _telegramService.SendMessageAsync(userId, TextUtility.ImgGenerationBadApiKeyMessage, cancellationToken);
                    return;
                }

                _stateCache.SetLastCommand(userId, "create-image", 2, data: "without-limit");
            }

            await _telegramService.SendMessageAsync(userId, TextUtility.ImgGenerationSendPromptMessage,
                replyMarkup: new ReplyKeyboardMarkup(new KeyboardButton("❌Cancel")) { ResizeKeyboard = true, OneTimeKeyboard = true }, cancellationToken);
        }
    }
}

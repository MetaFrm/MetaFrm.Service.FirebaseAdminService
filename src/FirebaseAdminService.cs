using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using MetaFrm.Database;
using MetaFrm.Extensions;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace MetaFrm.Service
{
    /// <summary>
    /// FirebaseAdminService
    /// </summary>
    public class FirebaseAdminService : IService
    {
        private readonly Priority androidConfigPriority = Priority.Normal;
        private readonly TimeSpan androidConfigTimeToLive = new(28, 0, 0, 0);

        /// <summary>
        /// FirebaseAdminService
        /// </summary>
        public FirebaseAdminService()
        {
            string tmp;
            string[] tmps;

            if (FirebaseApp.DefaultInstance == null)
            {
                FirebaseApp.Create(new AppOptions()
                {
                    Credential = GoogleCredential.FromJson(GetGoogleCredential()),
                });
            }

            try
            {
                tmp = this.GetAttribute("AndroidConfig.Priority");
                this.androidConfigPriority = tmp.EnumParse<Priority>();
            }
            catch (Exception exception)
            {
                if (Factory.Logger.IsEnabled(LogLevel.Error))
                    Factory.Logger.LogError(exception, "{Message}", exception.Message);
            }

            try
            {
                tmp = this.GetAttribute("AndroidConfig.TimeToLive");
                tmps = tmp.Split(' ');

                if (tmps.Length == 2)
                {
                    tmp = tmps[0];

                    tmps = tmps[1].Split(':');

                    if (tmps.Length == 3)
                        this.androidConfigTimeToLive = new(tmp.ToInt(), tmps[0].ToInt(), tmps[1].ToInt(), tmps[2].ToInt());
                }
            }
            catch (Exception exception)
            {
                if (Factory.Logger.IsEnabled(LogLevel.Error))
                    Factory.Logger.LogError(exception, "{Message}", exception.Message);
            }
        }

        private string GetGoogleCredential()
        {
            string key;
            string iv;
            
            key = this.GetAttribute("AesDecryptorKey");
            if (key.IsNullOrEmpty())
                key = Factory.AccessKey;

            iv = this.GetAttribute("AesDecryptorKeyIV");
            if (iv.IsNullOrEmpty())
                iv = nameof(FirebaseAdminService);

            if (key.Length < 5 || iv.Length < 5)
                return "";

            return this.GetAttribute("GoogleCredential").AesDecryptorToBase64String(key, iv);
        }

        Response IService.Request(ServiceData serviceData)
        {
            List<Message> messages;
            Response response;

            try
            {
                if (serviceData.ServiceName == null || !serviceData.ServiceName.Equals("MetaFrm.Service.FirebaseAdminService"))
                    throw new Exception("Not MetaFrm.Service.FirebaseAdminService");

                messages = [];
                response = new();

                foreach (var key in serviceData.Commands.Keys)
                {
                    Command command = serviceData.Commands[key];

                    for (int i = 0; i < command.Values.Count; i++)
                    {
                        string? imageUrlType;
                        string? dataJson;
                        Dictionary<string, string>? keyValues;

                        try
                        {
                            imageUrlType = command.Values[i][nameof(Notification.ImageUrl)].StringValue ?? "";
                            if (imageUrlType.IsNullOrEmpty())
                                imageUrlType = "OK";

                            if (!Uri.IsWellFormedUriString(imageUrlType, UriKind.Absolute))
                                imageUrlType = this.GetAttribute(imageUrlType);
                        }
                        catch (Exception exception)
                        {
                            if (Factory.Logger.IsEnabled(LogLevel.Error))
                                Factory.Logger.LogError(exception, "{Message}", exception.Message);
                            imageUrlType = null;
                        }

                        keyValues = null;
                        dataJson = command.Values[i][nameof(Message.Data)].StringValue ?? "";
                        if (!dataJson.IsNullOrEmpty())
                            keyValues = JsonSerializer.Deserialize<Dictionary<string, string>?>(dataJson);

                        messages.Add(new Message()
                        {
                            Token = command.Values[i][nameof(Message.Token)].StringValue,
                            Notification = new Notification()
                            {
                                Title = command.Values[i][nameof(Notification.Title)].StringValue,
                                Body = command.Values[i][nameof(Notification.Body)].StringValue,
                                ImageUrl = imageUrlType.IsNullOrEmpty() ? null : imageUrlType,
                            },
                            Data = keyValues,
                            Android = new AndroidConfig() { Priority = this.androidConfigPriority, TimeToLive = this.androidConfigTimeToLive },
                        });
                    }
                }

                if (messages.Count > 0)
                    _ = this.FirebaseSendMessage(messages);

                response.Status = Status.OK;
            }
            catch (Exception exception)
            {
                if (Factory.Logger.IsEnabled(LogLevel.Error))
                    Factory.Logger.LogError(exception, "{Message}", exception.Message);
                return new Response(exception);
            }

            return response;
        }

        private async Task<BatchResponse?> FirebaseSendMessage(List<Message> messages)
        {
            try
            {
                var result = await FirebaseMessaging.DefaultInstance.SendEachAsync(messages);

                if (result.FailureCount > 0)
                    this.DeleteFirebaseFCM_Token(messages, result);

                return result;
            }
            catch (Exception exception)
            {
                if (Factory.Logger.IsEnabled(LogLevel.Error))
                    Factory.Logger.LogError(exception, "{Message}", exception.Message);
            }

            return null;
        }

        private void DeleteFirebaseFCM_Token(List<Message> messages, BatchResponse batchResponse)
        {
            IService service;
            Response response;

            ServiceData serviceData = new()
            {
                TransactionScope = false,
            };
            serviceData["1"].CommandText = this.GetAttribute("DeleteToken");
            serviceData["1"].CommandType = System.Data.CommandType.StoredProcedure;
            serviceData["1"].AddParameter("TOKEN_TYPE", DbType.NVarChar, 50);
            serviceData["1"].AddParameter("TOKEN_STR", DbType.NVarChar, 200);

            for (int i = 0; i < messages.Count; i++) 
            {
                if (!batchResponse.Responses[i].IsSuccess)
                {
                    serviceData["1"].NewRow();
                    serviceData["1"].SetValue("TOKEN_TYPE", "Firebase.FCM");
                    serviceData["1"].SetValue("TOKEN_STR", messages[i].Token);
                }
            }

            if (serviceData["1"].Values.Count > 0)
            {
                service = (IService)Factory.CreateInstance(serviceData.ServiceName);
                response = service.Request(serviceData);

                if (response.Status != Status.OK && Factory.Logger.IsEnabled(LogLevel.Error))
                    Factory.Logger.LogError("Delete FirebaseFCM Token  Fail : {Message}", response.Message);
            }
        }
    }
}
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
                    for (int i = 0; i < serviceData.Commands[key].Values.Count; i++)
                    {
                        string? imageUrlType;
                        string? dataJson;
                        Dictionary<string, string>? keyValues;

                        try
                        {
                            imageUrlType = serviceData.Commands[key].Values[i][nameof(Notification.ImageUrl)].StringValue ?? "";
                            if (imageUrlType.IsNullOrEmpty())
                                imageUrlType = "OK";

                            if (!Uri.IsWellFormedUriString(imageUrlType, UriKind.Absolute))
                                imageUrlType = this.GetAttribute(imageUrlType);
                        }
                        catch (Exception exception)
                        {
                            Factory.Logger.LogError(exception, "{Message}", exception.Message);
                            imageUrlType = null;
                        }

                        keyValues = null;
                        dataJson = serviceData.Commands[key].Values[i][nameof(Message.Data)].StringValue ?? "";
                        if (!dataJson.IsNullOrEmpty())
                            keyValues = JsonSerializer.Deserialize<Dictionary<string, string>?>(dataJson);

                        messages.Add(new Message()
                        {
                            Token = serviceData.Commands[key].Values[i][nameof(Message.Token)].StringValue,
                            Notification = new Notification()
                            {
                                Title = serviceData.Commands[key].Values[i][nameof(Notification.Title)].StringValue,
                                Body = serviceData.Commands[key].Values[i][nameof(Notification.Body)].StringValue,
                                ImageUrl = imageUrlType.IsNullOrEmpty() ? null : imageUrlType,
                            },
                            Data = keyValues,
                            Android = new AndroidConfig() { Priority = this.androidConfigPriority, TimeToLive = this.androidConfigTimeToLive },
                        });
                    }
                }

                if (messages.Count > 0)
                    _ = this.FirebaseSandMessage(messages);

                response.Status = Status.OK;
            }
            catch (MetaFrmException exception)
            {
                Factory.Logger.LogError(exception, "{Message}", exception.Message);
                return new Response(exception);
            }
            catch (Exception exception)
            {
                Factory.Logger.LogError(exception, "{Message}", exception.Message);
                return new Response(exception);
            }

            return response;
        }

        private async Task<BatchResponse?> FirebaseSandMessage(List<Message> messages)
        {
            var result = await FirebaseMessaging.DefaultInstance.SendEachAsync(messages);

            if (result.FailureCount > 0)
                DeleteFirebaseFCM_Token(messages, result);

            return result;
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

            for (int i = 0; i < messages.Count; i++) 
            {
                if (!batchResponse.Responses[i].IsSuccess)
                {
                    serviceData["1"].AddParameter("TOKEN_TYPE", DbType.NVarChar, 50, "Firebase.FCM");
                    serviceData["1"].AddParameter("TOKEN_STR", DbType.NVarChar, 200, messages[i].Token);
                }
            }


            service = (IService)Factory.CreateInstance(serviceData.ServiceName);
            response = service.Request(serviceData);

            if (response.Status != Status.OK)
                if (response.Message != null)
                {
                    throw new Exception(response.Message);
                }
                else
                    throw new Exception("Delete FirebaseFCM Token  Fail !!");
        }

        private void Sample()
        {
            ServiceData serviceData1 = new()
            {
                ServiceName = "MetaFrm.Service.FirebaseAdminService",
                TransactionScope = false,
            };

            serviceData1["1"].CommandText = "FirebaseAdminService";

            string tmp = "dLL7FSzqQM2e2nfjiRKjDm:APA91bGlvrRlAa3b_f0hrzoEpYnEKSXQ7LTLwqus5t_R7SnfOqcEblscWK3Ny-5kPs1cpx9pUF_jVIFvMoBQ25kAcjwjuBbpIbaWcW0XPCIeKefA_0uaLPwD2PzIKRin2MXli1zC-11i";
            serviceData1["1"].AddParameter("Token", DbType.NVarChar, 4000, tmp);
            serviceData1["1"].AddParameter("Title", DbType.NVarChar, 4000, $"Title {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            serviceData1["1"].AddParameter("Body", DbType.NVarChar, 4000, $"Body {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            serviceData1["1"].AddParameter("ImageUrl", DbType.NVarChar, 4000, "OK");
            serviceData1["1"].AddParameter("Data", DbType.NVarChar, 4000, JsonSerializer.Serialize(new Dictionary<string, string> { { "Menu", "7,8" }, { "Search", "NAMESPACE" } }));
            serviceData1["1"].NewRow();

            tmp = "cKq983XtRESAXjqA0CxQED:APA91bEuqEC4wK9d6PiCiBX9x6srpgkrzEzvpmQwAOIHiZlUiHfjsGCeQJHg8omUqe3yV5FhpVyR4rpxDP7lyDHlAiAIkzIpNygyJknxwKgIOQHG9YzbMHLLR4507YtayDFBJtXpG-H3";
            serviceData1["1"].SetValue("Token", tmp);
            serviceData1["1"].SetValue("Title", $"Title {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            serviceData1["1"].SetValue("Body", $"Body {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            serviceData1["1"].SetValue("ImageUrl", "OK");
            serviceData1["1"].SetValue("Data", JsonSerializer.Serialize(new Dictionary<string, string> { { "Menu", "7,8" }, { "Search", "NAMESPACE" } }));

            string tmp1 = JsonSerializer.Serialize(serviceData1);
        }
    }
}
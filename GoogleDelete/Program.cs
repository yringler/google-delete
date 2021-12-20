using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GmailQuickstart
{
    class Program
    {
        // If modifying these scopes, delete your previously saved credentials
        // at ~/.credentials/gmail-dotnet-quickstart.json
        static string[] Scopes = { "https://mail.google.com/" };
        static string ApplicationName = "Gmail API .NET Quickstart";

        static void Main(string[] args)
        {
            UserCredential credential;

            using (var stream =
                new FileStream("credentials.json".ToApplicationPath(), FileMode.Open, FileAccess.Read))
            {
                // The file token.json stores the user's access and refresh tokens, and is created
                // automatically when the authorization flow completes for the first time.
                string credPath = "token.json".ToApplicationPath();
                credential = GoogleWebAuthorizationBroker.AuthorizeAsync(
                    GoogleClientSecrets.FromStream(stream).Secrets,
                    Scopes,
                    "user",
                    CancellationToken.None,
                    new FileDataStore(credPath, true)).Result;
                Console.WriteLine("Credential file saved to: " + credPath);
            }

            // Create Gmail API service.
            var service = new GmailService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = ApplicationName,
            });

            var query = service.Users.Messages.List("me");

            query.Q = "in:trash";
            query.LabelIds = new Google.Apis.Util.Repeatable<string>(new[] { "TRASH" });
            query.IncludeSpamTrash = true;

            var response = query.Execute();

            var ids = response.Messages?.Select(m => m.Id).ToList() ?? new List<string>();

            if (!ids.Any())
            {
                Console.WriteLine("Nothing to do");
            }
            else
            {
                Console.WriteLine($"Deleting: ${string.Join("\n", ids)}");

                var delete = service.Users.Messages.BatchDelete(new BatchDeleteMessagesRequest
                {
                    Ids = ids
                }, "me");

                delete.Execute();
            }

            Task.Delay(TimeSpan.FromSeconds(15));
            Environment.Exit(0);
        }
    }

    public static class Extensions
    {
        public static string ToApplicationPath(this string fileName)
        {
            var exePath = Path.GetDirectoryName(System.Reflection
                                .Assembly.GetExecutingAssembly().Location);
            Regex appPathMatcher = new Regex(@"(?<!fil)[A-Za-z]:\\+[\S\s]*?(?=\\+bin)");
            var appRoot = appPathMatcher.Match(exePath).Value;
            Console.WriteLine(appRoot);
            return Path.Combine(appRoot, fileName);
        }
    }
}
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Http;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace GmailQuickstart
{
    class Program
    {
        // If modifying these scopes, delete the token store (token.json folder next to credentials.json).
        static string[] Scopes = { "https://mail.google.com/" };
        static string ApplicationName = "Gmail API .NET Quickstart";

        static int Main(string[] args)
        {
            try
            {
                Run();
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR: " + ex);
                return 1;
            }
        }

        static void Run()
        {
            var httpClientFactory = new Ipv4FirstHttpClientFactory();
            UserCredential credential;

            string credentialsPath = "credentials.json".ToApplicationPath();
            // The token.json folder stores the user's access and refresh tokens, and is created
            // automatically when the authorization flow completes for the first time.
            string credPath = "token.json".ToApplicationPath();

            using (var stream = new FileStream(credentialsPath, FileMode.Open, FileAccess.Read))
            {
                credential = GoogleWebAuthorizationBroker.AuthorizeAsync(
                    new GoogleAuthorizationCodeFlow.Initializer
                    {
                        ClientSecrets = GoogleClientSecrets.FromStream(stream).Secrets,
                        HttpClientFactory = httpClientFactory,
                    },
                    Scopes,
                    "user",
                    CancellationToken.None,
                    new FileDataStore(credPath, true)).GetAwaiter().GetResult();
                Console.WriteLine("Using token store: " + credPath);
            }

            // Create Gmail API service.
            var service = new GmailService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                HttpClientFactory = httpClientFactory,
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
                Console.WriteLine($"Deleting {ids.Count} message(s):\n{string.Join("\n", ids)}");

                var delete = service.Users.Messages.BatchDelete(new BatchDeleteMessagesRequest
                {
                    Ids = ids
                }, "me");

                delete.Execute();
                Console.WriteLine("Done");
            }
        }
    }

    /// <summary>
    /// .NET's SocketsHttpHandler tries resolved addresses one at a time (no "happy eyeballs").
    /// On a network where IPv6 is advertised but black-holed, each IPv6 address costs a full
    /// ~21s TCP connect timeout; gmail.googleapis.com returns 8 AAAA records, which exceeds
    /// HttpClient's 100s timeout and surfaces as TaskCanceledException. Prefer IPv4, and cap
    /// each connect attempt so a dead address family can't stall the request.
    /// </summary>
    public class Ipv4FirstHttpClientFactory : HttpClientFactory
    {
        static readonly TimeSpan PerAddressConnectTimeout = TimeSpan.FromSeconds(5);

        protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args)
        {
            return new SocketsHttpHandler
            {
                AutomaticDecompression = args.GZipEnabled
                    ? DecompressionMethods.GZip | DecompressionMethods.Deflate
                    : DecompressionMethods.None,
                UseCookies = false,
                ConnectCallback = ConnectAsync,
            };
        }

        static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
        {
            var endPoint = context.DnsEndPoint;
            var addresses = await Dns.GetHostAddressesAsync(endPoint.Host, cancellationToken).ConfigureAwait(false);
            var ordered = addresses
                .OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
                .ToList();

            Exception lastError = null;
            foreach (var address in ordered)
            {
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attemptCts.CancelAfter(PerAddressConnectTimeout);
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(address, endPoint.Port), attemptCts.Token).ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    socket.Dispose();
                    lastError = ex;
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }

            throw new HttpRequestException($"Could not connect to {endPoint.Host}:{endPoint.Port} on any of {ordered.Count} address(es).", lastError);
        }
    }

    public static class Extensions
    {
        /// <summary>
        /// Resolves a file in the project directory (the folder containing GoogleDelete.csproj,
        /// found by walking up from the exe's folder). Falls back to the exe's folder.
        /// </summary>
        public static string ToApplicationPath(this string fileName)
        {
            return Path.Combine(AppRoot.Value, fileName);
        }

        static readonly Lazy<string> AppRoot = new Lazy<string>(() =>
        {
            var baseDir = AppContext.BaseDirectory;
            for (var dir = new DirectoryInfo(baseDir); dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "GoogleDelete.csproj")))
                    return dir.FullName;
            }
            return baseDir;
        });
    }
}

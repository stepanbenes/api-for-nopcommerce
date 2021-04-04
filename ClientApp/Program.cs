using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace ClientApp
{
	class Program
	{
		static async Task Main(string[] args)
		{
			Console.WriteLine("Types in this assembly:");
			foreach (Type t in typeof(Program).Assembly.GetTypes())
			{
				Console.WriteLine(t.FullName);
			}

			// Create api client
			var httpClient = new HttpClient { BaseAddress = new Uri(args[0]) };
			var nopApiClient = new ApiBindings.NopApi.NopApiClient(httpClient);

			Console.WriteLine("Authenticating...");
			var tokenResponse = await nopApiClient.RequestToken(Username: args[1], Password: args[2]);
			if (tokenResponse is { AccessToken: string token, TokenType: var type })
				httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(type ?? "Bearer", token);
			
			Console.WriteLine("Requesting categories...");
			var categories = await nopApiClient.GetCategories();

			if (categories?.Categories is not null)
			{
				foreach (var category in categories.Categories)
				{
					Console.WriteLine(category.ToString());
				}
			}

			Console.ReadKey();
		}
	}
}

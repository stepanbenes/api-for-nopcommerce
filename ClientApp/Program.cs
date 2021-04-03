using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace ClientApp
{
	class MyNopApiClient : ApiBindings.NopApi.NopApiClient
	{
		public MyNopApiClient(string uri)
			: base(new HttpClient { BaseAddress = new Uri(uri) })
		{ }

		public async Task Authenticate(string username, string password)
		{
			var tokenResponse = await GetToken(username, password);
			if (tokenResponse is { AccessToken: string token, TokenType: var type })
				AccessToken = new Token(token, type ?? "Bearer");
			else
				AccessToken = null;
		}
	}

	class Program
	{
		static async Task Main(string[] args)
		{
			Console.WriteLine("Types in this assembly:");
			foreach (Type t in typeof(Program).Assembly.GetTypes())
			{
				Console.WriteLine(t.FullName);
			}

			Console.WriteLine("Requesting categories...");

			var nopApiClient = new MyNopApiClient(uri: args[0]);
			await nopApiClient.Authenticate(username: args[1], password: args[2]);
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

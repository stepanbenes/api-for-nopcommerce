using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using ApiBindings.NopApi;

namespace ClientApp
{
	class Program
	{
		static async Task Main(string[] args)
		{
			//Console.WriteLine("Types in this assembly:");
			//foreach (Type t in typeof(Program).Assembly.GetTypes())
			//{
			//	Console.WriteLine(t.FullName);
			//}
			//Console.ReadKey();

			HttpClient httpClient = new HttpClient { BaseAddress = new Uri("https://localhost:5001") };
			NopApiClient nopApiClient = new NopApiClient(httpClient);
			var categories = await nopApiClient.GetApiCategories();
			if (categories?.Categories is not null)
			{
				foreach (var category in categories.Categories)
				{
					Console.WriteLine(category.ToString());
				}
			}
		}
	}
}

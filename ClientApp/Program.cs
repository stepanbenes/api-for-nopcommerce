using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using ApiBindings.NopApi.DTOs;

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

			try
			{
				var result = await nopApiClient.CreateShoppingCartItem(new ShoppingCartItemDtoDelta { ShoppingCartItem = new ShoppingCartItemDto { CustomerId = 1, ProductId = 1, Quantity = 1, ShoppingCartType = "ShoppingCart" } });

				var item = result?.ShoppingCarts?.SingleOrDefault();
				if (item != null)
				{
					_ = await nopApiClient.UpdateShoppingCartItem(new ShoppingCartItemDtoDelta { ShoppingCartItem = item }, item.Id.ToString());
				}

				result = await nopApiClient.GetShoppingCartItems(new ShoppingCartItemsParametersModel { Limit = 2, Page = 1 });
			}
			catch (ApiBindings.ApiException ex)
			{
				Console.WriteLine(ex.Message);
			}

			Console.ReadKey();
		}
	}
}

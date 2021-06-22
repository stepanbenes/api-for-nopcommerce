using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using ApiBindings.NopApi;
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
			var cookieContainer = new CookieContainer();
			using var handler = new HttpClientHandler() { CookieContainer = cookieContainer, UseCookies = true };
			using var httpClient = new HttpClient(handler) { BaseAddress = new Uri(args[0]) };

			INopApiClient nopApiClient = new NopApiClient(httpClient);

			try
			{
				Console.WriteLine("Authenticating...");

				var tokenResponse = await nopApiClient.RequestToken(new TokenRequest { Username = args[1], Password = args[2], RememberMe = true });

				if (tokenResponse is { AccessToken: string token, TokenType: var type })
				{
					nopApiClient.SetAuthorizationHeader(type ?? "Bearer", token);
					//nopApiClient.RemoveAuthorizationHeader();
				}

				Console.WriteLine("Requesting categories...");
				var categories = await nopApiClient.GetCategories();

				if (categories?.Categories is not null)
				{
					foreach (var category in categories.Categories)
					{
						Console.WriteLine(category.ToString());
					}
				}
				Console.WriteLine("Requesting countries...");
				var countries = await nopApiClient.GetCountries();

				if (countries?.Countries is not null)
				{
					foreach (var country in countries.Countries)
					{
						Console.WriteLine(country.ToString());
					}
				}

				Console.WriteLine("Adding product...");
				var createProductResult = await nopApiClient.CreateProduct(new ProductDtoDelta { Product = new ProductDto { Name = "Test product", ShortDescription = "The best product" } });

				var newProduct = createProductResult?.Products?.SingleOrDefault();

				Console.WriteLine("Creating shopping cart item...");
				var result = await nopApiClient.CreateShoppingCartItem(new ShoppingCartItemDtoDelta
				{
					ShoppingCartItem = new ShoppingCartItemDto(ShoppingCartType.ShoppingCart)
					{
						CustomerId = tokenResponse?.CustomerId,
						ProductId = newProduct?.Id,
						Quantity = 2
					}
				});

				var newShoppingCartItem = result?.ShoppingCarts?.SingleOrDefault();

				result = await nopApiClient.GetShoppingCartItems(CustomerId: tokenResponse?.CustomerId);

				if (newShoppingCartItem is not null)
				{
					Console.WriteLine("Updating shopping cart item...");
					_ = await nopApiClient.UpdateShoppingCartItem(new ShoppingCartItemDtoDelta { ShoppingCartItem = newShoppingCartItem with { Quantity = 3 }, }, newShoppingCartItem.Id.ToString());
				}

				result = await nopApiClient.GetShoppingCartItems(CustomerId: tokenResponse?.CustomerId);

				if (newShoppingCartItem is not null)
				{
					Console.WriteLine("Deleting shopping cart item...");
					await nopApiClient.DeleteShoppingCartItem(newShoppingCartItem.Id);
					//await nopApiClient.DeleteShoppingCartItems(Ids: new[] { newShoppingCartItem.Id });
				}


				result = await nopApiClient.GetShoppingCartItems(CustomerId: tokenResponse?.CustomerId);

				if (newProduct is not null)
				{
					Console.WriteLine("Deleting product...");
					await nopApiClient.DeleteProduct(newProduct.Id);
				}

				//var invoiceDocument = await nopApiClient.GetPdfInvoice(orderId: 1);
			}
			catch (ApiBindings.ApiException ex)
			{
				Console.WriteLine(ex.Message);
			}

			Console.ReadKey();
		}
	}
}

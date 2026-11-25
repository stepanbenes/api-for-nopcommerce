using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using ApiBindings;
using ApiBindings.NopApi;
using ApiBindings.NopApi.DTOs;

namespace ClientApp;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var username = Environment.GetEnvironmentVariable("NOP_API_TEST_USER");
        var password = Environment.GetEnvironmentVariable("NOP_API_TEST_PASSWORD");
        var checkout = args.Length > 1 ? CheckoutExample.Parse(args) : null;
        if (args.Length == 0 || (args.Length > 1 && checkout is null) ||
            !Uri.TryCreate(args[0], UriKind.Absolute, out var baseAddress) ||
            baseAddress.Scheme is not ("http" or "https") ||
            string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            Console.Error.WriteLine("Set NOP_API_TEST_USER and NOP_API_TEST_PASSWORD.");
            Console.Error.WriteLine("Usage: ClientApp URL [(checkout|checkout-retry) BILLING_ADDRESS_ID SHIPPING_ADDRESS_ID UUID [ATTRIBUTE_ID=VALUE]]");
            return 1;
        }

        using var handler = new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false };
        using var httpClient = new HttpClient(handler) { BaseAddress = baseAddress };
        var client = new NopApiClient(httpClient);

        try
        {
            var token = await client.RequestToken(new TokenRequest { Username = username, Password = password });
            if (token?.AccessToken is null)
            {
                Console.Error.WriteLine("The store did not return an access token.");
                return 1;
            }

            client.SetAuthorizationHeader(token.TokenType ?? "Bearer", token.AccessToken);
            if (checkout is not null)
                return await checkout.PlaceAsync(client, token.CustomerId);

            var categories = await client.GetCategories();
            var countries = await client.GetCountries();
            Console.WriteLine($"Categories returned: {categories?.Categories?.Count() ?? 0}");
            Console.WriteLine($"Countries returned: {countries?.Countries?.Count() ?? 0}");
            return 0;
        }
        catch (ApiException ex)
        {
            Console.Error.WriteLine($"API request failed with status {(int)ex.StatusCode}.");
            return 1;
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"Could not reach the API. {ex.Message}");
            return 1;
        }
    }
}

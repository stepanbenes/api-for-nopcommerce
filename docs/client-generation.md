# Verify generated API clients

Both clients use [the checked-in OpenAPI document](../ClientApp/nopapi.swagger.json). The tests use a disposable loopback server for C# and HTTPX mock transport for Python. They run without a nopCommerce source checkout or store.

## C#

With the .NET 10 SDK, run:

```sh
dotnet test ClientApp.Tests/ClientApp.Tests.csproj --configuration Release
```

The build generates the client with the repository's C# source generator. The tests exercise request serialization and response parsing through that generated client.

Use the concrete client's `CreateOrderWithResponse` method to read the response status, body, and documented headers together. Check `Headers` for `Idempotency-Replayed` or `X-Order-Follow-Up`. Ordinary `CreateOrder` calls continue to return only the order body.

## Python

With Python 3.12 or later and Bash, run:

```sh
NOP_PYTHON=python3.12 ./scripts/test-python-client.sh
```

The script installs `openapi-python-client==0.29.1` into a temporary virtual environment. It generates from the checked-in document with warnings treated as errors, compiles the package, imports every documented operation, and exercises representative requests and responses. If the documented operation count changes intentionally, update the expected count in the test.

For an optional read-only smoke test against a local installed store, set `NOP_API_TEST_URL`, `NOP_API_TEST_USER`, and `NOP_API_TEST_PASSWORD` before running the script. Use a test account with the **Api Users** role. The smoke test authenticates, reads the product count, and loads shipping checkout attributes.

## Order creation and updates

For `POST /api/orders`, use the generated `CreateOrderRequest` with an `order` containing `customer_id` and `billing_address.id`. The address must already belong to the customer. Supply `shipping_address.id` when the cart requires shipping. Both address references can use just the ID.

For `PUT /api/orders/{id}`, use `OrderDtoDelta` and send the fields you want to change. The shared order response model also permits a compact receipt containing only the committed order ID, customer ID, and store ID.

Protected endpoints require an access token and the **Api Users** role in
nopCommerce. The account also needs the permissions required by each operation.

For a guest storefront session, request a token with `guest: true`. The store
creates a guest customer and assigns the API role. Use the returned `customer_id`
when an operation requires the shopper’s ID.

```sh
curl --fail-with-body https://store.example/token \
  -H 'Content-Type: application/json' \
  --data '{"guest":true}'
```

For a registered shopper or a trusted integration, assign the API role to the
account and request a token with its credentials using `POST /token`.

```sh
curl --fail-with-body https://store.example/token \
  -H 'Content-Type: application/json' \
  --data '{"username":"api-user@example.com","password":"YOUR_PASSWORD"}'
```

Copy `access_token` from the JSON response and use it in the
`Authorization: Bearer` header when calling protected endpoints.

```sh
curl --fail-with-body https://store.example/token/check \
  -H "Authorization: Bearer $NOP_API_TOKEN"
```

Use `expires_at_utc` to determine when to request a fresh token.

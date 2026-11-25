Assign the API account the **Api Users** role in nopCommerce. The account also
needs the nopCommerce permissions required by each endpoint.

Request an access token from the store using `POST /token`.

```sh
curl --fail-with-body https://store.example/token \
  -H 'Content-Type: application/json' \
  --data '{"username":"api-user@example.com","password":"YOUR_PASSWORD"}'
```

Copy `access_token` from the JSON response and use it in the
`Authorization: Bearer` header when calling protected endpoints.

```sh
curl --fail-with-body https://store.example/api/orders/count \
  -H "Authorization: Bearer $NOP_API_TOKEN"
```

Use `expires_at_utc` to determine when to request a fresh token.

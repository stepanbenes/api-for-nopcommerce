# Run nopCommerce 5.00.1 locally with Docker

Use Docker with Compose 2.17 or later and BuildKit. From this repository, clone the upstream release into a sibling directory, generate local secrets and start both services.

```sh
git clone --depth 1 --branch release-5.00.1 https://github.com/nopSolutions/nopCommerce.git ../nopCommerce
export NOP_DB_PASSWORD="$(openssl rand -hex 24)"
export NOP_API_SIGNING_KEY="$(openssl rand -hex 32)"
docker compose up --build -d
```

Set `NOP_ROOT` to the path of an existing `release-5.00.1` checkout if you already have one. Keep the exported database password and JWT signing key available for subsequent Compose runs against these volumes. If port 5081 is occupied, set `NOP_PORT` before installation and use that port in the URLs below.

1. Open <http://127.0.0.1:5081/install> and choose a local administrator email and password.
2. Choose PostgreSQL, select **Enter raw connection string (advanced)** and enter `Host=db;Database=nopcommerce;Username=nopcommerce;Password=YOUR_NOP_DB_PASSWORD`. Use the generated database password. Compose creates the database before starting the store.
3. Leave the newsletter box unchecked. Select **Create sample data** for a store with example catalog records, then choose **Install** and wait for the store restart.
4. Under **Configuration → Local plugins**, verify that **Api plugin** is installed. If the plugin appears as pending, apply the pending changes and let the store restart.
5. Edit the administrator in **Customers → Customers** and add the **Api Users** role while keeping the existing roles. Open <http://127.0.0.1:5081/api/swagger> to try the API. Use the local administrator credentials to request a token.

`docker compose down` stops the stack. PostgreSQL data, nopCommerce installation settings, and store images persist in named volumes.

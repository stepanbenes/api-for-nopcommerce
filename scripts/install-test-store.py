import http.cookiejar
import os
from html.parser import HTMLParser
import time
import urllib.parse
import urllib.request


class InstallForm(HTMLParser):
    token = None

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if tag == "input" and attrs.get("name") == "__RequestVerificationToken":
            self.token = attrs.get("value")


base = f"http://127.0.0.1:{os.environ['NOP_PORT']}"
opener = urllib.request.build_opener(
    urllib.request.ProxyHandler({}),
    urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()),
)
for attempt in range(60):
    try:
        with opener.open(base + "/install", timeout=5) as response:
            form = InstallForm()
            form.feed(response.read().decode())
        if form.token:
            break
    except OSError:
        pass
    time.sleep(2)
else:
    raise SystemExit("The isolated nopCommerce installer did not become ready.")

password = os.environ["NOP_OPENAPI_ADMIN_PASSWORD"]
provider = os.environ.get("NOP_INSTALL_PROVIDER", "postgresql")
db_password = os.environ["NOP_DB_PASSWORD"]
if provider == "sqlserver":
    connection = f"Server=db,1433;Database=nopcommerce;User ID=sa;Password={db_password};TrustServerCertificate=True"
elif provider == "postgresql":
    connection = f"Host=db;Database=nopcommerce;Username=nopcommerce;Password={db_password}"
else:
    raise ValueError(f"Unsupported test provider: {provider}")
payload = urllib.parse.urlencode({
    "__RequestVerificationToken": form.token,
    "AdminEmail": "openapi-ci@example.test",
    "AdminPassword": password,
    "ConfirmPassword": password,
    "DataProvider": "1" if provider == "sqlserver" else "3",
    "CreateDatabaseIfNotExists": "true" if provider == "sqlserver" else "false",
    "ConnectionStringRaw": "true",
    "ConnectionString": connection,
    "InstallSampleData": os.environ["NOP_OPENAPI_RUN_TESTS"],
    "SubscribeNewsletters": "false",
}).encode()
with opener.open(base + "/install", payload, timeout=600) as response:
    if "window.location.replace(" not in response.read().decode():
        raise SystemExit("nopCommerce did not complete installation.")

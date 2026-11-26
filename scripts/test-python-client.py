import base64
import json
import os
import sys
from datetime import datetime, timezone
from importlib import import_module
from inspect import Parameter, signature
from pathlib import Path
from uuid import UUID

import httpx
from openapi_python_client.strings import snake_case

from nop_api_client import AuthenticatedClient, Client
from nop_api_client.api.cache_maintenance import clear_cache
from nop_api_client.api.categories import create_category
from nop_api_client.api.customers import set_billing_address
from nop_api_client.api.menus import add_menu_item, remove_menu_item
from nop_api_client.api.order_items import create_order_item
from nop_api_client.api.orders import create_order, get_checkout_attributes, get_orders, get_pdf_invoice, update_order
from nop_api_client.api.products import bulk_update_product_price_stock, get_products, get_products_count
from nop_api_client.api.token import request_token
from nop_api_client.models import (
    AddMenuItemRequest,
    AddressDto,
    BulkPriceStockItem,
    BulkPriceStockRequest,
    CategoryDto,
    CategoryDtoDelta,
    CheckoutAttributeSelectionDto,
    CreateOrderDto,
    CreateOrderRequest,
    ErrorsRootObject,
    MenuItemType,
    OrderBillingAddressDto,
    OrderDto,
    OrderDtoDelta,
    OrderItemDto,
    OrderItemDtoDelta,
    OrderStatus,
    ShippingStatus,
    TokenRequest,
    TokenResponse,
)


def check_operations(spec):
    operations = [(method, path, operation) for path, item in spec["paths"].items()
                  for method, operation in item.items()
                  if method in {"get", "post", "put", "delete", "patch", "head", "options", "trace"}]
    assert len(operations) == 135, f"Expected 135 documented operations, got {len(operations)}"
    modules = set()
    for method, path, operation in operations:
        module = f"nop_api_client.api.{snake_case(operation['tags'][0])}.{snake_case(operation['operationId'])}"
        assert module not in modules, f"Duplicate generated operation for {method.upper()} {path}"
        modules.add(module)
        assert callable(import_module(module).sync_detailed), f"Missing {method.upper()} {path}"
    return len(operations)


def request_to(method, path, payload=None, *, status=200, body=None, auth=True, query=(), headers=None):
    def inspect(request):
        assert request.method == method and request.url.path == path, f"Unexpected request {request.method} {request.url.path}"
        assert list(request.url.params.multi_items()) == list(query), request.url
        assert request.headers.get("Authorization") == ("Bearer fixture-token" if auth else None)
        for name, value in (headers or {}).items():
            assert request.headers.get(name) == value, f"Unexpected {name} header"
        if body is not None:
            assert request.headers["Content-Type"] == "application/json"
            assert json.loads(request.content) == body
        else:
            assert not request.content
        if isinstance(payload, str):
            return httpx.Response(status, text=payload)
        return httpx.Response(status, json=payload) if payload is not None else httpx.Response(status)
    return httpx.MockTransport(inspect)


def call(client, endpoint, transport, *args, **kwargs):
    with httpx.Client(base_url="https://store.example.test", transport=transport) as http:
        outgoing = client.get_httpx_client()
        http.headers.update(outgoing.headers)
        client.set_httpx_client(http)
        try:
            return endpoint.sync_detailed(*args, client=client, **kwargs)
        finally:
            client.set_httpx_client(outgoing)


def mock_api():
    url = "https://store.example.test"
    public = Client(base_url=url, raise_on_unexpected_status=True)
    uuid = UUID("af8bdbe5-235e-4122-b7ab-258077dc2aab")
    now = datetime(2026, 1, 2, tzinfo=timezone.utc)
    token = call(public, request_token, request_to("POST", "/token", {
        "access_token": "fixture-token", "token_type": "Bearer", "customer_id": 42,
        "customer_guid": str(uuid), "expires_at_utc": now.isoformat(),
    }, auth=False, body={"guest": True, "remember_me": False}),
        body=TokenRequest(guest=True, remember_me=False)).parsed
    assert isinstance(token, TokenResponse) and token.customer_id == 42
    assert token.customer_guid == uuid and token.expires_at_utc == now
    auth = AuthenticatedClient(base_url=url, token=token.access_token, raise_on_unexpected_status=True)

    cache_clear = call(auth, clear_cache, request_to("POST", "/api/admin/cache/clear", status=204))
    assert cache_clear.status_code == 204 and cache_clear.parsed is None
    unauthorized = call(auth, clear_cache, request_to("POST", "/api/admin/cache/clear", status=401))
    assert unauthorized.status_code == 401 and unauthorized.parsed is None

    unavailable = call(public, request_token, request_to(
        "POST", "/token", "Token signing is unavailable.", auth=False, status=503, body={"guest": True}),
        body=TokenRequest(guest=True))
    assert unavailable.status_code == 503 and unavailable.parsed == "Token signing is unavailable."

    address = call(auth, set_billing_address, request_to(
        "POST", "/api/customers/42/billingaddress", {"id": 17, "first_name": "Ada"},
        body={"first_name": "Ada"}), 42, body=AddressDto(first_name="Ada")).parsed
    assert isinstance(address, AddressDto) and address.id == 17

    products = call(auth, get_products, request_to(
        "GET", "/api/products", {"products": [{"id": 8, "price": 12.50, "published": True}]},
        query=(("Ids", "8"), ("Ids", "9"), ("Limit", "2"), ("PublishedStatus", "true"))),
        ids=[8, 9], limit=2, published_status=True).parsed.products
    assert type(products[0].id) is int and type(products[0].price) is float
    assert products[0].price == 12.50 and products[0].published is True

    bulk_items = [{"product_id": 14, "price": 31.9512}, {"product_id": 19, "stock_quantity": 0}]
    bulk = call(auth, bulk_update_product_price_stock, request_to(
        "POST", "/api/products/bulk-price-stock", {"results": [
            {"product_id": 14, "status": "updated"}, {"product_id": 19, "status": "unsupported_stock_mode"},
        ]}, body={"operation_id": str(uuid), "version": 4_294_967_296, "items": bulk_items}),
        body=BulkPriceStockRequest(operation_id=uuid, version=4_294_967_296, items=[
            BulkPriceStockItem(product_id=14, price=31.9512),
            BulkPriceStockItem(product_id=19, stock_quantity=0),
        ])).parsed
    assert [(item.product_id, item.status) for item in bulk.results] == [
        (14, "updated"), (19, "unsupported_stock_mode")]

    all_products = call(auth, get_products, request_to(
        "GET", "/api/products", {"products": [{"id": 8}]}, query=(("unpaged", "true"),)),
        unpaged=True).parsed.products
    assert all_products[0].id == 8

    category = call(auth, create_category, request_to(
        "POST", "/api/categories", {"categories": [{"id": 6, "name": "Parts"}]},
        body={"category": {"name": "Parts", "published": False}}),
        body=CategoryDtoDelta(category=CategoryDto(name="Parts", published=False))).parsed.categories[0]
    assert isinstance(category, CategoryDto) and category.id == 6

    attrs = call(auth, get_checkout_attributes, request_to("GET", "/api/checkout_attributes", {
        "checkout_attributes": [
            {"id": 3, "required": True, "choices": [{"id": 7, "name": "No", "preselected": True}]},
            {"id": 4, "condition": {"attribute_id": 3, "values": ["7"]}},
        ],
    }, query=(("shipping_required", "true"), ("include_conditional", "true"))),
        shipping_required=True, include_conditional=True).parsed.checkout_attributes
    assert attrs[0].required is True and attrs[0].choices[0].id == 7
    assert attrs[1].condition.attribute_id == 3 and attrs[1].condition.values == ["7"]

    item = OrderItemDto(product_id=8, quantity=2)
    for model, field in ((CreateOrderRequest, "order"), (CreateOrderDto, "customer_id"),
                         (CreateOrderDto, "billing_address"), (OrderBillingAddressDto, "id")):
        assert signature(model).parameters[field].default is Parameter.empty, f"{model.__name__}.{field} must be required"
    order = CreateOrderRequest(order=CreateOrderDto(
        customer_id=42, billing_address=OrderBillingAddressDto(id=17),
        order_items=[item], checkout_attributes=[CheckoutAttributeSelectionDto(id=3, values=["7"])]))
    committed = call(auth, create_order, request_to("POST", "/api/orders", {
        "orders": [{"id": 23, "customer_id": 42, "store_id": 1, "shipping_status": None}],
    }, headers={"Idempotency-Key": str(uuid)}, body={"order": {
        "customer_id": 42, "billing_address": {"id": 17},
        "order_items": [{"product_id": 8, "quantity": 2}], "checkout_attributes": [{"id": 3, "values": ["7"]}],
    }}), body=order, idempotency_key=str(uuid)).parsed.orders[0]
    assert type(committed.id) is int and committed.id == 23
    assert committed.shipping_status is None

    orders = call(auth, get_orders, request_to("GET", "/api/orders", {"orders": []}, query=(("Status", "Pending"),)),
        status=OrderStatus.PENDING)
    assert orders.parsed.orders == []

    amended = call(auth, update_order, request_to("PUT", "/api/orders/23", {
        "orders": [{"id": 23, "shipping_status": "NotYetShipped"}],
    }, body={"order": {"customer_ip": "192.0.2.1"}}), 23,
        body=OrderDtoDelta(order=OrderDto(customer_ip="192.0.2.1"))).parsed.orders[0]
    assert amended.shipping_status is ShippingStatus.NOTYETSHIPPED

    added_item = call(auth, create_order_item, request_to("POST", "/api/orders/23/items", {
        "order_items": [{"id": 39, "product_id": 8, "quantity": 2}],
    }, body={"order_item": {"product_id": 8, "quantity": 2}}), 23,
        body=OrderItemDtoDelta(order_item=item)).parsed.order_items[0]
    assert isinstance(added_item, OrderItemDto) and added_item.id == 39 and added_item.product_id == 8

    menu = AddMenuItemRequest(link_type="category", entity_id=6, display_order=4)
    for status in (200, 201):
        result = call(auth, add_menu_item, request_to("POST", "/api/menus/12/items", {
            "menu_items": [{"id": 9, "menu_id": 12, "item_type": "Category", "entity_id": 6}],
        }, status=status, body={"link_type": "category", "entity_id": 6, "display_order": 4}), 12, body=menu)
        assert result.status_code == status and result.parsed.menu_items[0].item_type is MenuItemType.CATEGORY
    removed = call(auth, remove_menu_item, request_to("DELETE", "/api/menus/12/items/9", status=204), 12, 9)
    assert removed.status_code == 204 and removed.parsed is None

    invalid = call(auth, create_category, request_to("POST", "/api/categories", {
        "errors": {"name": ["required"]},
    }, status=422, body={"category": {}}), body=CategoryDtoDelta(category=CategoryDto()))
    assert invalid.status_code == 422 and isinstance(invalid.parsed, ErrorsRootObject)
    assert invalid.parsed.errors.to_dict() == {"name": ["required"]}

    encoded = base64.b64encode(b"%PDF-1.7").decode()
    invoice = call(auth, get_pdf_invoice, request_to("GET", "/api/orders/23/pdf-invoice", {
        "FileName": "order.pdf", "MimeType": "application/pdf", "Content": encoded, "CreatedAt": now.isoformat(),
    }), 23).parsed
    assert invoice.file_name == "order.pdf" and invoice.mime_type == "application/pdf"
    assert base64.b64decode(invoice.content) == b"%PDF-1.7" and invoice.created_at == now


def live_api(url):
    credentials = TokenRequest(username=os.environ["NOP_API_TEST_USER"], password=os.environ["NOP_API_TEST_PASSWORD"])
    with Client(base_url=url, raise_on_unexpected_status=True) as public:
        login = request_token.sync_detailed(client=public, body=credentials)
    assert login.status_code == 200 and isinstance(login.parsed, TokenResponse), "Local test login failed"
    with AuthenticatedClient(base_url=url, token=login.parsed.access_token, raise_on_unexpected_status=True) as auth:
        products = get_products_count.sync_detailed(client=auth)
        assert products.status_code == 200 and type(products.parsed.count) is int
        attributes = get_checkout_attributes.sync_detailed(client=auth, shipping_required=True)
        assert attributes.status_code == 200 and isinstance(attributes.parsed.checkout_attributes, list)
    print("Python client authenticated and read product count and checkout attributes from the installed store.")


if __name__ == "__main__":
    count = check_operations(json.loads(Path(sys.argv[1]).read_text()))
    mock_api()
    print(f"Python client imported all {count} operations and passed mock wire checks.")
    if url := os.getenv("NOP_API_TEST_URL"):
        live_api(url)

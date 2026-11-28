using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Core.Infrastructure;
using Nop.Data;
using Nop.Plugin.Api.Attributes;
using Nop.Plugin.Api.Authorization.Attributes;
using Nop.Plugin.Api.Delta;
using Nop.Plugin.Api.DTO;
using Nop.Plugin.Api.DTO.Customers;
using Nop.Plugin.Api.DTO.Errors;
using Nop.Plugin.Api.Factories;
using Nop.Plugin.Api.Helpers;
using Nop.Plugin.Api.Infrastructure;
using Nop.Plugin.Api.JSON.ActionResults;
using Nop.Plugin.Api.JSON.Serializers;
using Nop.Plugin.Api.MappingExtensions;
using Nop.Plugin.Api.ModelBinders;
using Nop.Plugin.Api.Models.CustomersParameters;
using Nop.Plugin.Api.Services;
using Nop.Services.Authentication;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Directory;
using Nop.Services.Discounts;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Media;
using Nop.Services.Messages;
using Nop.Services.Security;
using Nop.Services.Stores;
using System.Net;

namespace Nop.Plugin.Api.Controllers
{
    [AuthorizePermission(StandardPermission.Customers.CUSTOMERS_CREATE_EDIT_DELETE)]
    public class CustomersController : BaseApiController
    {
        private readonly ICountryService _countryService;
        private readonly INopDataProvider _dataProvider;
        private readonly ICustomerApiService _customerApiService;
        private readonly IEncryptionService _encryptionService;
        private readonly IFactory<Customer> _factory;
        private readonly ILanguageService _languageService;
        private readonly IPermissionService _permissionService;
        private readonly IAddressService _addressService;
        private readonly IAuthenticationService _authenticationService;
        private readonly ICurrencyService _currencyService;
        private readonly INewsLetterSubscriptionService _newsLetterSubscriptionService;

        // We resolve the customer settings this way because of the tests.
        // The auto mocking does not support concreate types as dependencies. It supports only interfaces.
        private CustomerSettings _customerSettings;

        public CustomersController(
          ICustomerApiService customerApiService,
          IJsonFieldsSerializer jsonFieldsSerializer,
          IAclService aclService,
          ICustomerService customerService,
          IStoreMappingService storeMappingService,
          IStoreService storeService,
          IDiscountService discountService,
          ICustomerActivityService customerActivityService,
          ILocalizationService localizationService,
          IEncryptionService encryptionService,
          IFactory<Customer> factory,
          ICountryService countryService,
          INewsLetterSubscriptionService newsLetterSubscriptionService,
          IPictureService pictureService, ILanguageService languageService,
          IPermissionService permissionService,
          IAddressService addressService,
          IAuthenticationService authenticationService,
          ICurrencyService currencyService,
          INopDataProvider dataProvider) :
          base(jsonFieldsSerializer, aclService, customerService, storeMappingService, storeService, discountService, customerActivityService,
             localizationService, pictureService)
        {
            _customerApiService = customerApiService;
            _dataProvider = dataProvider;
            _factory = factory;
            _countryService = countryService;
            _newsLetterSubscriptionService = newsLetterSubscriptionService;
            _languageService = languageService;
            _permissionService = permissionService;
            _addressService = addressService;
            _authenticationService = authenticationService;
            _currencyService = currencyService;
            _encryptionService = encryptionService;
        }

        private CustomerSettings CustomerSettings
        {
            get
            {
                if (_customerSettings == null)
                {
                    _customerSettings = EngineContext.Current.Resolve<CustomerSettings>();
                }

                return _customerSettings;
            }
        }

        /// <summary>
        ///     Retrieve all customers of a shop
        /// </summary>
        /// <response code="200">OK</response>
        /// <response code="400">Bad request</response>
        /// <response code="401">Unauthorized</response>
        [HttpGet]
        [Route("/api/customers", Name = "GetCustomers")]
        [ApiAudience(ApiAudience.BackOffice)]
        [ProducesResponseType(typeof(CustomersRootObject), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Unauthorized)]
        [GetRequestsErrorInterceptorActionFilter]
        [UnpagedCollection]
        public async Task<IActionResult> GetCustomers([FromQuery] CustomersParametersModel parameters)
        {
            if (parameters.Limit < Constants.Configurations.MinLimit || parameters.Limit > Constants.Configurations.MaxLimit)
            {
                return Error(HttpStatusCode.BadRequest, "limit", "Invalid limit parameter");
            }

            if (parameters.Page < Constants.Configurations.DefaultPageValue)
            {
                return Error(HttpStatusCode.BadRequest, "page", "Invalid request parameters");
            }

            var allCustomers = await _customerApiService.GetCustomersDtosAsync(parameters.CreatedAtMin, parameters.CreatedAtMax, GetCollectionLimit(parameters.Limit), parameters.Page, parameters.SinceId);

            var customersRootObject = new CustomersRootObject
            {
                Customers = allCustomers
            };

            var json = JsonFieldsSerializer.Serialize(customersRootObject, parameters.Fields);

            return new RawJsonActionResult(json);
        }

        /// <summary>
        ///     Retrieve customer by spcified id
        /// </summary>
        /// <param name="id">Id of the customer</param>
        /// <param name="fields">Fields from the customer you want your json to contain</param>
        /// <response code="200">OK</response>
        /// <response code="404">Not Found</response>
        /// <response code="401">Unauthorized</response>
        [HttpGet]
        [Route("/api/customers/me", Name = "GetCurrentCustomer")]
        [ApiAudience(ApiAudience.BackOffice)]
        [AuthorizePermission(StandardPermission.Customers.CUSTOMERS_CREATE_EDIT_DELETE, ignore: true)] // turn off all permission authorizations, access to this action is allowed to all authenticated customers
        [ProducesResponseType(typeof(CustomersRootObject), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.NotFound)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Unauthorized)]
        [GetRequestsErrorInterceptorActionFilter]
        public async Task<IActionResult> GetCurrentCustomer([FromQuery] string fields)
        {
            var customerEntity = await _authenticationService.GetAuthenticatedCustomerAsync();

            if (customerEntity is null)
            {
                return Error(HttpStatusCode.Unauthorized);
            }

            var customerDto = await _customerApiService.GetCustomerByIdAsync(customerEntity.Id);

            if (customerDto == null)
            {
                return Error(HttpStatusCode.NotFound, "customer", "not found");
            }

            var customersRootObject = new CustomersRootObject();
            customersRootObject.Customers.Add(customerDto);

            var json = JsonFieldsSerializer.Serialize(customersRootObject, fields ?? "");

            return new RawJsonActionResult(json);
        }

        /// <summary>
        ///     Retrieve customer by spcified id
        /// </summary>
        /// <param name="id">Id of the customer</param>
        /// <param name="fields">Fields from the customer you want your json to contain</param>
        /// <response code="200">OK</response>
        /// <response code="404">Not Found</response>
        /// <response code="401">Unauthorized</response>
        [HttpGet]
        [Route("/api/customers/{id}", Name = "GetCustomerById")]
        [ApiAudience(ApiAudience.BackOffice)]
        [ProducesResponseType(typeof(CustomersRootObject), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.NotFound)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Unauthorized)]
        [GetRequestsErrorInterceptorActionFilter]
        public async Task<IActionResult> GetCustomerById([FromRoute] int id, [FromQuery] string fields = "")
        {
            if (id <= 0)
            {
                return Error(HttpStatusCode.BadRequest, "id", "invalid id");
            }

            var customer = await _customerApiService.GetCustomerByIdAsync(id);

            if (customer == null)
            {
                return Error(HttpStatusCode.NotFound, "customer", "not found");
            }

            var customersRootObject = new CustomersRootObject();
            customersRootObject.Customers.Add(customer);

            var json = JsonFieldsSerializer.Serialize(customersRootObject, fields);

            return new RawJsonActionResult(json);
        }


        /// <summary>
        ///     Get a count of all customers
        /// </summary>
        /// <response code="200">OK</response>
        /// <response code="401">Unauthorized</response>
        [HttpGet]
        [Route("/api/customers/count", Name = "GetCustomersCount")]
        [ApiAudience(ApiAudience.BackOffice)]
        [ProducesResponseType(typeof(CustomersCountRootObject), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Unauthorized)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.BadRequest)]
        public async Task<IActionResult> GetCustomersCount()
        {
            var allCustomersCount = await _customerApiService.GetCustomersCountAsync();

            var customersCountRootObject = new CustomersCountRootObject
            {
                Count = allCustomersCount
            };

            return Ok(customersCountRootObject);
        }

        /// <summary>
        ///     Search for customers matching supplied query
        /// </summary>
        /// <response code="200">OK</response>
        /// <response code="400">Bad Request</response>
        /// <response code="401">Unauthorized</response>
        [HttpGet]
        [Route("/api/customers/search", Name = "SearchCustomers")]
        [ApiAudience(ApiAudience.BackOffice)]
        [ProducesResponseType(typeof(CustomersRootObject), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Unauthorized)]
        [UnpagedCollection]
        public async Task<IActionResult> Search([FromQuery] CustomersSearchParametersModel parameters)
        {
            if (parameters.Limit < Constants.Configurations.MinLimit || parameters.Limit > Constants.Configurations.MaxLimit)
            {
                return Error(HttpStatusCode.BadRequest, "limit", "Invalid limit parameter");
            }

            if (parameters.Page <= 0)
            {
                return Error(HttpStatusCode.BadRequest, "page", "Invalid page parameter");
            }

            IList<CustomerDto> customersDto;
            try
            {
                customersDto = await _customerApiService.SearchAsync(parameters.Query, parameters.Order, parameters.Page, GetCollectionLimit(parameters.Limit));
            }
            catch (ArgumentException validationError) when (validationError.ParamName == "order")
            {
                return Error(HttpStatusCode.BadRequest, "order", "Use a customer field followed by an optional asc or desc.");
            }

            var customersRootObject = new CustomersRootObject
            {
                Customers = customersDto
            };

            var json = JsonFieldsSerializer.Serialize(customersRootObject, parameters.Fields);

            return new RawJsonActionResult(json);
        }

        [HttpPost]
        [Route("/api/customers", Name = "CreateCustomer")]
        [ApiAudience(ApiAudience.BackOffice)]
        [ProducesResponseType(typeof(CustomersRootObject), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ErrorsRootObject), 422)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Unauthorized)]
        public async Task<IActionResult> CreateCustomer(
          [FromBody]
      [ModelBinder(typeof(JsonModelBinder<CustomerDto>))]
      Delta<CustomerDto> customerDelta)
        {
            // Here we display the errors if the validation has failed at some point.
            if (!ModelState.IsValid)
            {
                return Error();
            }

            if (customerDelta.Dto.Id != 0 || customerDelta.Dto.CustomerGuid != Guid.Empty)
                return Error(HttpStatusCode.BadRequest, "customer", "customer identity is assigned by the store");

            if (customerDelta.Dto.Addresses.Any(address => address.Id != 0))
                return Error(HttpStatusCode.BadRequest, "addresses", "new addresses must omit id");

            var newCustomer = await _factory.InitializeAsync();
            var customerGuid = newCustomer.CustomerGuid;
            customerDelta.Merge(newCustomer);
            newCustomer.CustomerGuid = customerGuid;

            using (var transaction = _dataProvider.CreateTransactionScope())
            {
                var (roles, roleError) = await ValidateRolesAsync(customerDelta, newCustomer, [], creating: true);
                if (roleError is not null)
                    return roleError;

                var identityError = await ValidateLoginIdentityAsync(customerDelta, newCustomer);
                if (identityError is not null)
                    return identityError;

                await CustomerService.InsertCustomerAsync(newCustomer);
                await SaveAddressesAsync(customerDelta, newCustomer, new Dictionary<int, Address>());
                await ApplyRolesAsync(newCustomer, [], roles);

                if (!string.IsNullOrWhiteSpace(customerDelta.Dto.Password))
                    await AddPasswordAsync(customerDelta.Dto.Password, newCustomer);

                transaction.Complete();
            }

            // Preparing the result dto of the new customer
            // We do not prepare the shopping cart items because we have a separate endpoint for them.
            var newCustomerDto = newCustomer.ToDto();
            newCustomerDto.Addresses = (await CustomerService.GetAddressesByCustomerIdAsync(newCustomer.Id)).Select(address => address.ToDto()).ToList();
            newCustomerDto.RoleIds = (await CustomerService.GetCustomerRolesAsync(newCustomer, showHidden: true)).Select(role => role.Id).ToList();

            // This is needed because the entity framework won't populate the navigation properties automatically
            // and the country will be left null. So we do it by hand here.
            await PopulateAddressCountryNamesAsync(newCustomerDto);

            // Set the fist and last name separately because they are not part of the customer entity, but are saved in the generic attributes.
            newCustomerDto.FirstName = customerDelta.Dto.FirstName;
            newCustomerDto.LastName = customerDelta.Dto.LastName;

            newCustomerDto.LanguageId = customerDelta.Dto.LanguageId;
            newCustomerDto.CurrencyId = customerDelta.Dto.CurrencyId;

            //activity log
            await CustomerActivityService.InsertActivityAsync("AddNewCustomer", await LocalizationService.GetResourceAsync("ActivityLog.AddNewCustomer"), newCustomer);

            var customersRootObject = new CustomersRootObject();

            customersRootObject.Customers.Add(newCustomerDto);

            var json = JsonFieldsSerializer.Serialize(customersRootObject, string.Empty);

            return new RawJsonActionResult(json);
        }

        [HttpPut]
        [Route("/api/customers/{id}", Name = "UpdateCustomer")]
        [ApiAudience(ApiAudience.BackOffice)]
        [ProducesResponseType(typeof(CustomersRootObject), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ErrorsRootObject), 422)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.NotFound)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Unauthorized)]
        public async Task<IActionResult> UpdateCustomer(
          [FromBody]
      [ModelBinder(typeof(JsonModelBinder<CustomerDto>))]
      Delta<CustomerDto> customerDelta)
        {
            // Here we display the errors if the validation has failed at some point.
            if (!ModelState.IsValid)
            {
                return Error();
            }

            Customer currentCustomer;
            using (var transaction = _dataProvider.CreateTransactionScope())
            {
                currentCustomer = await _customerApiService.GetCustomerEntityByIdAsync(customerDelta.Dto.Id);
                if (currentCustomer is null)
                    return Error(HttpStatusCode.NotFound, "customer", "not found");

                if (customerDelta.HasProperty(nameof(CustomerDto.CustomerGuid)) && customerDelta.Dto.CustomerGuid != currentCustomer.CustomerGuid)
                    return Error(HttpStatusCode.BadRequest, "customer_guid", "customer identity cannot change");

                var existingRoles = await CustomerService.GetCustomerRolesAsync(currentCustomer, showHidden: true);
                customerDelta.Merge(currentCustomer);
                var (roles, roleError) = await ValidateRolesAsync(customerDelta, currentCustomer, existingRoles);
                if (roleError is not null)
                    return roleError;

                var identityError = await ValidateLoginIdentityAsync(customerDelta, currentCustomer);
                if (identityError is not null)
                    return identityError;

                var addresses = customerDelta.Dto.Addresses.Count == 0
                    ? new Dictionary<int, Address>()
                    : (await CustomerService.GetAddressesByCustomerIdAsync(currentCustomer.Id)).ToDictionary(address => address.Id);
                if (customerDelta.Dto.Addresses.Any(address => address.Id != 0 && !addresses.ContainsKey(address.Id)))
                    return Error(HttpStatusCode.BadRequest, "addresses", "address id does not belong to the customer");

                await CustomerService.UpdateCustomerAsync(currentCustomer);
                await SaveAddressesAsync(customerDelta, currentCustomer, addresses);

                if (customerDelta.HasProperty(nameof(CustomerDto.RoleIds)))
                    await ApplyRolesAsync(currentCustomer, existingRoles, roles);

                if (!string.IsNullOrWhiteSpace(customerDelta.Dto.Password))
                    await AddPasswordAsync(customerDelta.Dto.Password, currentCustomer);

                transaction.Complete();
            }

            // Preparing the result dto of the new customer
            // We do not prepare the shopping cart items because we have a separate endpoint for them.
            var updatedCustomer = currentCustomer.ToDto();
            updatedCustomer.Addresses = (await CustomerService.GetAddressesByCustomerIdAsync(currentCustomer.Id)).Select(address => address.ToDto()).ToList();
            updatedCustomer.RoleIds = (await CustomerService.GetCustomerRolesAsync(currentCustomer, showHidden: true)).Select(role => role.Id).ToList();

            // This is needed because the entity framework won't populate the navigation properties automatically
            // and the country name will be left empty because the mapping depends on the navigation property
            // so we do it by hand here.
            await PopulateAddressCountryNamesAsync(updatedCustomer);


            //activity log
            await CustomerActivityService.InsertActivityAsync("UpdateCustomer", await LocalizationService.GetResourceAsync("ActivityLog.UpdateCustomer"), currentCustomer);

            var customersRootObject = new CustomersRootObject();

            customersRootObject.Customers.Add(updatedCustomer);

            var json = JsonFieldsSerializer.Serialize(customersRootObject, string.Empty);

            return new RawJsonActionResult(json);
        }

        [HttpDelete]
        [Route("/api/customers/{id}", Name = "DeleteCustomer")]
        [ApiAudience(ApiAudience.BackOffice)]
        [GetRequestsErrorInterceptorActionFilter]
        [ProducesResponseType(typeof(void), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.NotFound)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Unauthorized)]
        public async Task<IActionResult> DeleteCustomer([FromRoute] int id)
        {
            if (id <= 0)
            {
                return Error(HttpStatusCode.BadRequest, "id", "invalid id");
            }

            Customer customer;
            using (var transaction = _dataProvider.CreateTransactionScope())
            {
                customer = await _customerApiService.GetCustomerEntityByIdAsync(id);
                if (customer is null)
                    return Error(HttpStatusCode.NotFound, "customer", "not found");

                var adminRole = (await CustomerService.GetCustomerRolesAsync(customer, showHidden: true))
                    .FirstOrDefault(role => role.SystemName == NopCustomerDefaults.AdministratorsRoleName);
                if (adminRole is not null)
                {
                    if (!await IsActingAdministratorAsync())
                        return Error(HttpStatusCode.Forbidden, "customer", "only an administrator may delete an administrator");

                    if (!await AnotherActiveAdministratorExistsAsync(customer.Id, adminRole.Id))
                        return Error(HttpStatusCode.BadRequest, "customer", "at least one active administrator must remain");
                }

                await CustomerService.DeleteCustomerAsync(customer);
                transaction.Complete();
            }

            //remove newsletter subscription (if exists)
            foreach (var store in await StoreService.GetAllStoresAsync())
            {
                var subscriptions = await _newsLetterSubscriptionService.GetNewsLetterSubscriptionsByEmailAsync(customer.Email, store.Id);
                foreach (var subscription in subscriptions)
                {
                    await _newsLetterSubscriptionService.DeleteNewsLetterSubscriptionAsync(subscription);
                }
            }

            //activity log
            await CustomerActivityService.InsertActivityAsync("DeleteCustomer", await LocalizationService.GetResourceAsync("ActivityLog.DeleteCustomer"), customer);

            return new RawJsonActionResult("{}");
        }

        [HttpPost]
        [Route("api/customers/{customerId}/billingaddress", Name = "SetBillingAddress")]
        [ApiAudience(ApiAudience.Both)]
        [AuthorizePermission(StandardPermission.Customers.CUSTOMERS_CREATE_EDIT_DELETE, ignore: true)]
        [GetRequestsErrorInterceptorActionFilter]
        [ProducesResponseType(typeof(AddressDto), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Unauthorized)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Forbidden)]
        public async Task<IActionResult> SetBillingAddress([FromRoute] int customerId, [FromBody] AddressDto newAddress)
        {
            // TODO: add address validation via model binder
            if (!await CheckPermissions(customerId))
            {
                return AccessDenied();
            }
            var customer = await CustomerService.GetCustomerByIdAsync(customerId);
            if (customer is null)
            {
                return Error(HttpStatusCode.NotFound, "customerId", $"Customer with id {customerId} not found");
            }
            var address = await InsertNewCustomerAddressIfDoesNotExist(customer, newAddress);
            customer.BillingAddressId = address.Id;
            await CustomerService.UpdateCustomerAsync(customer);
            return Ok(address.ToDto());
        }

        [HttpPost]
        [Route("api/customers/{customerId}/shippingaddress", Name = "SetShippingAddress")]
        [ApiAudience(ApiAudience.Both)]
        [AuthorizePermission(StandardPermission.Customers.CUSTOMERS_CREATE_EDIT_DELETE, ignore: true)]
        [GetRequestsErrorInterceptorActionFilter]
        [ProducesResponseType(typeof(AddressDto), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Unauthorized)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Forbidden)]
        public async Task<IActionResult> SetShippingAddress([FromRoute] int customerId, [FromBody] AddressDto newAddress)
        {
            // TODO: add address validation via model binder
            if (!await CheckPermissions(customerId))
            {
                return AccessDenied();
            }
            var customer = await CustomerService.GetCustomerByIdAsync(customerId);
            if (customer is null)
            {
                return Error(HttpStatusCode.NotFound, "customerId", $"Customer with id {customerId} not found");
            }
            var address = await InsertNewCustomerAddressIfDoesNotExist(customer, newAddress);
            customer.ShippingAddressId = address.Id;
            await CustomerService.UpdateCustomerAsync(customer);
            return Ok(address.ToDto());
        }

        #region Private methods

        private async Task<Address> InsertNewCustomerAddressIfDoesNotExist(Customer customer, AddressDto newAddress)
        {
            var newAddressEntity = newAddress.ToEntity();

            //try to find an address with the same values (don't duplicate records)
            var allCustomerAddresses = await CustomerService.GetAddressesByCustomerIdAsync(customer.Id);
            var address = _addressService.FindAddress(allCustomerAddresses.ToList(),
              newAddressEntity.FirstName, newAddressEntity.LastName, newAddressEntity.PhoneNumber,
              newAddressEntity.Email, newAddressEntity.FaxNumber, newAddressEntity.Company,
              newAddressEntity.Address1, newAddressEntity.Address2, newAddressEntity.City,
              newAddressEntity.County, newAddressEntity.StateProvinceId, newAddressEntity.ZipPostalCode,
              newAddressEntity.CountryId, newAddressEntity.CustomAttributes);

            if (address is null)
            {
                //address is not found. let's create a new one
                address = newAddressEntity;
                address.CreatedOnUtc = DateTime.UtcNow;

                //some validation
                if (address.CountryId == 0)
                    address.CountryId = null;
                if (address.StateProvinceId == 0)
                    address.StateProvinceId = null;

                await _addressService.InsertAddressAsync(address);

                await CustomerService.InsertCustomerAddressAsync(customer, address);
            }

            return address;
        }

        private async Task<bool> CheckPermissions(int customerId)
        {
            var currentCustomer = await _authenticationService.GetAuthenticatedCustomerAsync();
            if (currentCustomer is null)
                return false; // should not happen
            if (currentCustomer.Id == customerId)
            {
                return true;
            }
            // if I want to handle other customer's info, check admin permission
            return await _permissionService.AuthorizeAsync(StandardPermission.Customers.CUSTOMERS_CREATE_EDIT_DELETE, currentCustomer);
        }



        private async Task<(IList<CustomerRole> Roles, IActionResult Error)> ValidateRolesAsync(
            Delta<CustomerDto> delta, Customer customer, IList<CustomerRole> existingRoles, bool creating = false)
        {
            var replacesRoles = creating || delta.HasProperty(nameof(CustomerDto.RoleIds));
            var requestedRoles = existingRoles;
            if (replacesRoles)
            {
                var requestedIds = delta.Dto.RoleIds.ToHashSet();
                var availableRoles = await CustomerService.GetAllCustomerRolesAsync(showHidden: true);
                requestedRoles = availableRoles.Where(role => requestedIds.Contains(role.Id)).ToList();
                if (requestedRoles.Count != requestedIds.Count)
                    return (null, Error((HttpStatusCode)422, "role_ids", "one or more customer roles do not exist"));

                var isGuest = requestedRoles.Any(role => role.Active && role.SystemName == NopCustomerDefaults.GuestsRoleName);
                var isRegistered = requestedRoles.Any(role => role.Active && role.SystemName == NopCustomerDefaults.RegisteredRoleName);
                if (isGuest == isRegistered)
                    return (null, Error((HttpStatusCode)422, "role_ids", "an active Guests or Registered role is required, and the two roles are mutually exclusive"));

                var existingIds = existingRoles.Select(role => role.Id).ToHashSet();
                var requiresAcl = requestedRoles.Any(role => !existingIds.Contains(role.Id) && role.SystemName != NopCustomerDefaults.RegisteredRoleName)
                    || existingIds.Except(requestedIds).Any();
                if (requiresAcl)
                {
                    var actor = await _authenticationService.GetAuthenticatedCustomerAsync();
                    if (actor is null || !await _permissionService.AuthorizeAsync(StandardPermission.Configuration.MANAGE_ACL, actor))
                        return (null, Error(HttpStatusCode.Forbidden, "role_ids", "managing customer roles requires permission to manage access control"));
                }
            }

            var existingAdminRole = existingRoles.FirstOrDefault(role => role.SystemName == NopCustomerDefaults.AdministratorsRoleName);
            var requestedAdminRole = requestedRoles.FirstOrDefault(role => role.SystemName == NopCustomerDefaults.AdministratorsRoleName);
            if (existingAdminRole is not null || requestedAdminRole is not null)
            {
                if (!await IsActingAdministratorAsync())
                    return (null, Error(HttpStatusCode.Forbidden, "customer", "only an administrator may manage administrators"));

                var retainsAdminAccess = requestedAdminRole is { Active: true } && customer.Active && !customer.Deleted
                    && HasUsableLoginIdentity(customer)
                    && requestedRoles.Any(role => role.Active && role.SystemName == NopCustomerDefaults.RegisteredRoleName);
                if (existingAdminRole is not null && !retainsAdminAccess
                    && !await AnotherActiveAdministratorExistsAsync(customer.Id, existingAdminRole.Id))
                    return (null, Error(HttpStatusCode.BadRequest, "customer", "at least one active administrator must remain"));

                if (requestedAdminRole is not null && customer.VendorId > 0)
                    return (null, Error(HttpStatusCode.BadRequest, "role_ids", "a vendor cannot be an administrator"));
            }

            return (requestedRoles, null);
        }

        private async Task<bool> IsActingAdministratorAsync()
        {
            var actor = await _authenticationService.GetAuthenticatedCustomerAsync();
            return actor is not null && await CustomerService.IsAdminAsync(actor);
        }

        private async Task<bool> AnotherActiveAdministratorExistsAsync(int customerId, int adminRoleId)
        {
            var admins = await CustomerService.GetAllCustomersAsync(customerRoleIds: [adminRoleId], isActive: true);
            foreach (var admin in admins.Where(admin => admin.Id != customerId && HasUsableLoginIdentity(admin)))
            {
                if (await CustomerService.IsRegisteredAsync(admin))
                    return true;
            }

            return false;
        }

        private bool HasUsableLoginIdentity(Customer customer) =>
            CommonHelper.IsValidEmail(customer.Email)
            && (!CustomerSettings.UsernamesEnabled || !string.IsNullOrWhiteSpace(customer.Username));

        private async Task<IActionResult> ValidateLoginIdentityAsync(Delta<CustomerDto> delta, Customer customer)
        {
            if (delta.HasProperty(nameof(CustomerDto.Email)))
            {
                var owner = await CustomerService.GetCustomerByEmailAsync(customer.Email);
                if (owner is not null && owner.Id != customer.Id)
                    return Error(HttpStatusCode.BadRequest, "email", "email is already registered");
            }

            if (delta.HasProperty(nameof(CustomerDto.Username)) && CustomerSettings.UsernamesEnabled)
            {
                var owner = await CustomerService.GetCustomerByUsernameAsync(customer.Username);
                if (owner is not null && owner.Id != customer.Id)
                    return Error(HttpStatusCode.BadRequest, "username", "username is already registered");
            }

            return null;
        }

        private async Task ApplyRolesAsync(Customer customer, IList<CustomerRole> existingRoles, IList<CustomerRole> requestedRoles)
        {
            var existingIds = existingRoles.Select(role => role.Id).ToHashSet();
            var requestedIds = requestedRoles.Select(role => role.Id).ToHashSet();
            foreach (var role in requestedRoles.Where(role => !existingIds.Contains(role.Id)))
            {
                await CustomerService.AddCustomerRoleMappingAsync(new CustomerCustomerRoleMapping
                {
                    CustomerId = customer.Id,
                    CustomerRoleId = role.Id
                });
            }

            foreach (var role in existingRoles.Where(role => !requestedIds.Contains(role.Id)))
                await CustomerService.RemoveCustomerRoleMappingAsync(customer, role);
        }

        private async Task SaveAddressesAsync(Delta<CustomerDto> delta, Customer customer, IDictionary<int, Address> existingAddresses)
        {
            foreach (var passedAddress in delta.Dto.Addresses)
            {
                if (existingAddresses.TryGetValue(passedAddress.Id, out var existingAddress))
                {
                    delta.Merge(passedAddress, existingAddress);
                    await _addressService.UpdateAddressAsync(existingAddress);
                }
                else
                {
                    var newAddress = passedAddress.ToEntity();
                    newAddress.CreatedOnUtc = passedAddress.CreatedOnUtc ?? DateTime.UtcNow;
                    if (newAddress.CountryId == 0)
                        newAddress.CountryId = null;
                    if (newAddress.StateProvinceId == 0)
                        newAddress.StateProvinceId = null;

                    await _addressService.InsertAddressAsync(newAddress);
                    await CustomerService.InsertCustomerAddressAsync(customer, newAddress);
                }
            }
        }

        private async Task PopulateAddressCountryNamesAsync(CustomerDto newCustomerDto)
        {
            foreach (var address in newCustomerDto.Addresses)
            {
                await SetCountryNameAsync(address);
            }

            if (newCustomerDto.BillingAddress != null)
            {
                await SetCountryNameAsync(newCustomerDto.BillingAddress);
            }

            if (newCustomerDto.ShippingAddress != null)
            {
                await SetCountryNameAsync(newCustomerDto.ShippingAddress);
            }
        }

        private async Task SetCountryNameAsync(AddressDto address)
        {
            if (string.IsNullOrEmpty(address.CountryName) && address.CountryId.HasValue)
            {
                var country = await _countryService.GetCountryByIdAsync(address.CountryId.Value);
                address.CountryName = country.Name;
            }
        }

        private async Task AddPasswordAsync(string newPassword, Customer customer)
        {
            var customerPassword = new CustomerPassword
            {
                CustomerId = customer.Id,
                PasswordFormat = CustomerSettings.DefaultPasswordFormat,
                CreatedOnUtc = DateTime.UtcNow
            };

            switch (CustomerSettings.DefaultPasswordFormat)
            {
                case PasswordFormat.Clear:
                    {
                        customerPassword.Password = newPassword;
                    }
                    break;
                case PasswordFormat.Encrypted:
                    {
                        customerPassword.Password = _encryptionService.EncryptText(newPassword);
                    }
                    break;
                case PasswordFormat.Hashed:
                    {
                        var saltKey = _encryptionService.CreateSaltKey(5);
                        customerPassword.PasswordSalt = saltKey;
                        customerPassword.Password = _encryptionService.CreatePasswordHash(newPassword, saltKey, CustomerSettings.HashedPasswordFormat);
                    }
                    break;
            }

            await CustomerService.InsertCustomerPasswordAsync(customerPassword);
        }

        #endregion
    }
}

using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Plugin.Api.Configuration;
using Nop.Plugin.Api.Domain;
using Nop.Plugin.Api.Infrastructure;
using Nop.Plugin.Api.Models.Authentication;
using Nop.Services.Customers;
using Nop.Services.Logging;

namespace Nop.Plugin.Api.Controllers
{
    [AllowAnonymous]
    public class TokenController : Controller
    {
        private readonly ApiConfiguration _apiConfiguration;
        private readonly ApiSettings _apiSettings;
        private readonly ICustomerActivityService _customerActivityService;
        private readonly IWorkContext _workContext;
        private readonly ICustomerRegistrationService _customerRegistrationService;
        private readonly ICustomerService _customerService;
        private readonly CustomerSettings _customerSettings;

        public TokenController(
            ICustomerService customerService,
            ICustomerRegistrationService customerRegistrationService,
            ICustomerActivityService customerActivityService,
            IWorkContext workContext,
            CustomerSettings customerSettings,
            ApiSettings apiSettings,
            ApiConfiguration apiConfiguration)
        {
            _customerService = customerService;
            _customerRegistrationService = customerRegistrationService;
            _customerActivityService = customerActivityService;
            _workContext = workContext;
            _customerSettings = customerSettings;
            _apiSettings = apiSettings;
            _apiConfiguration = apiConfiguration;
        }

        [HttpGet]
        [Route("/token", Name = "RequestToken")]
        [ProducesResponseType(typeof(TokenResponse), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Forbidden)]
        public async Task<IActionResult> Create([FromBody] TokenRequest model)
        {
            Customer customer;

            if (model.Guest)
            {
                customer = await _workContext.GetCurrentCustomerAsync();

                if (!await _customerService.IsGuestAsync(customer))
                {
                    customer = await _customerService.InsertGuestCustomerAsync();
                    await _workContext.SetCurrentCustomerAsync(customer);
                }

                if (!await _customerService.IsInCustomerRoleAsync(customer, Constants.Roles.ApiRoleSystemName))
                {
                    //add to 'ApiUserRole' role if not yet present
                    var apiRole = await _customerService.GetCustomerRoleBySystemNameAsync(Constants.Roles.ApiRoleSystemName);
                    if (apiRole == null)
                        throw new InvalidOperationException($"'{Constants.Roles.ApiRoleSystemName}' role could not be loaded");
                    await _customerService.AddCustomerRoleMappingAsync(new CustomerCustomerRoleMapping { CustomerId = customer.Id, CustomerRoleId = apiRole.Id });
                }
            }
            else
            {
                if (string.IsNullOrEmpty(model.Username))
                {
                    return BadRequest("Missing username");
                }

                if (string.IsNullOrEmpty(model.Password))
                {
                    return BadRequest("Missing password");
                }

                customer = await LoginAsync(model.Username, model.Password, model.RememberMe);

                if (customer is null)
                {
                    return StatusCode((int)HttpStatusCode.Forbidden, "Wrong username or password");
                }

            }

            var tokenResponse = GenerateToken(customer);


            // activity log
            await _customerActivityService.InsertActivityAsync(customer, "Api.TokenRequest", "API token request", customer);

            return Json(tokenResponse);
        }

        #region Private methods

        private async Task<Customer> LoginAsync(string username, string password, bool rememberMe)
        {
            var result = await _customerRegistrationService.ValidateCustomerAsync(username, password);

            if (result == CustomerLoginResults.Successful)
            {
                var customer = await (_customerSettings.UsernamesEnabled
                                   ? _customerService.GetCustomerByUsernameAsync(username)
                                   : _customerService.GetCustomerByEmailAsync(username));

                _ = await _customerRegistrationService.SignInCustomerAsync(customer, null, rememberMe);

                return customer;
            }

            return null;
        }

        private int GetTokenExpiryInDays()
        {
            return _apiSettings.TokenExpiryInDays <= 0
                       ? Constants.Configurations.DefaultAccessTokenExpirationInDays
                       : _apiSettings.TokenExpiryInDays;
        }

        private TokenResponse GenerateToken(Customer customer)
        {
            var currentTime = DateTimeOffset.Now;
            var expiresInSeconds = currentTime.AddDays(GetTokenExpiryInDays()).ToUnixTimeSeconds();

            var claims = new List<Claim>
                         {
                             new Claim(JwtRegisteredClaimNames.Nbf, currentTime.ToUnixTimeSeconds().ToString()),
                             new Claim(JwtRegisteredClaimNames.Exp, expiresInSeconds.ToString()),
                             new Claim("CustomerId", customer.Id.ToString()),
                             new Claim(ClaimTypes.NameIdentifier, customer.CustomerGuid.ToString()),
                         };

            if (!string.IsNullOrEmpty(customer.Email))
            {
                claims.Add(new Claim(ClaimTypes.Email, customer.Email));
            }

            if (_customerSettings.UsernamesEnabled)
            {
                if (!string.IsNullOrEmpty(customer.Username))
                {
                    claims.Add(new Claim(ClaimTypes.Name, customer.Username));
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(customer.Email))
                {
                    claims.Add(new Claim(ClaimTypes.Email, customer.Email));
                }
            }

            var signingCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_apiConfiguration.SecurityKey)), SecurityAlgorithms.HmacSha256);
            var token = new JwtSecurityToken(new JwtHeader(signingCredentials), new JwtPayload(claims));
            var accessToken = new JwtSecurityTokenHandler().WriteToken(token);

            return new TokenResponse(accessToken, currentTime.UtcDateTime, expiresInSeconds)
            {
                CustomerId = customer.Id,
                CustomerGuid = customer.CustomerGuid,
                Username = _customerSettings.UsernamesEnabled ? customer.Username : customer.Email,
                TokenType = "Bearer"
            };
        }

        #endregion
    }
}

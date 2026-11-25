using FluentValidation;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Nop.Core.Domain.Customers;
using Nop.Plugin.Api.DTO.Customers;
using Nop.Plugin.Api.Helpers;

namespace Nop.Plugin.Api.Validators
{
    [UsedImplicitly]
    public class CustomerDtoValidator : BaseDtoValidator<CustomerDto>
    {
        #region Private Fields

        private readonly ICustomerRolesHelper _customerRolesHelper;

        #endregion

        #region Constructors

        public CustomerDtoValidator(
            IHttpContextAccessor httpContextAccessor, IJsonHelper jsonHelper, Dictionary<string, object> requestJsonDictionary,
            ICustomerRolesHelper customerRolesHelper) : base(httpContextAccessor, jsonHelper, requestJsonDictionary)
        {
            _customerRolesHelper = customerRolesHelper;

            SetEmailRule();
            SetRolesRule();
            SetPasswordRule();

            SetBillingAddressRule();
            SetShippingAddressRule();

            SetCustomerAddressesRule();
        }

        #endregion

        #region Private Methods

        private void SetCustomerAddressesRule()
        {
            if (!RequestJsonDictionary.TryGetValue("addresses", out var value) || value is not List<object> submittedAddresses)
                return;

            RuleFor(customer => customer.Addresses).Custom((addresses, validationContext) =>
            {
                foreach (var (addressDto, submittedAddress) in addresses.Zip(submittedAddresses))
                {
                    if (submittedAddress is not Dictionary<string, object> addressFields)
                    {
                        validationContext.AddFailure("addresses", "each address must be an object");
                        continue;
                    }

                    if (addressDto.Id == 0 && addressFields.ContainsKey("id"))
                    {
                        addressFields = new Dictionary<string, object>(addressFields);
                        addressFields.Remove("id");
                    }

                    var validator = new AddressDtoValidator(HttpContextAccessor, JsonHelper, addressFields);
                    MergeValidationResult(validationContext, validator.Validate(addressDto));
                }
            });
        }

        private void SetBillingAddressRule()
        {
            var key = "billing_address";
            if (RequestJsonDictionary.ContainsKey(key))
            {
                RuleFor(c => c.BillingAddress)
                    .SetValidator(new AddressDtoValidator(HttpContextAccessor, JsonHelper, (Dictionary<string, object>)RequestJsonDictionary[key]));
            }
        }

        private void SetShippingAddressRule()
        {
            var key = "shipping_address";
            if (RequestJsonDictionary.ContainsKey(key))
            {
                RuleFor(c => c.ShippingAddress)
                    .SetValidator(new AddressDtoValidator(HttpContextAccessor, JsonHelper, (Dictionary<string, object>)RequestJsonDictionary[key]));
            }
        }

        private void SetEmailRule()
        {
            SetNotNullOrEmptyCreateOrUpdateRule(c => c.Email, "invalid email", "email");
        }

        private void SetPasswordRule()
        {
            SetNotNullOrEmptyCreateOrUpdateRule(c => c.Password, "invalid password", "password");
        }

        private void SetRolesRule()
        {
            if (HttpMethod == HttpMethod.Post || RequestJsonDictionary.ContainsKey("role_ids"))
            {
                IList<CustomerRole> customerRoles = null;

                // async validation: https://docs.fluentvalidation.net/en/latest/async.html

                RuleFor(x => x.RoleIds)
                    .NotNull()
                    .Must(roles => roles.Count > 0)
                    .WithMessage("role_ids required")
                    .DependentRules(() => RuleFor(dto => dto.RoleIds)
                                          .MustAsync(async (roleIds, cancellation) =>
                                          {
                                              if (customerRoles == null)
                                              {
                                                  customerRoles = await _customerRolesHelper.GetValidCustomerRolesAsync(roleIds);
                                              }

                                              var isInGuestAndRegisterRoles = _customerRolesHelper.IsInGuestsRole(customerRoles) &&
                                                                              _customerRolesHelper.IsInRegisteredRole(customerRoles);

                                              // Customer can not be in guest and register roles simultaneously
                                              return !isInGuestAndRegisterRoles;
                                          })
                                          .WithMessage("must not be in guest and register roles simultaneously")
                                          .DependentRules(() => RuleFor(dto => dto.RoleIds)
                                                                .MustAsync(async (roleIds, cancellation) =>
                                                                {
                                                                    if (customerRoles == null)
                                                                    {
                                                                        customerRoles = await _customerRolesHelper.GetValidCustomerRolesAsync(roleIds);
                                                                    }

                                                                    var isInGuestOrRegisterRoles = _customerRolesHelper.IsInGuestsRole(customerRoles) ||
                                                                                                   _customerRolesHelper.IsInRegisteredRole(customerRoles);

                                                                    // Customer must be in either guest or register role.
                                                                    return isInGuestOrRegisterRoles;
                                                                })
                                                                .WithMessage("must be in guest or register role")
                                                         )
                                   );
            }
        }

        #endregion
    }
}

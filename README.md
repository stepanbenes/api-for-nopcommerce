# API plugin for nopCommerce 4.50

This plugin provides a RESTful API for managing resources in nopCommerce 4.50.

## Installation

1. clone the [NopCommerce](https://github.com/nopSolutions/nopCommerce) repository (`develop` branch) into folder called `nopCommerce`
1. clone this repository into the same folder where the `nopCommerce` folder is located
1. build the nopCommerce solution
1. build the api-for-nopcommerce solution (the output will be placed inside the nopCommerce directory)
1. run the Nop.Web project in the nopCommerce solution
1. install the nopCommerce database, create the admin user (skip this step if already done)
1. go to the administration page, Api plugin should be listed in local plugins configuration section.
1. in the customers section assign the  role `Api Users` to the user that will be used to access the api.
1. set fake appId and app secret in facebook authentication plugin as a workaround for [issue #17](https://github.com/stepanbenes/api-for-nopcommerce/issues/17#issuecomment-840502748).
1. go to `/api/swagger` page and experiment with the api (use the Authorize button to authenticate requests) or run .NET 5 client application (`ClientApp` project) that contains auto-generated Api client class generated from the swagger json file using C# 9 source generator.

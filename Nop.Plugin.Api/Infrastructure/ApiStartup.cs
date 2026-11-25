using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json.Converters;
using Nop.Core.Configuration;
using Nop.Core.Infrastructure;
using Nop.Plugin.Api.Attributes;
using Nop.Plugin.Api.Authorization.Policies;
using Nop.Plugin.Api.Authorization.Requirements;
using Nop.Plugin.Api.Configuration;
using Nop.Plugin.Api.Delta;
using Nop.Plugin.Api.DTO.Errors;
using Nop.Plugin.Api.Helpers;
using Nop.Plugin.Api.ModelBinders;
using System.Reflection;
using Swashbuckle.AspNetCore.SwaggerGen;


namespace Nop.Plugin.Api.Infrastructure
{
    internal class DeltaSchemaFilter : ISchemaFilter
    {
        public void Apply(OpenApiSchema schema, SchemaFilterContext context)
        {
            var type = context.Type;
            if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(Delta<>))
                return;

            var dtoType = type.GetGenericArguments()[0];
            var root = ReflectionHelper.GetJsonObjectAttribute(dtoType).Title;
            schema.Properties.Clear();
            schema.Properties[root] = context.SchemaGenerator.GenerateSchema(dtoType, context.SchemaRepository);
            schema.Required.Clear();
            schema.Required.Add(root);
        }
    }

    internal class ApiOperationFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            if (context.ApiDescription.ActionDescriptor.EndpointMetadata.OfType<IAuthorizeData>().Any() &&
                !context.ApiDescription.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any())
            {
                operation.Security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
                    }] = Array.Empty<string>()
                });
            }

            if (context.MethodInfo.DeclaringType == typeof(Controllers.OrdersController) &&
                context.MethodInfo.Name == nameof(Controllers.OrdersController.CreateOrder) &&
                operation.Responses.TryGetValue("200", out var successResponse))
            {
                successResponse.Headers["Idempotency-Replayed"] = new OpenApiHeader
                {
                    Description = "True when this request replays a previously committed order.",
                    Schema = new OpenApiSchema { Type = "boolean" }
                };
                successResponse.Headers["X-Order-Follow-Up"] = new OpenApiHeader
                {
                    Description = "Follow-up guidance when order placement succeeded and a subsequent step failed.",
                    Schema = new OpenApiSchema { Type = "string" }
                };
            }

            if (context.MethodInfo.IsDefined(typeof(UnpagedCollectionAttribute), inherit: true))
            {
                operation.Parameters.Add(new OpenApiParameter
                {
                    Name = UnpagedCollectionAttribute.ParameterName,
                    In = ParameterLocation.Query,
                    Description = "Return all matching results. Administrators only. Omit page and limit.",
                    Schema = new OpenApiSchema { Type = "boolean" }
                });
                if (!operation.Responses.ContainsKey("403"))
                {
                    operation.Responses["403"] = new OpenApiResponse
                    {
                        Description = "Unpaged collections require an administrator.",
                        Content =
                        {
                            ["application/json"] = new OpenApiMediaType
                            {
                                Schema = context.SchemaGenerator.GenerateSchema(typeof(ErrorsRootObject), context.SchemaRepository)
                            }
                        }
                    };
                }
            }

            var delta = context.MethodInfo.GetParameters().FirstOrDefault(parameter =>
                parameter.ParameterType.IsGenericType &&
                parameter.ParameterType.GetGenericTypeDefinition() == typeof(Delta<>));

            // MVC advertises aliases for JSON models. Publish the canonical representation once
            // when an alias refers to the same schema, so clients serialize and parse it as JSON.
            foreach (var response in operation.Responses.Values)
                RemoveJsonAliases(response.Content, "text/plain", "text/json");

            if (delta == null)
            {
                RemoveJsonAliases(operation.RequestBody?.Content,
                    "application/json-patch+json", "text/json", "application/*+json");
                return;
            }

            var createsOrder = context.MethodInfo.DeclaringType == typeof(Controllers.OrdersController) &&
                context.MethodInfo.Name == nameof(Controllers.OrdersController.CreateOrder);
            operation.RequestBody = new OpenApiRequestBody
            {
                Required = true,
                Description = createsOrder ? "Supply order.customer_id and order.billing_address with an address ID owned by the customer." : null,
                Content =
                {
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = createsOrder
                            ? GenerateCreateOrderRequestSchema(context)
                            : context.SchemaGenerator.GenerateSchema(delta.ParameterType, context.SchemaRepository)
                    }
                }
            };

            // The Delta binder reads numeric IDs directly from route data.
            var id = operation.Parameters.FirstOrDefault(parameter => parameter.In == ParameterLocation.Path && parameter.Name == "id");
            if (id != null)
                id.Schema = new OpenApiSchema { Type = "integer", Format = "int32" };
        }

        private static OpenApiSchema GenerateCreateOrderRequestSchema(OperationFilterContext context)
        {
            var repository = context.SchemaRepository;
            var addressRef = context.SchemaGenerator.GenerateSchema(typeof(DTO.AddressDto), repository);
            var address = new OpenApiSchema(repository.Schemas[addressRef.Reference.Id]);
            address.Properties = new Dictionary<string, OpenApiSchema>(address.Properties)
            {
                ["id"] = new OpenApiSchema(address.Properties["id"]) { Minimum = 1 }
            };
            address.Required = new HashSet<string>(address.Required) { "id" };
            var billingAddress = repository.AddDefinition("OrderBillingAddressDto", address);

            var orderRef = context.SchemaGenerator.GenerateSchema(typeof(DTO.Orders.OrderDto), repository);
            var order = new OpenApiSchema(repository.Schemas[orderRef.Reference.Id]);
            order.Properties = new Dictionary<string, OpenApiSchema>(order.Properties)
            {
                ["customer_id"] = new OpenApiSchema(order.Properties["customer_id"]) { Nullable = false, Minimum = 1 },
                ["billing_address"] = billingAddress
            };
            order.Required = new HashSet<string>(order.Required) { "customer_id", "billing_address" };

            return repository.AddDefinition("CreateOrderRequest", new OpenApiSchema
            {
                Type = "object",
                AdditionalPropertiesAllowed = false,
                Required = { "order" },
                Properties = { ["order"] = repository.AddDefinition("CreateOrderDto", order) }
            });
        }

        private static void RemoveJsonAliases(IDictionary<string, OpenApiMediaType> content, params string[] aliases)
        {
            if (content == null || !content.TryGetValue("application/json", out var json) ||
                json.Schema?.Reference?.Id is not { } schemaId)
                return;

            foreach (var alias in aliases)
            {
                if (content.TryGetValue(alias, out var representation) && representation.Schema?.Reference?.Id == schemaId)
                    content.Remove(alias);
            }
        }
    }

    public class ApiStartup : INopStartup
    {
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {

            var apiConfig = Singleton<AppSettings>.Instance.Get<ApiConfiguration>();
            var signingKey = apiConfig.GetSigningKey();
            services.AddAuthentication().AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = signingKey == null ? null : new SymmetricSecurityKey(signingKey),
                    ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(apiConfig.AllowedClockSkewInMinutes)
                };
            });
            AddAuthorizationPipeline(services);

            services.Configure<KestrelServerOptions>(options =>
            {
                options.AllowSynchronousIO = true;
            });
            services.Configure<IISServerOptions>(options =>
            {
                options.AllowSynchronousIO = true;
            });
            services.Configure<MvcNewtonsoftJsonOptions>(options =>
            {
                options.SerializerSettings.Converters.Add(new StringEnumConverter());
            });
            services.PostConfigure<ApiBehaviorOptions>(options =>
            {
                var defaultResponse = options.InvalidModelStateResponseFactory;
                options.InvalidModelStateResponseFactory = context =>
                {
                    if (context.ActionDescriptor is not ControllerActionDescriptor controller ||
                        controller.ControllerTypeInfo.Namespace != typeof(Controllers.TokenController).Namespace)
                        return defaultResponse(context);

                    var parameters = controller.MethodInfo.GetParameters();
                    var usesDelta = parameters.Any(parameter => parameter.ParameterType.IsGenericType &&
                        parameter.ParameterType.GetGenericTypeDefinition() == typeof(Delta<>));
                    var usesQueryBinder = parameters.Any(parameter =>
                        parameter.ParameterType.GetCustomAttribute<ModelBinderAttribute>()?.BinderType is { IsGenericType: true } binder &&
                        binder.GetGenericTypeDefinition() == typeof(ParametersModelBinder<>));
                    if (!usesDelta && !usesQueryBinder)
                        return defaultResponse(context);

                    var errors = context.ModelState
                        .Where(item => item.Value.Errors.Count > 0)
                        .ToDictionary(item => item.Key, item => item.Value.Errors
                            .Select(error => string.IsNullOrEmpty(error.ErrorMessage) ? "Invalid value." : error.ErrorMessage)
                            .ToList());
                    var response = new ErrorsRootObject { Errors = errors };
                    return usesDelta ? new UnprocessableEntityObjectResult(response) : new BadRequestObjectResult(response);
                };
            });
            services.AddSwaggerGen(options =>
            {
                // api description >>
                options.SwaggerDoc("v1", new OpenApiInfo { Title = "Nop API", Version = "v1" });
                options.DocInclusionPredicate((_, description) =>
                    description.ActionDescriptor is ControllerActionDescriptor controller &&
                    controller.ControllerTypeInfo.Namespace == typeof(Controllers.TokenController).Namespace);
                // auth definition >>
                options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    In = ParameterLocation.Header,
                    Description = "Please enter into field the word 'Bearer' following by space and JWT",
                    Name = "Authorization",
                    Type = SecuritySchemeType.ApiKey
                });
                // custom type mappings >>
                options.MapType<decimal>(() => new OpenApiSchema { Type = "number", Format = "decimal" }); // correct currency typings
                options.SchemaFilter<DeltaSchemaFilter>();
                options.SchemaFilter<NullableEnumSchemaFilter>();
                options.ParameterFilter<NullableEnumSchemaFilter>();
                options.OperationFilter<ApiOperationFilter>();
                // TODO: options.UseAllOfToExtendReferenceSchemas(); // https://github.com/stepanbenes/api-for-nopcommerce/issues/16
            });
            services.AddSwaggerGenNewtonsoftSupport();


        }

        public void Configure(IApplicationBuilder app)
        {
            var environment = app.ApplicationServices.GetRequiredService<IWebHostEnvironment>();

            var rewriteOptions = new RewriteOptions().AddRewrite("^api/token(/check)?$", "/token$1", true);
            app.UseRewriter(rewriteOptions);

            app.UseCors(x => x
                     .AllowAnyOrigin()
                     .AllowAnyMethod()
                     .AllowAnyHeader());


            app.MapWhen(context => context.Request.Path.StartsWithSegments(new PathString("/api")),
              a =>
              {
                  if (environment.IsDevelopment())
                  {
                      a.UseDeveloperExceptionPage();
                  }

                  a.Use(async (context, next) =>
            {
                // API Call
                context.Request.EnableBuffering();
                await next();
            });

                  a.UseExceptionHandler(a => a.Run(async context =>
            {
                var exceptionHandlerPathFeature = context.Features.Get<IExceptionHandlerPathFeature>();
                var exception = exceptionHandlerPathFeature.Error;
                await context.Response.WriteAsJsonAsync(new { error = exception.Message });
            }));

                  a.UseRouting();
                  a.UseAuthentication();
                  a.UseAuthorization();
                  a.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });

                  // swagger configuration
                  {
                      // http://eatcodelive.com/2017/05/19/change-default-swagger-route-in-an-asp-net-core-web-api/

                      a.UseSwagger(options => options.RouteTemplate = "api/swagger/{documentName}/swagger.json");
                      a.UseSwaggerUI(c =>
                {
                    c.RoutePrefix = "api/swagger";
                    c.SwaggerEndpoint("/api/swagger/v1/swagger.json", "Nop.Plugin.Api v5.00");
                    c.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.None);
                });
                  }
              }
            );
        }

        public int Order => 1;

        private static void AddAuthorizationPipeline(IServiceCollection services)
        {
            services.AddAuthorization(options =>
            {
                options.AddPolicy(JwtBearerDefaults.AuthenticationScheme,
                    policy =>
                    {
                        policy.Requirements.Add(new ActiveApiPluginRequirement());
                        policy.Requirements.Add(new AuthorizationSchemeRequirement());
                        policy.Requirements.Add(new CustomerRoleRequirement());
                        policy.RequireAuthenticatedUser();
                    });
            });

            services.AddSingleton<IAuthorizationHandler, ActiveApiPluginAuthorizationPolicy>();
            services.AddSingleton<IAuthorizationHandler, ValidSchemeAuthorizationPolicy>();
            services.AddScoped<IAuthorizationHandler, CustomerRoleAuthorizationPolicy>();

        }
    }
}

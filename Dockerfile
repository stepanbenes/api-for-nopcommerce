FROM mcr.microsoft.com/dotnet/sdk:10.0 AS nop-build

WORKDIR /src/nopCommerce
COPY --from=nopcommerce /global.json .
COPY --from=nopcommerce /src ./src
RUN dotnet build src/NopCommerce.sln -c Release
RUN dotnet publish src/Presentation/Nop.Web/Nop.Web.csproj -c Release --no-build -o /app/published

FROM nop-build AS plugin-build

ARG BUILD_BUILDNUMBER
COPY Nop.Plugin.Api /src/api-for-nopcommerce/Nop.Plugin.Api
RUN dotnet build /src/api-for-nopcommerce/Nop.Plugin.Api/Nop.Plugin.Api.csproj -c Release -p:NopRoot=/src/nopCommerce

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS nop-runtime

RUN apt-get update \
    && apt-get install --no-install-recommends -y libgdiplus \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=nop-build /app/published .
RUN mkdir -p bin logs
EXPOSE 8080
ENTRYPOINT ["dotnet", "Nop.Web.dll"]

FROM nop-runtime AS runtime

COPY --from=plugin-build /src/nopCommerce/src/Presentation/Nop.Web/Plugins/Nop.Plugin.Api ./Plugins/Nop.Plugin.Api

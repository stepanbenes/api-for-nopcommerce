# The base working directory is supposed to be parent directory. In the parent directory, there must be nopCommerce and api-for-nopcommerce subdirectories.

# create the build instance
FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build

WORKDIR /
COPY ./nopCommerce/src ./nopCommerce/src

COPY ./api-for-nopcommerce/Nop.Plugin.Api ./api-for-nopcommerce/Nop.Plugin.Api

# restore solution
WORKDIR /nopCommerce/src
RUN dotnet restore NopCommerce.sln

# build nop api plugin, output binaries are stored in plugins directory
WORKDIR /api-for-nopcommerce/Nop.Plugin.Api
RUN dotnet restore Nop.Plugin.Api.csproj
RUN dotnet build Nop.Plugin.Api.csproj -c Release

WORKDIR /nopCommerce/src/Presentation/Nop.Web

# build project
RUN dotnet build Nop.Web.csproj -c Release

# build api plugin
WORKDIR /api-for-nopcommerce/Nop.Plugin.Api
RUN dotnet build Nop.Plugin.Api.csproj -c Release

# publish project
WORKDIR /nopCommerce/src/Presentation/Nop.Web   
RUN dotnet publish Nop.Web.csproj -c Release -o /app/published

# create the runtime instance 
FROM mcr.microsoft.com/dotnet/aspnet:6.0-alpine AS runtime

RUN apk add --no-cache icu-dev fontconfig libc-dev && apk add --no-cache libgdiplus --repository=http://dl-cdn.alpinelinux.org/alpine/edge/testing/
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

# Debian linux >>>
# FROM mcr.microsoft.com/dotnet/aspnet:5.0-buster-slim AS runtime
# RUN apt-get update
# RUN apt-get install -y libicu-dev fontconfig-config
# ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
# RUN apt-get install -y libgdiplus linux-libc-dev

# copy entrypoint script
COPY ./nopCommerce/entrypoint.sh /entrypoint.sh
RUN chmod 755 /entrypoint.sh

WORKDIR /app
RUN mkdir bin
RUN mkdir logs

COPY --from=build /app/published .

COPY ./api-for-nopcommerce/plugins-docker.json /app/App_Data/plugins.json
COPY ./api-for-nopcommerce/appsettings-docker.json /app/App_Data/appsettings.json

# call entrypoint script instead of dotnet
ENTRYPOINT "/entrypoint.sh"

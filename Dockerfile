# Build 階段
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY . .

RUN dotnet restore
RUN dotnet publish -c Release -o /app/publish

# Runtime 階段
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

ENV DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false
ENV DOTNET_USE_POLLING_FILE_WATCHER=true


COPY --from=build /app/publish .

EXPOSE 8080

ENTRYPOINT ["dotnet", "WebLoginDemo2.dll"]